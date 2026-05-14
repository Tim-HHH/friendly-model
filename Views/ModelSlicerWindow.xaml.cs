using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ModelHotSwapWorkflow.Services; // 确保引用了您的后台工具类

namespace ModelHotSwapWorkflow.Views
{
    public partial class ModelSlicerWindow : Window
    {
        public ModelSlicerWindow()
        {
            InitializeComponent();
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog
            {
                Filter = "ONNX 模型文件 (*.onnx)|*.onnx",
                Title = "选择需要切片的 AI 模型"
            };

            if (dlg.ShowDialog() == true)
            {
                TxtInputModel.Text = dlg.FileName;
                LogMessage($"已加载待切片模型: {Path.GetFileName(dlg.FileName)}");
            }
        }

        private void CmbPresetType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PnlCustomNode == null) return;

            // 如果选择了第3项（自定义）
            if (CmbPresetType.SelectedIndex == 2)
            {
                PnlCustomNode.Visibility = Visibility.Visible;
            }
            else
            {
                PnlCustomNode.Visibility = Visibility.Collapsed;
                TxtCustomNode.Text = "";
            }
        }

        /// <summary>
        /// 触发物理切片的核心逻辑：
        /// 负责收集界面参数、锁定UI、呼叫后台Python进程并处理最终结果。
        /// </summary>
        private async void BtnStartSlice_Click(object sender, RoutedEventArgs e)
        {
            // 1. 基础安全校验：检查是否选择了模型文件
            string modelPath = TxtInputModel.Text;
            if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
            {
                MessageBox.Show("请先选择有效的 ONNX 模型文件！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. 架构参数映射：将界面下拉框的索引转换为脚本识别的字符串标识
            // 严格对应 XAML 中的顺序：0:v8, 1:v5, 2:v11, 3:v12, 4:Custom
            string modelType = "custom";
            switch (CmbPresetType.SelectedIndex)
            {
                case 0: modelType = "yolov8"; break;
                case 1: modelType = "yolov5"; break;
                case 2: modelType = "yolov11"; break;
                case 3: modelType = "yolov12"; break;
                default: modelType = "custom"; break;
            }

            // 3. 自定义模式校验：如果选了自定义，必须填写节点名
            string customNode = TxtCustomNode.Text.Trim();
            if (modelType == "custom" && string.IsNullOrEmpty(customNode))
            {
                MessageBox.Show("您选择了【自定义节点模式】，请务必输入切分点的节点名称！", "参数缺失", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 4. UI 状态锁定：防止用户在计算过程中重复点击导致进程冲突
            BtnStartSlice.IsEnabled = false;
            BtnStartSlice.Content = "⏳ 正在执行物理切片，请稍候...";

            // 记录启动日志，时间戳细化到毫秒(.fff)
            LogMessage($"\n[{DateTime.Now:HH:mm:ss.fff}] 🚀 准备启动外部切割引擎 (Mode: {modelType})...");

            try
            {
                // 5. 跨语言调用：执行异步切片任务
                // 我们将模型路径、架构类型、节点名发送给 Python 脚本，并实时将脚本的打印信息反馈到界面日志框
                bool success = await ModelSlicerTool.RunSlicerAsync(
                    modelPath,
                    modelType,
                    customNode,
                    msg => Dispatcher.Invoke(() => LogMessage(msg)) // 线程安全地回传脚本日志
                );

                // 6. 任务后处理
                if (success)
                {
                    LogMessage($"[{DateTime.Now:HH:mm:ss.fff}] ✅ 状态：任务圆满结束。");
                    MessageBox.Show("模型切片成功！\n\n新模型文件：\n1. backbone.onnx (主干提取)\n2. head.onnx (推理头)\n已保存至原模型同级目录。",
                                    "大功告成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    LogMessage($"[{DateTime.Now:HH:mm:ss.fff}] ❌ 状态：任务执行失败。");
                    MessageBox.Show("切片过程发生错误！\n请检查：\n1. Python 环境是否包含 onnx 库\n2. 预设的节点名是否与您的模型匹配",
                                    "执行错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"[{DateTime.Now:HH:mm:ss.fff}] ⚠️ 发生系统级异常: {ex.Message}");
            }
            finally
            {
                // 7. 恢复 UI 状态：无论成功失败，都要把按钮还给用户
                BtnStartSlice.IsEnabled = true;
                BtnStartSlice.Content = "⚡ 一键执行物理切片";
            }
        }

        private void LogMessage(string message)
        {
            TxtLog.AppendText(message + "\n");
            TxtLog.ScrollToEnd();
        }
    }
}