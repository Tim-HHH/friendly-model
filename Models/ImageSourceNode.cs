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
            // 【核心魔术】：如果是上位机通过 HTTP 空投进来的张量包裹，直接免检放行！
            if (input is TensorPayload injectedPayload)
            {
                return injectedPayload; // 直接发往下游二阶段模型！
            }

            // ==========================================
            // 下面保留原有的本地测试逻辑 (通过 TCP 触发时，读取本地硬盘的测试图)
            if (string.IsNullOrEmpty(ImagePath) || !System.IO.File.Exists(ImagePath))
                throw new Exception($"无法加载图像文件，请检查路径: {ImagePath}");

            OpenCvSharp.Mat tensor = OpenCvSharp.Cv2.ImRead(ImagePath, OpenCvSharp.ImreadModes.Color);
            if (tensor.Empty()) throw new Exception($"OpenCV 无法解析该图像文件: {ImagePath}");

            GlobalGallery.LastOriginalImage = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(tensor);

            return new TensorPayload { BaseTensor = tensor, RoiResults = null };
        }
    }
}