using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HMManager;
using OpenCvSharp;

namespace ModelHotSwapWorkflow.Models
{
    /// <summary>
    /// 深度学习推理节点：支持全图推理与基于 ROI 的零拷贝多级级联推理。
    /// 基于张量传递机制 (Tensor Transmission) 优化跨节点内存分配。
    /// </summary>
    public class ModelNode : NodeBase
    {
        private ModelManager? _modelManager;

        public string? ModelPath { get; set; }
        public string? ModelName { get; set; }
        public string ConfigPath { get; set; } = "model_config.json";
        public List<string> AvailableDataSources { get; set; } = new List<string>();

        public override string NodeType => "ModelNode";
        public override Type InputType => typeof(TensorPayload);
        public override Type OutputType => typeof(TensorPayload);

        /// <summary>
        /// 切换节点启用状态。
        /// 休眠时显式调用 Dispose() 释放底层的 ONNX 推理会话及 GPU 显存资源。
        /// </summary>
        public override void ToggleEnableState()
        {
            base.ToggleEnableState();

            if (!this.IsEnabled)
            {
                if (_modelManager != null)
                {
                    _modelManager.Dispose();
                    _modelManager = null;
                }
            }
            else
            {
                try
                {
                    InitializeEngine();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[{Name}] 唤醒初始化失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 异步处理输入张量数据。
        /// 采用内存指针偏移 (Zero-Copy Slicing) 提取 ROI 张量，执行推理后向系统下游透传聚合载体。
        /// </summary>
        /// <param name="input">上游传递的标准 TensorPayload 载体。</param>
        /// <returns>封装了本阶段推理结果的新 TensorPayload 对象。</returns>
        public override async Task<object> Process(object input)
        {
            InitializeEngine();

            if (!(input is TensorPayload payload) || payload.BaseTensor == null || payload.BaseTensor.IsDisposed)
            {
                throw new ArgumentException($"[{Name}] 推理失败：输入数据并非有效的 TensorPayload，或者底层张量已被释放。");
            }

            DetectionResultCollection finalResults = new DetectionResultCollection();

            await Task.Run(() =>
            {
                if (payload.RoiResults != null && payload.RoiResults.DetectionCount > 0)
                {
                    // 级联模式：针对每一个特征区域进行局部推理
                    var roiItems = payload.RoiResults.ToList();
                    foreach (var roiResult in roiItems)
                    {
                        foreach (var box in roiResult.Boxes)
                        {
                            // 构造安全边界，防止张量越界访问
                            var safeRect = ValidateRoiBounds(box, payload.BaseTensor.Width, payload.BaseTensor.Height);
                            if (safeRect.Width <= 0 || safeRect.Height <= 0) continue;

                            // 【核心技术：零拷贝张量切片 (Zero-Copy Tensor Slicing)】
                            // 此操作不复制像素内存，仅创建一个新的矩阵头指向原 BaseTensor 的偏移地址
                            using (Mat roiTensor = new Mat(payload.BaseTensor, safeRect))
                            {
                                var subResults = _modelManager!.Run(roiTensor);

                                // 坐标系映射：将局部张量坐标映射回全局特征图坐标
                                foreach (var subRes in subResults)
                                {
                                    for (int i = 0; i < subRes.Boxes.Count; i++)
                                    {
                                        var b = subRes.Boxes[i];
                                        subRes.Boxes[i] = new Rectangle(b.X + safeRect.X, b.Y + safeRect.Y, b.Width, b.Height);
                                    }
                                    finalResults.Add(subRes);
                                }
                            }
                        }
                    }
                }
                else
                {
                    // 全图模式：直接传入原始张量进行全尺寸特征提取
                    var results = _modelManager!.Run(payload.BaseTensor);
                    foreach (var res in results) finalResults.Add(res);
                }
            });

            // 构造新的输出载体：透传原始图像张量，并附带本阶段的检测结果
            TensorPayload outputPayload = new TensorPayload
            {
                BaseTensor = payload.BaseTensor, // 引用传递，极低开销
                RoiResults = finalResults
            };

            return outputPayload;
        }

        /// <summary>
        /// 校验并纠正感兴趣区域（ROI）的几何边界，避免引发内存访问越界异常 (AccessViolationException)。
        /// </summary>
        private OpenCvSharp.Rect ValidateRoiBounds(Rectangle box, int maxWidth, int maxHeight)
        {
            int x = Math.Max(0, box.X);
            int y = Math.Max(0, box.Y);
            int width = Math.Min(box.Width, maxWidth - x);
            int height = Math.Min(box.Height, maxHeight - y);
            return new OpenCvSharp.Rect(x, y, width, height);
        }

        private void InitializeEngine()
        {
            if (_modelManager == null)
            {
                if (!File.Exists(ConfigPath)) throw new FileNotFoundException($"配置文件缺失: {ConfigPath}");
                _modelManager = new ModelManager(ConfigPath);
            }
        }
    }
}