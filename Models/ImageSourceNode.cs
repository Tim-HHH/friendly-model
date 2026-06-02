using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows; // 【新增】用于调用界面弹窗 MessageBox
using OpenCvSharp;
using OpenCvSharp.Extensions;

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
        public override Type OutputType => typeof(TensorPayload);

        /// <summary>
        /// 加载图像并封装为底层张量载体
        /// </summary>
        public override async Task<object> Process(object input)
        {
            // 1. 如果上游已经把张量送过来了，直接透传放行！
            if (input is TensorPayload payload && payload.BaseTensor != null && !payload.BaseTensor.Empty())
            {
                return payload;
            }

            // 2. 如果没有外部供图，则降级为本地硬盘读取模式
            return await Task.Run<object>(() =>
            {
                // 【核心修复】：不要直接 throw Exception 导致闪退
                // 改为在主界面线程弹出友好提示，并返回 null 截断工作流
                if (string.IsNullOrEmpty(ImagePath) || !System.IO.File.Exists(ImagePath))
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show("未检测到图像！\n请先双击【图像输入节点】选择一张图片，然后再点击运行测试。",
                                        "缺少图像", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                    return null;
                }

                // 高速读取硬盘图像
                Mat tensor = Cv2.ImRead(ImagePath, ImreadModes.Color);
                if (tensor.Empty())
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"OpenCV 无法解析该图像文件: {ImagePath}",
                                        "读取失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                    return null;
                }

                // 直接装进张量总线包裹发往下一站
                return new TensorPayload { BaseTensor = tensor, RoiResults = null };
            });
        }
    }
}