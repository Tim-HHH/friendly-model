using System;
using System.Collections.Generic;
using System.Windows;
using Microsoft.Win32;
using ModelHotSwapWorkflow.Models;

namespace ModelHotSwapWorkflow.Views
{
    /// <summary>
    /// 模型配置对话框逻辑交互类
    /// </summary>
    public partial class ModelConfigDialog : Window
    {
        private readonly ModelNode _node;

        /// <summary>
        /// 初始化模型配置界面
        /// </summary>
        /// <param name="node">当前选中的模型节点实例</param>
        public ModelConfigDialog(ModelNode node)
        {
            InitializeComponent();
            _node = node;

            // 初始化界面显示
            SourceComboBox.ItemsSource = _node.AvailableDataSources;
            ModelPathTextBox.Text = _node.ModelPath;

            // 假设我们在 ModelNode 中增加了以下属性（见后续 ModelNode 修改）
            // ConfidenceSlider.Value = _node.Threshold;
            // GpuCheckBox.IsChecked = _node.UseGpu;
        }

        /// <summary>
        /// 浏览并选择 ONNX 模型文件
        /// </summary>
        private void BrowseModel_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "ONNX 模型文件 (*.onnx)|*.onnx|所有文件 (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                ModelPathTextBox.Text = dialog.FileName;
            }
        }

        /// <summary>
        /// 确认保存配置并关闭窗口
        /// </summary>
        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            _node.ModelPath = ModelPathTextBox.Text;
            _node.ModelName = System.IO.Path.GetFileNameWithoutExtension(_node.ModelPath);
            _node.ConfigDisplay = $"模型: {_node.ModelName}";

            // 如果您增加了以下属性，请在此处赋值
            // _node.Threshold = ConfidenceSlider.Value;
            // _node.UseGpu = GpuCheckBox.IsChecked ?? false;

            this.DialogResult = true;
            this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}