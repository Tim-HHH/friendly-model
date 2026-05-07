using System;
using OpenCvSharp;

namespace HMManager
{
    public class ImageProcessor
    {
        public int ModelHeight { get; set; }
        public int ModelWidth { get; set; }
        public MatType DataType { get; set; }

        public ImageProcessor(int modelHeight, int modelWidth, MatType dataType)
        {
            ModelHeight = modelHeight;
            ModelWidth = modelWidth;
            DataType = dataType;
        }

        public (float[] data, double[] ratio, (double padW, double padH)) ProcessImage(Mat inputImg, int channelNumber)
        {
            Mat img = inputImg.Clone();
            var originalShape = new int[] { img.Height, img.Width };
            var targetShape = new int[] { ModelHeight, ModelWidth };

            double scaleRatio = Math.Min(targetShape[0] / (double)originalShape[0], targetShape[1] / (double)originalShape[1]);
            double[] ratio = new double[] { scaleRatio, scaleRatio };

            var newUnpaddedSize = new int[] {
                (int)Math.Round(originalShape[1] * scaleRatio),
                (int)Math.Round(originalShape[0] * scaleRatio)
            };

            double padW = (targetShape[1] - newUnpaddedSize[0]) / 2.0;
            double padH = (targetShape[0] - newUnpaddedSize[1]) / 2.0;

            if (originalShape[1] != newUnpaddedSize[0] || originalShape[0] != newUnpaddedSize[1])
                Cv2.Resize(img, img, new OpenCvSharp.Size(newUnpaddedSize[0], newUnpaddedSize[1]), 0, 0, InterpolationFlags.Linear);

            Cv2.CopyMakeBorder(img, img, (int)Math.Round(padH - 0.1), (int)Math.Round(padH + 0.1),
                               (int)Math.Round(padW - 0.1), (int)Math.Round(padW + 0.1),
                               BorderTypes.Constant, new Scalar(114, 114, 114));

            Mat processedImg = new Mat();
            if (channelNumber == 1) Cv2.CvtColor(img, processedImg, ColorConversionCodes.BGR2GRAY);
            else if (channelNumber == 3) Cv2.CvtColor(img, processedImg, ColorConversionCodes.BGR2RGB);

            int totalElements = processedImg.Width * processedImg.Height * processedImg.Channels();
            float[] floatArray = new float[totalElements];
            int currentIndex = 0;

            Mat[] channels = processedImg.Split();
            foreach (var channel in channels)
            {
                channel.ConvertTo(channel, DataType, 1.0 / 255.0);
                float[] channelData = new float[channel.Width * channel.Height];
                channel.GetArray(out channelData);
                Array.Copy(channelData, 0, floatArray, currentIndex, channelData.Length);
                currentIndex += channelData.Length;
            }

            return (floatArray, ratio, (padW, padH));
        }
    }
}