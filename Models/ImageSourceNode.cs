using System;
using System.Drawing;
using System.Threading.Tasks;
using OpenCvSharp; // 【新增】引入 OpenCV
using OpenCvSharp.Extensions; // 【新增】引入 OpenCV 与 Bitmap 的转换扩展

namespace ModelHotSwapWorkflow.Models
{
    /// <summary>
    /// 图像输入节点：负责从本地磁盘加载图像，转换为张量包裹发往下游
    /// </summary>
    public class ImageSourceNode : NodeBase
    {
        public string ImagePath { get; set; }

        public override string NodeType => "ImageSource";
        public override Type InputType => null;

        // 【核心修改 1】：把输出类型从 typeof(Bitmap) 改为 typeof(TensorPayload)
        public override Type OutputType => typeof(TensorPayload);

        /// <summary>
        /// 加载图像并封装为底层张量载体
        /// </summary>
        public override async Task<object> Process(object input)
        {
            // 1. 如果上游（比如TCP全局指挥官的 HTTP 传图）已经把张量送过来了，直接透传放行！
            if (input is TensorPayload payload && payload.BaseTensor != null && !payload.BaseTensor.Empty())
            {
                return payload;
            }

            // 2. 如果没有外部供图，则降级为本地硬盘读取模式
            return await Task.Run(() =>
            {
                if (string.IsNullOrEmpty(ImagePath) || !System.IO.File.Exists(ImagePath))
                    throw new Exception($"[图像源节点] 路径无效或文件不存在: {ImagePath}");

                // 高速读取硬盘图像
                Mat tensor = Cv2.ImRead(ImagePath, ImreadModes.Color);
                if (tensor.Empty())
                {
                    throw new Exception($"[解码失败] OpenCV 无法解析该图像文件: {ImagePath}");
                }

                // 【核心修复】：直接装进张量总线包裹发往下一站！彻底抛弃落后的 GlobalGallery！
                return new TensorPayload { BaseTensor = tensor, RoiResults = null };
            });
        }
    }
}