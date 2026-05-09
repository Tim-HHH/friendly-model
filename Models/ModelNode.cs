using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HMManager;

namespace ModelHotSwapWorkflow.Models
{
    /// <summary>
    /// 深度学习推理节点：支持全图推理与基于 ROI 的多级级联推理
    /// </summary>
    public class ModelNode : NodeBase
    {
        private ModelManager? _modelManager;

        public string? ModelPath { get; set; }
        public string? ModelName { get; set; }
        public string ConfigPath { get; set; } = "model_config.json";
        public List<string> AvailableDataSources { get; set; } = new List<string>();

        public override string NodeType => "ModelNode";
        public override Type InputType => typeof(object);
        public override Type OutputType => typeof(HMManager.DetectionResultCollection);

        public override async Task<object> Process(object input)
        {
            InitializeEngine();

            Bitmap? sourceImage = null;
            List<HMManager.DetectionResult> roiItems = new List<HMManager.DetectionResult>();

            // 识别输入负载：可能是直接的位图，或者是上游模型（或分支）传来的检测结果
            if (input is Bitmap bmp)
            {
                sourceImage = bmp;
            }
            else if (input is HMManager.DetectionResultCollection results)
            {
                // 获取全局缓存的原始图像作为裁剪基准
                sourceImage = GlobalGallery.LastOriginalImage;
                roiItems = results.ToList();
            }

            // 严谨性检查：确保推理前具备有效的图像背景
            if (sourceImage == null)
            {
                throw new Exception($"[{Name}] 推理失败：全局图像缓存为空。请确保工作流以图像源节点开始。");
            }

            HMManager.DetectionResultCollection finalResults = new HMManager.DetectionResultCollection();

            await Task.Run(() =>
            {
                if (roiItems.Count > 0)
                {
                    // 级联模式：针对每一个 ROI 区域进行局部推理
                    foreach (var roiResult in roiItems)
                    {
                        foreach (var box in roiResult.Boxes)
                        {
                            using (Bitmap croppedImg = CropImage(sourceImage, box))
                            {
                                var subResults = _modelManager!.Run(croppedImg);
                                // 坐标系转换：将局部坐标还原至全局坐标系
                                foreach (var subRes in subResults)
                                {
                                    for (int i = 0; i < subRes.Boxes.Count; i++)
                                    {
                                        var b = subRes.Boxes[i];
                                        subRes.Boxes[i] = new Rectangle(b.X + box.X, b.Y + box.Y, b.Width, b.Height);
                                    }
                                    finalResults.Add(subRes);
                                }
                            }
                        }
                    }
                }
                else
                {
                    // 全图模式
                    var results = _modelManager!.Run(sourceImage);
                    foreach (var res in results) finalResults.Add(res);
                }
            });

            // 更新渲染缓存
            if (finalResults.DetectionCount > 0)
            {
                GlobalGallery.LastDrawnImage = finalResults.Visualize(sourceImage, true);
            }

            return finalResults;
        }

        private Bitmap CropImage(Bitmap source, Rectangle rect)
        {
            Rectangle safeRect = Rectangle.Intersect(new Rectangle(0, 0, source.Width, source.Height), rect);
            if (safeRect.Width <= 0 || safeRect.Height <= 0) return new Bitmap(1, 1);
            return source.Clone(safeRect, source.PixelFormat);
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