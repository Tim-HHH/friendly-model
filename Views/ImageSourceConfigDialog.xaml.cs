using System;
using System.Windows;
using Microsoft.Win32;
using ModelHotSwapWorkflow.Models;

namespace ModelHotSwapWorkflow.Views
{
    public partial class ImageSourceConfigDialog : Window
    {
        private readonly ImageSourceNode _node;

        public ImageSourceConfigDialog(ImageSourceNode node)
        {
            InitializeComponent();
            _node = node;

            // 回显现有配置
            PathTextBox.Text = _node.ImagePath;
            // 假设您在 ImageSourceNode 中扩展了以下属性
            // ModeComboBox.SelectedIndex = _node.IsFolderMode ? 1 : 0;
            // IntervalTextBox.Text = _node.Interval.ToString();
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            if (ModeComboBox.SelectedIndex == 0) // 文件模式
            {
                OpenFileDialog dialog = new OpenFileDialog { Filter = "图像文件|*.jpg;*.png;*.bmp" };
                if (dialog.ShowDialog() == true) PathTextBox.Text = dialog.FileName;
            }
            else // 文件夹模式
            {
                // 使用 .NET 8.0+ 专为 WPF 提供的现代文件夹选择对话框
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "请选择图像序列所在的文件夹",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
                };

                if (dialog.ShowDialog() == true)
                {
                    PathTextBox.Text = dialog.FolderName;
                }
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            _node.ImagePath = PathTextBox.Text;
            _node.ConfigDisplay = System.IO.Path.GetFileName(_node.ImagePath);

            // 更新节点逻辑参数
            // _node.IsFolderMode = ModeComboBox.SelectedIndex == 1;
            // int.TryParse(IntervalTextBox.Text, out int interval);
            // _node.Interval = interval;

            this.DialogResult = true;
            this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => this.Close();
    }
}