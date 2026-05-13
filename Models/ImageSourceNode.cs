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
            // 1. 如果是从上位机（HTTP）传过来的张量包裹，直接验收放行！
            if (input is TensorPayload injectedPayload && injectedPayload.BaseTensor != null)
            {
                return injectedPayload;
            }

            // 2. 否则（TCP触发 或是 手动模式），老老实实去读节点里配置的本地硬盘图片
            if (string.IsNullOrEmpty(ImagePath) || !System.IO.File.Exists(ImagePath))
            {
                throw new Exception($"图像源 [{Name}] 无法加载本地图像，请检查路径: {ImagePath}");
            }

            // 读取硬盘图像
            Mat tensor = Cv2.ImRead(ImagePath, ImreadModes.Color);
            if (tensor.Empty())
            {
                throw new Exception($"[解码失败] OpenCV 无法解析该图像文件: {ImagePath}");
            }

            // 存入全局画廊并打包发送
            GlobalGallery.LastOriginalImage = BitmapConverter.ToBitmap(tensor);
            return new TensorPayload { BaseTensor = tensor, RoiResults = null };
        }
    }
}