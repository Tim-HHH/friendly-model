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
        // 【替换为这句，让每个节点生成独一无二的配置文件】
        public string ConfigPath { get; set; } = $"model_config_{Guid.NewGuid():N}.json";
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
                        LogMessage($"[{Name}] 执行计算图切片(模式二)：提炼高维特征张量...");
                        // 【通电】：真实呼叫特征抽取接口
                        outputFeatureTensor = _modelManager!.ExtractFeature(payload.BaseTensor);
                    }
                    else if (SliceRole == ModelSliceRole.DetectionHead)
                    {
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
                        int fX = box.X / FeatureStride;
                        int fY = box.Y / FeatureStride;
                        int fW = box.Width / FeatureStride;
                        int fH = box.Height / FeatureStride;

                        var featureRect = ValidateBounds(new Rectangle(fX, fY, fW, fH), payload.FeatureTensor.Width, payload.FeatureTensor.Height);
                        if (featureRect.Width <= 0 || featureRect.Height <= 0) continue;

                        using (Mat featureCrop = new Mat(payload.FeatureTensor, featureRect))
                        {
                            LogMessage($"[{Name}] 特征层裁剪成功，执行 Head 头超高速推理...");

                            // 【通电】：执行纯张量推理
                            var subResults = _modelManager!.RunHead(featureCrop);

                            // 【坐标反演】：将特征图里的目标框，平移加上原图大框的起始点
                            FilterAndMapCoordinates(subResults, finalResults, box.X, box.Y);
                        }
                    }
                }
            }
            else
            {
                // 【新增】：如果模式二没收到检测框，就对整张特征图全盘扫描！
                LogMessage($"[{Name}] 未收到上游 RoI，全盘执行 Head 推理...");
                var subResults = _modelManager!.RunHead(payload.FeatureTensor);
                FilterAndMapCoordinates(subResults, finalResults, 0, 0);
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
                var filteredRes = new HMManager.DetectionResult
                {
                    taskType = res.taskType,
                    ClassDefinitions = res.ClassDefinitions,
                    Boxes = new List<Rectangle>(),
                    Scores = new List<float>(),
                    ClassIds = new List<int>(),
                    ClassNames = new List<string>()
                };

                if (res.Boxes != null)
                {
                    for (int i = 0; i < res.Boxes.Count; i++)
                    {
                        string className = res.ClassNames != null && res.ClassNames.Count > i ? res.ClassNames[i] : "";
                        int classId = res.ClassIds != null && res.ClassIds.Count > i ? res.ClassIds[i] : -1;

                        // 1. 可视化类别专注过滤机制 (针对单个框进行检查)
                        if (!string.IsNullOrEmpty(TargetClassId))
                        {
                            if (classId.ToString() != TargetClassId && className != TargetClassId)
                                continue; // 只有这个框不是目标时，跳过这个框
                        }

                        // 2. 局部坐标向全局图像坐标系映射投影
                        var b = res.Boxes[i];
                        filteredRes.Boxes.Add(new System.Drawing.Rectangle(b.X + offsetX, b.Y + offsetY, b.Width, b.Height));
                        filteredRes.Scores.Add(res.Scores[i]);
                        filteredRes.ClassIds.Add(classId);
                        if (res.ClassNames != null) filteredRes.ClassNames.Add(className);
                        filteredRes.DetectionCount++;
                    }
                }

                if (filteredRes.DetectionCount > 0)
                    finalResults.Add(filteredRes);
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