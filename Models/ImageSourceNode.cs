using System;
using System.Drawing;
using System.Threading.Tasks;

namespace ModelHotSwapWorkflow.Models
{
    /// <summary>
    /// 图像输入节点：负责从本地磁盘加载图像，并同步至全局图像缓存
    /// </summary>
    public class ImageSourceNode : NodeBase
    {
        public string ImagePath { get; set; }

        public override string NodeType => "ImageSource";
        public override Type InputType => null;
        public override Type OutputType => typeof(Bitmap);

        /// <summary>
        /// 加载图像并更新全局原始图像引用
        /// </summary>
        public override async Task<object> Process(object input)
        {
            if (string.IsNullOrEmpty(ImagePath) || !System.IO.File.Exists(ImagePath))
            {
                throw new Exception($"无法加载图像文件，请检查路径: {ImagePath}");
            }

            // 加载位图
            Bitmap bmp = new Bitmap(ImagePath);

            // 【关键步骤】：在返回之前，必须将原始大图存入全局画廊，供级联模型裁剪使用
            GlobalGallery.LastOriginalImage = bmp;

            return bmp;
        }
    }
}