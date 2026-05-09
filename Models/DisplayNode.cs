using System;
using System.Drawing;
using System.Threading.Tasks;

namespace ModelHotSwapWorkflow.Models
{
    /// <summary>
    /// 全局图像数据中转站：用于跨节点共享原始图像与渲染结果
    /// </summary>
    public static class GlobalGallery
    {
        /// <summary>
        /// 最近一次推理并绘制了检测框的图像（用于 UI 展示）
        /// </summary>
        public static Bitmap? LastDrawnImage { get; set; }

        /// <summary>
        /// 最近一次从源获取的原始完整图像（用于级联推理的 ROI 裁剪基准）
        /// </summary>
        public static Bitmap? LastOriginalImage { get; set; }
    }

    /// <summary>
    /// 结果展示节点：负责将最终图像渲染到主界面
    /// </summary>
    public class DisplayNode : NodeBase
    {
        public override string NodeType => "DisplayNode";
        public override Type InputType => typeof(object);
        public override Type OutputType => null;

        public event Action<Bitmap>? OnImageUpdated;

        public override async Task<object> Process(object input)
        {
            // 优先显示经过模型渲染的带框图像
            if (GlobalGallery.LastDrawnImage != null)
            {
                OnImageUpdated?.Invoke(GlobalGallery.LastDrawnImage);
                // 展示后清空渲染缓存，但保留原始图像供其他流水线使用
                GlobalGallery.LastDrawnImage = null;
            }
            else if (input is Bitmap bmp)
            {
                OnImageUpdated?.Invoke(bmp);
            }

            return null;
        }
    }
}