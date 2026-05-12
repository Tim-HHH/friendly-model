using System;
using System.Drawing;
using System.Threading.Tasks;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace ModelHotSwapWorkflow.Models
{
    /// <summary>
    /// 全局图像数据中转站：用于跨节点共享原始图像与渲染结果。
    /// </summary>
    public static class GlobalGallery
    {
        /// <summary>
        /// 最近一次推理并绘制了检测框的图像（保留以向下兼容旧架构）。
        /// </summary>
        public static Bitmap? LastDrawnImage { get; set; }

        /// <summary>
        /// 最近一次从源获取的原始完整图像（用于全局兜底）。
        /// </summary>
        public static Bitmap? LastOriginalImage { get; set; }
    }

    /// <summary>
    /// 结果展示节点：负责解析上游传递的张量载荷，执行包围盒渲染并更新 UI。
    /// </summary>
    public class DisplayNode : NodeBase
    {
        public override string NodeType => "DisplayNode";

        public override Type InputType => typeof(TensorPayload);
        public override Type OutputType => null;

        /// <summary>
        /// 图像更新事件，UI 订阅此事件以刷新 Canvas 或 Image 控件。
        /// </summary>
        public event Action<Bitmap>? OnImageUpdated;

        /// <summary>
        /// 异步解析输入载荷，执行特征图可视化并触发渲染事件。
        /// </summary>
        public override async Task<object> Process(object input)
        {
            Bitmap? imageToDisplay = null;

            // 1. 向下兼容：优先检查旧版架构遗留的全局渲染缓存
            if (GlobalGallery.LastDrawnImage != null)
            {
                imageToDisplay = GlobalGallery.LastDrawnImage;
                GlobalGallery.LastDrawnImage = null;
            }
            // 2. 核心渲染逻辑：解析张量包裹并执行可视化合成
            else if (input is TensorPayload payload && payload.BaseTensor != null && !payload.BaseTensor.IsDisposed)
            {
                // 校验载荷中是否存在有效的感兴趣区域 (ROI) 检测结果
                if (payload.RoiResults != null && payload.RoiResults.DetectionCount > 0)
                {
                    // 调用底层 OpenCV 扩展方法，将检测框与分类标签实时绘制于张量矩阵上
                    using (Mat drawnMat = payload.RoiResults.Visualize(payload.BaseTensor, true))
                    {
                        // 转换绘制后的矩阵为 GDI+ Bitmap 供 WPF 前端消费
                        imageToDisplay = BitmapConverter.ToBitmap(drawnMat);
                    }
                }
                else
                {
                    // 若无检测结果，直接转换原始底层张量
                    imageToDisplay = BitmapConverter.ToBitmap(payload.BaseTensor);
                }
            }
            // 3. 兜底逻辑：处理直接传入位图的异常边际情况
            else if (input is Bitmap bmp)
            {
                imageToDisplay = bmp;
            }

            // 触发事件，驱动主窗体调度器更新界面像素
            if (imageToDisplay != null)
            {
                OnImageUpdated?.Invoke(imageToDisplay);
            }

            return null;
        }
    }
}