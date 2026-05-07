using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using System.Windows;
using Size = OpenCvSharp.Size;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;

namespace HMManager
{
    public interface IylResultParser
    {
        DetectionResult Parse(IReadOnlyCollection<NamedOnnxValue> outputs, Size originalImageSize, double ratio, double pad_w, double pad_h);
    }

    public class yl8DetectionParser : IylResultParser
    {
        private List<ClassDefinition> _classes;
        private OutputConfig _outCfg;

        public yl8DetectionParser(List<ClassDefinition> classes, OutputConfig outCfg)
        {
            _classes = classes;
            _outCfg = outCfg ?? new OutputConfig();
        }

        public DetectionResult Parse(IReadOnlyCollection<NamedOnnxValue> outputs, Size originalImageSize, double ratio, double pad_w, double pad_h)
        {
            var tensor = outputs.First(o => o.Name == "output0").AsTensor<float>();
            int numClasses = tensor.Dimensions[1] - 4;
            Mat data = Mat.FromPixelData(tensor.Dimensions[1], tensor.Dimensions[2], MatType.CV_32FC1, tensor.ToArray()).T();

            List<Rect> boxes = new List<Rect>();
            List<float> scores = new List<float>();
            List<int> classIds = new List<int>();

            for (int i = 0; i < data.Rows; i++)
            {
                Mat classScores = new Mat(data, new Rect(4, i, numClasses, 1));
                Cv2.MinMaxLoc(classScores, out _, out double maxScore, out _, out Point maxLoc);

                if (maxScore > _outCfg.ConfidenceThreshold)
                {
                    float cx = data.At<float>(i, 0);
                    float cy = data.At<float>(i, 1);
                    float w = data.At<float>(i, 2);
                    float h = data.At<float>(i, 3);

                    double x = (cx - w / 2 - pad_w) / ratio;
                    double y = (cy - h / 2 - pad_h) / ratio;
                    x = Math.Max(0, Math.Min(x, originalImageSize.Width));
                    y = Math.Max(0, Math.Min(y, originalImageSize.Height));

                    int bw = (int)Math.Min(originalImageSize.Width - x, w / ratio);
                    int bh = (int)Math.Min(originalImageSize.Height - y, h / ratio);

                    boxes.Add(new Rect((int)x, (int)y, bw, bh));
                    scores.Add((float)maxScore);
                    classIds.Add(maxLoc.X);
                }
            }

            CvDnn.NMSBoxes(boxes, scores, _outCfg.ConfidenceThreshold, _outCfg.IouThreshold, out int[] indices);

            DetectionResult res = new DetectionResult { ClassDefinitions = _classes, taskType = TaskType.Detection };
            foreach (int idx in indices)
            {
                res.Boxes.Add(new Rectangle(boxes[idx].X, boxes[idx].Y, boxes[idx].Width, boxes[idx].Height));
                res.Scores.Add(scores[idx]);
                res.ClassIds.Add(classIds[idx]);
                res.DetectionCount++;
            }
            return res;
        }
    }

    public class yl86ClassificationParser : IylResultParser
    {
        private List<ClassDefinition> _classes;
        public yl86ClassificationParser(List<ClassDefinition> classes, OutputConfig outCfg) { _classes = classes; }

        public DetectionResult Parse(IReadOnlyCollection<NamedOnnxValue> outputs, Size size, double ratio, double pw, double ph)
        {
            var tensor = outputs.First(o => o.Name == "output0").AsTensor<float>();
            float maxScore = 0;
            int maxId = 0;

            for (int i = 0; i < tensor.Dimensions[1]; i++)
            {
                if (tensor[0, i] > maxScore) { maxScore = tensor[0, i]; maxId = i; }
            }

            var def = _classes.FirstOrDefault(c => c.Id == maxId) ?? new ClassDefinition { Name = "Class_" + maxId };
            return new DetectionResult
            {
                taskType = TaskType.Classification,
                ClassIds = new List<int> { maxId },
                Scores = new List<float> { maxScore },
                ClassNames = new List<string> { def.Name },
                DetectionCount = 1
            };
        }
    }
}