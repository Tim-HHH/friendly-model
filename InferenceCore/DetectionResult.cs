using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using OpenCvSharp.Extensions;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace HMManager
{
    public class DetectionResultCollection : IEnumerable<DetectionResult>, IReadOnlyCollection<DetectionResult>
    {
        private List<DetectionResult> _results = new List<DetectionResult>();
        public IEnumerator<DetectionResult> GetEnumerator() => _results.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public int Count => _results.Count;
        public int DetectionCount => _results.Sum(r => r.DetectionCount);

        public void Add(DetectionResult res) => _results.Add(res);

        public Mat Visualize(Mat image, bool bIsRenderingBoxesAndLabels = true)
        {
            Mat result = image.Clone();
            foreach (var item in _results) result = item.Visualize(result, bIsRenderingBoxesAndLabels);
            return result;
        }

        public Bitmap Visualize(Bitmap image, bool bIsRenderingBoxesAndLabels = true)
        {
            using (Mat mat = BitmapConverter.ToMat(image))
                return Visualize(mat, bIsRenderingBoxesAndLabels).ToBitmap();
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            foreach (var item in _results) sb.Append(item.ToString());
            return sb.ToString();
        }
    }

    public class DetectionResult
    {
        public int DetectionCount = 0;
        public List<Rectangle> Boxes { get; set; } = new List<Rectangle>();
        public List<float> Scores { get; set; } = new List<float>();
        public List<int> ClassIds { get; set; } = new List<int>();
        public List<string> ClassNames { get; set; } = new List<string>();
        public List<ClassDefinition> ClassDefinitions { get; set; } = new List<ClassDefinition>();
        public TaskType taskType { get; set; } = TaskType.Detection;

        public Mat Visualize(Mat image, bool drawBoxesAndLabels = true)
        {
            Mat result = image.Clone();
            if (taskType == TaskType.Classification)
            {
                if (ClassNames.Count > 0)
                    Cv2.PutText(result, $"{ClassNames[0]}: {Scores[0]:F2}", new OpenCvSharp.Point(10, 30), HersheyFonts.HersheySimplex, 1.0, new Scalar(0, 0, 255), 2);
            }
            else if (drawBoxesAndLabels)
            {
                for (int i = 0; i < Boxes.Count; i++)
                {
                    var def = ClassDefinitions.FirstOrDefault(d => d.Id == ClassIds[i]) ?? new ClassDefinition { Name = $"Class_{ClassIds[i]}" };
                    Scalar color = new Scalar(def.DisplayColor.B, def.DisplayColor.G, def.DisplayColor.R);
                    Rect rect = new Rect(Boxes[i].X, Boxes[i].Y, Boxes[i].Width, Boxes[i].Height);

                    Cv2.Rectangle(result, rect, color, 2);
                    Cv2.Rectangle(result, new OpenCvSharp.Point(rect.X, rect.Y - 25), new OpenCvSharp.Point(rect.Right, rect.Y), new Scalar(0, 255, 255), -1);
                    Cv2.PutText(result, $"{def.Name} {Scores[i]:F2}", new OpenCvSharp.Point(rect.X, rect.Y - 5), HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 0, 0), 2);
                }
            }
            return result;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Task: {taskType}, Found: {DetectionCount}");
            for (int i = 0; i < Boxes.Count; i++)
                sb.AppendLine($"[Class {ClassIds[i]}] Conf: {Scores[i]:F2}, Box: {Boxes[i]}");
            return sb.ToString();
        }
    }
}