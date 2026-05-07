using System;
using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace HMManager
{
    public static class VisualizerFactory
    {
        public static IDetectionVisualizer_Bmp CreateVisualizer_Bmp() => new BitmapVisualizer();
        public static IDetectionVisualizer_Mat CreateVisualizer_Mat() => new OpenCvVisualizer();
    }

    public interface IDetectionVisualizer_Bmp
    {
        Bitmap Visualize(Bitmap image, DetectionResultCollection results, bool drawBoxesAndLabels);
    }

    public interface IDetectionVisualizer_Mat
    {
        Mat Visualize(Mat image, DetectionResultCollection results, bool drawBoxesAndLabels);
    }

    internal class BitmapVisualizer : IDetectionVisualizer_Bmp
    {
        public Bitmap Visualize(Bitmap image, DetectionResultCollection results, bool drawBoxesAndLabels)
        {
            using (Mat mat = BitmapConverter.ToMat(image))
                return new OpenCvVisualizer().Visualize(mat, results, drawBoxesAndLabels).ToBitmap();
        }
    }

    internal class OpenCvVisualizer : IDetectionVisualizer_Mat
    {
        public Mat Visualize(Mat image, DetectionResultCollection results, bool drawBoxesAndLabels)
        {
            return results.Visualize(image, drawBoxesAndLabels);
        }
    }
}