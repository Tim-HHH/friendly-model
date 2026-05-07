using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;

namespace ModelHotSwapWorkflow.Models
{
    public class ImageSourceNode : NodeBase
    {
        public string ImagePath { get; set; }
        public override string NodeType => "ImageSource";
        public override Type InputType => null;
        public override Type OutputType => typeof(Image);

        public override async Task<object> Process(object input)
        {
            if (string.IsNullOrEmpty(ImagePath) || !File.Exists(ImagePath))
                throw new InvalidOperationException("图像源未配置有效图像路径");
            return Image.FromFile(ImagePath);
        }
    }
}