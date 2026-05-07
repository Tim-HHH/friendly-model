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

        public void Dispose() => onnx_infer?.Dispose();
    }
}