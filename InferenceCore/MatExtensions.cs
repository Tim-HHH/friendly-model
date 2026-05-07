using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace HMManager
{
    public static class MatExtensions
    {
        public static Bitmap ToBitmap(this Mat mat)
        {
            if (mat == null || mat.Empty()) throw new ArgumentException("Mat 对象为空或无效");
            return OpenCvSharp.Extensions.BitmapConverter.ToBitmap(mat);
        }
    }
}