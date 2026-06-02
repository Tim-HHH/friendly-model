using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace HMManager
{
    public static class Logger
    {
        public static void LogInfo(string m) => Debug.WriteLine($"[INFO] {m}");
        public static void LogWarning(string m) => Debug.WriteLine($"[WARN] {m}");
        public static void LogError(string m, Exception e = null) => Debug.WriteLine($"[ERROR] {m} {e?.Message}");
    }

    public class ModelManager : IDisposable
    {
        private Predictor _predictor;

        public ModelManager(string configPath)
        {
            if (!File.Exists(configPath)) throw new FileNotFoundException($"配置不存在: {configPath}");
            string json = File.ReadAllText(configPath);
            var configColl = JsonSerializer.Deserialize<ModelConfigCollection>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (configColl == null || configColl.Count == 0) throw new Exception("配置为空");

            ModelConfig config = configColl[0];
            if (!Path.IsPathRooted(config.ModelPath))
                config.ModelPath = Path.Combine(Path.GetDirectoryName(configPath), config.ModelPath.Replace(".\\", ""));

            _predictor = new Predictor(config);
        }

        public DetectionResultCollection Run(Bitmap inputBitmap)
        {
            using (Mat mat = OpenCvSharp.Extensions.BitmapConverter.ToMat(inputBitmap))
            {
                var col = new DetectionResultCollection();
                var result = _predictor.RunInference(mat);
                if (result != null) col.Add(result);
                return col;
            }
        }

        // 请将此方法添加到 InferenceCore/ModelManager.cs 的 ModelManager 类中

        /// <summary>
        /// 执行底层推理（张量直通重载版本）。
        /// 绕过 Bitmap 的装箱与拆箱过程，直接将 OpenCV Mat 投递至 ONNX 运行时执行器，显著降低 I/O 延迟。
        /// </summary>
        /// <param name="inputTensor">输入的图像张量/矩阵。</param>
        /// <returns>检测结果集合。</returns>
        public DetectionResultCollection Run(Mat inputTensor)
        {
            var col = new DetectionResultCollection();
            // 直接复用传入的连续内存块进入推理管线
            var result = _predictor.RunInference(inputTensor);
            if (result != null) col.Add(result);
            return col;
        }
        // ================= 【模式二专属接口】 =================
        public Mat ExtractFeature(Mat inputTensor)
        {
            return _predictor.RunBackbone(inputTensor);
        }

        public DetectionResultCollection RunHead(Mat featureCrop)
        {
            var col = new DetectionResultCollection();
            var result = _predictor.RunHead(featureCrop);
            if (result != null) col.Add(result);
            return col;
        }
        // ====================================================


        public void Dispose() => _predictor?.Dispose();
    }

    public class Predictor : IDisposable
    {
        private InferenceSession onnx_infer;
        private ModelConfig _config;
        private IylResultParser parser;

        public Predictor(ModelConfig config)
        {
            _config = config;
            parser = _config.TaskType == TaskType.Detection
                ? (IylResultParser)new yl8DetectionParser(_config.ClassDefinitions, _config.OutputConfig[0])
                : new yl86ClassificationParser(_config.ClassDefinitions, _config.OutputConfig[0]);
            InitializeSession();
        }

        private void InitializeSession()
        {
            byte[] bytes = new FileEncryptor().DecryptFile(_config.ModelPath);
            bool gpuLoaded = false;

            // -------- 核心：智能降级机制 --------
            if (_config.PerformanceSettings.UseGpu)
            {
                try
                {
                    var optGpu = new SessionOptions();
                    optGpu.AppendExecutionProvider_CUDA(_config.PerformanceSettings.GpuId);
                    onnx_infer = new InferenceSession(bytes, optGpu);
                    gpuLoaded = true;
                    Logger.LogInfo("成功：已在现场环境启用 GPU (CUDA) 加速！");
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"现场无显卡或CUDA环境缺失，自动回退到纯 CPU 模式！({ex.Message})");
                }
            }

            if (!gpuLoaded)
            {
                var optCpu = new SessionOptions();
                optCpu.AppendExecutionProvider_CPU(0);
                optCpu.IntraOpNumThreads = _config.PerformanceSettings.Threads;
                onnx_infer = new InferenceSession(bytes, optCpu);
                Logger.LogInfo("成功：已启用 CPU 推理模式！");
            }
        }

        public DetectionResult RunInference(Mat inputImage)
        {
            var processor = new ImageProcessor(_config.InputConfig.Height, _config.InputConfig.Width, MatType.CV_32F);
            var (floatArray, ratio, padding) = processor.ProcessImage(inputImage, _config.InputConfig.Channels);

            var inputTensor = new DenseTensor<float>(floatArray, new[] { 1, _config.InputConfig.Channels, _config.InputConfig.Height, _config.InputConfig.Width });
            var outputs = onnx_infer.Run(new[] { NamedOnnxValue.CreateFromTensor("images", inputTensor) });

            return parser.Parse(outputs, new OpenCvSharp.Size(inputImage.Width, inputImage.Height), ratio[0], padding.padW, padding.padH);
        }


        // ================= 【模式二底层引擎：张量排布转换】 =================
        /// <summary>
        /// 抽取骨干网络的高维特征 (跳过解析器，返回纯数学张量)
        /// </summary>
        public Mat RunBackbone(Mat inputImage)
        {
            var processor = new ImageProcessor(_config.InputConfig.Height, _config.InputConfig.Width, MatType.CV_32F);
            var (floatArray, _, _) = processor.ProcessImage(inputImage, _config.InputConfig.Channels);

            // 获取切片模型真实的输入名（比如 "images"）
            string inputName = onnx_infer.InputMetadata.Keys.First();
            var inputTensor = new DenseTensor<float>(floatArray, new[] { 1, _config.InputConfig.Channels, _config.InputConfig.Height, _config.InputConfig.Width });

            var outputs = onnx_infer.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, inputTensor) });
            var outTensor = outputs.First().AsTensor<float>();

            // 【替换为这段自适应维度的代码】
            var dims = outTensor.Dimensions;
            int C = 1, H = 1, W = 1;

            if (dims.Length == 4)
            {
                // 标准 4D 张量: [Batch, Channels, Height, Width]
                C = dims[1];
                H = dims[2];
                W = dims[3];
            }
            else if (dims.Length == 3)
            {
                // 被压缩掉 Batch 的 3D 张量: [Channels, Height, Width]
                C = dims[0];
                H = dims[1];
                W = dims[2];
            }
            else
            {
                // 如果遇到其他奇葩维度，直接拦截报错，防止系统崩溃
                throw new Exception($"张量维度异常！期望3维或4维空间特征图，但实际收到 {dims.Length} 维数据。");
            }

            // 【核心专利点】：ONNX 输出是 [C, H, W] 的层叠矩阵，
            // 为了让 OpenCV 能够对其进行 ROI 矩形裁剪，必须将其重排为 [H, W, C] 的交织矩阵！
            Mat featureMat = new Mat(H, W, MatType.CV_32FC(C));
            float[] interleaved = new float[H * W * C];
            var outSpan = outTensor.ToArray();

            for (int c = 0; c < C; c++)
            {
                for (int y = 0; y < H; y++)
                {
                    for (int x = 0; x < W; x++)
                    {
                        interleaved[(y * W + x) * C + c] = outSpan[(c * H + y) * W + x];
                    }
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(interleaved, 0, featureMat.Data, interleaved.Length);
            return featureMat;
        }

        /// <summary>
        /// 在裁剪后的高维特征上运行检测头 (跳过图片预处理)
        /// </summary>
       // 【这个放在文件下方的 Predictor 类里】
        public DetectionResult RunHead(Mat featureCrop)
        {
            // 特征图如果经过 RoI 裁剪，内存是不连续的，必须 Clone 强制连续化
            using (Mat continuousCrop = featureCrop.IsContinuous() ? featureCrop : featureCrop.Clone())
            {
                int H = continuousCrop.Rows;
                int W = continuousCrop.Cols;
                int C = continuousCrop.Channels();

                float[] interleaved = new float[H * W * C];
                System.Runtime.InteropServices.Marshal.Copy(continuousCrop.Data, interleaved, 0, interleaved.Length);

                // [H, W, C] 转换回 [1, C, H, W]
                float[] planar = new float[C * H * W];
                for (int c = 0; c < C; c++)
                {
                    for (int y = 0; y < H; y++)
                    {
                        for (int x = 0; x < W; x++)
                        {
                            planar[(c * H + y) * W + x] = interleaved[(y * W + x) * C + c];
                        }
                    }
                }

                try
                {
                    // 【关键探针】：扫描这个 Head 模型到底需要几个输入接口？
                    if (onnx_infer.InputMetadata.Count > 1)
                    {
                        string needed = string.Join("\n - ", onnx_infer.InputMetadata.Keys);
                        throw new Exception($"架构阻断！此 Head 模型实际上需要 {onnx_infer.InputMetadata.Count} 个输入端：\n - {needed}\n\n原因：YOLO 存在跳跃连接(FPN)，切割点太浅导致藕断丝连。当前的单一张量总线无法同时传输多个特征！");
                    }

                    string inputName = onnx_infer.InputMetadata.Keys.First();
                    var inputTensor = new DenseTensor<float>(planar, new[] { 1, C, H, W });

                    var outputs = onnx_infer.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, inputTensor) });

                    return parser.Parse(outputs, new OpenCvSharp.Size(99999, 99999), 1.0, 0, 0);
                }
                catch (Exception ex)
                {
                    throw new Exception($"ONNX底层拒绝了形状为 [1, {C}, {H}, {W}] 的张量！\n原生报错：{ex.Message}\n请检查您的模型是否支持动态长宽输入。");
                }
            }
        }





        public void Dispose() => onnx_infer?.Dispose();
    }
}