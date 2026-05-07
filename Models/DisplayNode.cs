using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;

namespace ModelHotSwapWorkflow.Models
{
    public class DisplayNode : NodeBase
    {
        public event Action<Image> OnImageUpdated;
        public override string NodeType => "DisplayNode";
        public override Type InputType => typeof(DetectionResult);
        public override Type OutputType => null;

        public override async Task<object> Process(object input)
        {
            var result = input as DetectionResult;
            if (result == null) return null;

            var drawnImage = DrawDetections(result.Image, result.Detections);
            OnImageUpdated?.Invoke(drawnImage);
            return null;
        }

        private Image DrawDetections(Image img, List<Detection> detections)
        {
            if (img == null || detections == null || detections.Count == 0) return img;
            Bitmap bmp = new Bitmap(img);
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
            {
                var best = detections.OrderByDescending(d => d.Confidence).FirstOrDefault();
                if (best?.Bbox?.Length >= 4)
                {
                    int w = bmp.Width, h = bmp.Height;
                    float x1 = best.Bbox[0] * w, y1 = best.Bbox[1] * h;
                    float x2 = best.Bbox[2] * w, y2 = best.Bbox[3] * h;
                    using (System.Drawing.Pen pen = new System.Drawing.Pen(System.Drawing.Color.Red, 3))
                        g.DrawRectangle(pen, x1, y1, x2 - x1, y2 - y1);
                    using (System.Drawing.Font font = new System.Drawing.Font("微软雅黑", 10))
                        g.DrawString($"Conf: {best.Confidence:P1}", font, System.Drawing.Brushes.White, x1, y1 - 15);
                }
            }
            return bmp;
        }
    }
}