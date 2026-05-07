using System;
using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;

namespace ModelHotSwapWorkflow.Helpers
{
    public static class ImageHelper
    {
        public static BitmapSource ToBitmapSource(Image img)
        {
            if (img == null) return null;
            using (var ms = new MemoryStream())
            {
                img.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = ms;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                return bitmap;
            }
        }
    }
}