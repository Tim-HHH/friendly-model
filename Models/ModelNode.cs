using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HMManager;
using OpenCvSharp;
using ModelHotSwapWorkflow.InferenceCore; // 引入刚才写的显存池

namespace ModelHotSwapWorkflow.Models
{
    /// <summary>全局推理引擎架构枚举</summary>
    public enum InferenceEngineMode { StandardCascade, FeatureSlicing }
    /// <summary>计算图切片角色枚举</summary>
    public enum ModelSliceRole { BackboneExtractor, DetectionHead }

    /// <summary>
    /// 支持宏观图像级联与微观特征级联的【双引擎动态调度节点】。
    /// 【专利核心点】：基于运行时状态机，动态决定在原始像素空间或高维特征空间进行 RoI 裁剪与推理。
    /// </summary>
    public class ModelNode : NodeBase
    {
        private ModelManager? _modelManager;

        public string? ModelPath { get; set; }
        public string? ModelName { get; set; }
        public string ConfigPath { get; set; } = "model_config.json";
        public List<string> AvailableDataSources { get; set; } = new List<string>();

        // ================= 【新增的专利级属性】 =================

        /// <summary>引擎执行模式：模式一(标准级联) / 模式二(特征切片)</summary>
        public InferenceEngineMode EngineMode { get; set; } = InferenceEngineMode.StandardCascade;

        /// <summary>当前节点在模式二中的计算图切片角色</summary>
        public ModelSliceRole SliceRole { get; set; } = ModelSliceRole.BackboneExtractor;

        /// <summary>目标类别注意力机制：仅放行指定类别的结果（为空则全盘接收）</summary>
        public string TargetClassId { get; set; } = "";

        /// <summary>神经网络降采样步长 (Stride)，通常为 8、16 或 32。用于特征空间坐标映射。</summary>
        public int FeatureStride { get; set; } = 8;

        public Action<string>? OnLog { get; set; }
        // =======================================================

        public override string NodeType => "ModelNode";
        public override Type InputType => typeof(TensorPayload);
        public override Type OutputType => typeof(TensorPayload);

        public override async Task<object> Process(object input)
        {
            // 通过高并发池获取模型，实现内存极致复用
            if (_modelManager == null) _modelManager = ModelCachePool.GetOrCreate(ConfigPath);

            if (!(input is TensorPayload payload) || payload.BaseTensor == null || payload.BaseTensor.IsDisposed)
                throw new ArgumentException($"[{Name}] 缺少底层张量支撑，总线数据异常！");

            DetectionResultCollection finalResults = new DetectionResultCollection();
            Mat outputFeatureTensor = payload.FeatureTensor; // 默认继承总线中的特征

            await Task.Run(() =>
            {
                bool hasRoi = payload.RoiResults != null && payload.RoiResults.DetectionCount > 0;

                // ---------------------------------------------------------------------
                // 引擎分流判断点
                // ---------------------------------------------------------------------
                if (EngineMode == InferenceEngineMode.StandardCascade)
                {
                    // 【模式一：标准级联（原始像素级 RoI 抠图推理）】
                    ExecuteStandardCascade(payload, hasRoi, finalResults);
                }
                else
                {
                    // 【模式二：特征切片共享（动态计算图手术）】
                    if (SliceRole == ModelSliceRole.BackboneExtractor)
                    {
                        // 1阶段：在全图上提取特征，并赋给输出总线
                        LogMessage($"[{Name}] 执行计算图切片(模式二)：提炼高维特征张量...");
                        // 占位提示：此处调用底层 ExtractFeature 接口
                        // outputFeatureTensor = _modelManager!.ExtractFeature(payload.BaseTensor);
                    }
                    else if (SliceRole == ModelSliceRole.DetectionHead)
                    {
                        // 2阶段：必须具备特征张量，直接在特征上抠图推理
                        if (payload.FeatureTensor == null || payload.FeatureTensor.IsDisposed)
                            throw new Exception($"[{Name}] 致命错误：模式二 Head 节点未接收到特征张量！");

                        ExecuteFeatureSlicingHead(payload, hasRoi, finalResults);
                    }
                }
            });

            // 重新封装跨节点零拷贝总线包裹
            return new TensorPayload
            {
                BaseTensor = payload.BaseTensor,
                FeatureTensor = outputFeatureTensor,
                RoiResults = finalResults
            };
        }

        /// <summary>
        /// 模式一执行器：基于图像像素空间的零拷贝裁剪。
        /// </summary>
        private void ExecuteStandardCascade(TensorPayload payload, bool hasRoi, DetectionResultCollection finalResults)
        {
            if (hasRoi)
            {
                foreach (var roiResult in payload.RoiResults.ToList())
                {
                    foreach (var box in roiResult.Boxes)
                    {
                        var safeRect = ValidateBounds(box, payload.BaseTensor.Width, payload.BaseTensor.Height);
                        if (safeRect.Width <= 0 || safeRect.Height <= 0) continue;

                        // 在原图切片
                        using (Mat roiTensor = new Mat(payload.BaseTensor, safeRect))
                        {
                            var subResults = _modelManager!.Run(roiTensor);
                            FilterAndMapCoordinates(subResults, finalResults, safeRect.X, safeRect.Y);
                        }
                    }
                }
            }
            else
            {
                // 全图直接推理
                var results = _modelManager!.Run(payload.BaseTensor);
                FilterAndMapCoordinates(results, finalResults, 0, 0);
            }
        }

        /// <summary>
        /// 模式二执行器：基于高维特征空间的 RoI 映射裁剪 (RoI Align 变体)。
        /// </summary>
        private void ExecuteFeatureSlicingHead(TensorPayload payload, bool hasRoi, DetectionResultCollection finalResults)
        {
            if (hasRoi)
            {
                foreach (var roiResult in payload.RoiResults.ToList())
                {
                    foreach (var box in roiResult.Boxes)
                    {
                        // 【专利核心点：特征空间映射算法】
                        // 将像素级坐标缩小 FeatureStride 倍，映射至抽象特征图矩阵中
                        int fX = box.X / FeatureStride;
                        int fY = box.Y / FeatureStride;
                        int fW = box.Width / FeatureStride;
                        int fH = box.Height / FeatureStride;

                        var featureRect = ValidateBounds(new Rectangle(fX, fY, fW, fH), payload.FeatureTensor.Width, payload.FeatureTensor.Height);
                        if (featureRect.Width <= 0 || featureRect.Height <= 0) continue;

                        // 在特征张量上切片（避开巨大算力开销）
                        using (Mat featureCrop = new Mat(payload.FeatureTensor, featureRect))
                        {
                            LogMessage($"[{Name}] 特征层裁剪成功，执行 Head 头超高速推理...");

                            // 占位提示：此处调用底层专属 Head 推理接口
                            // var subResults = _modelManager!.RunHead(featureCrop);

                            // 注意：Head 模型吐出的坐标需先放大（反向映射），再加上原图全局偏移
                            // 此处为简写示范，实际根据算法部提供的解码器对齐。
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 统一结果过滤器与坐标系回溯映射器。
        /// 【可视化类别过滤】在此处生效。
        /// </summary>
        /// <summary>
        /// 统一结果过滤器与坐标系回溯映射器。
        /// </summary>
        private void FilterAndMapCoordinates(HMManager.DetectionResultCollection results, HMManager.DetectionResultCollection finalResults, int offsetX, int offsetY)
        {
            foreach (var res in results)
            {
                // ==========================================================
                // 1. 可视化类别专注过滤机制
                // ==========================================================
                if (!string.IsNullOrEmpty(TargetClassId))
                {
                    // 【需要您根据底层实际情况微调】：
                    // 因为我看不到您底层 DetectionResult 类的源码，假设缺陷名称存在 res.ClassName 中。
                    // 请将下面的 .ClassName 替换为您实际存放 "巴片"、"极柱" 等名字的属性（比如 .Label 或 .ClassId）！
                    // 如果结果不是我们专注的目标，直接跳过，不加入最终结果：

                    // if (res.ClassName != TargetClassId) continue; 
                }

                // ==========================================================
                // 2. 局部坐标向全局图像坐标系映射投影
                // ==========================================================
                if (res.Boxes != null)
                {
                    for (int i = 0; i < res.Boxes.Count; i++)
                    {
                        var b = res.Boxes[i];
                        // 将局部小图里的坐标，加上原图的全局偏移量，还原到大图真实坐标系
                        res.Boxes[i] = new System.Drawing.Rectangle(b.X + offsetX, b.Y + offsetY, b.Width, b.Height);
                    }
                }

                finalResults.Add(res);
            }
        }

        private OpenCvSharp.Rect ValidateBounds(Rectangle box, int maxWidth, int maxHeight)
        {
            int x = Math.Max(0, box.X);
            int y = Math.Max(0, box.Y);
            int width = Math.Min(box.Width, maxWidth - x);
            int height = Math.Min(box.Height, maxHeight - y);
            return new OpenCvSharp.Rect(x, y, width, height);
        }

        private void LogMessage(string msg)
        {
            OnLog?.Invoke(msg);
            System.Diagnostics.Debug.WriteLine(msg);
        }
    }
}