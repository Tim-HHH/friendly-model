using System.Windows;
using ModelHotSwapWorkflow.Models;

namespace ModelHotSwapWorkflow.Views
{
    public partial class DisplayConfigDialog : Window
    {
        private DisplayNode _node;

        public DisplayConfigDialog(DisplayNode node)
        {
            InitializeComponent();
            _node = node;

            ChkDrawBox.IsChecked = _node.DrawBoundingBox;
            ChkDrawLabel.IsChecked = _node.DrawLabel;
            TxtSavePath.Text = _node.SaveImagePath;
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            // 使用 .NET 8 最新原生的 WPF 文件夹选择器
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择不良品保存目录"
            };

            if (dialog.ShowDialog() == true)
            {
                // 注意：新版获取路径的属性叫 FolderName，旧版叫 SelectedPath
                TxtSavePath.Text = dialog.FolderName;
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            _node.DrawBoundingBox = ChkDrawBox.IsChecked == true;
            _node.DrawLabel = ChkDrawLabel.IsChecked == true;
            _node.SaveImagePath = TxtSavePath.Text.Trim();

            _node.ConfigDisplay = $"[显示] 渲染框: {(_node.DrawBoundingBox ? "开" : "关")}";

            this.DialogResult = true;
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}