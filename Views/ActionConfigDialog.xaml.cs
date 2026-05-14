using System.Windows;
using ModelHotSwapWorkflow.Models;

namespace ModelHotSwapWorkflow.Views
{
    public partial class ActionConfigDialog : Window
    {
        private ActionNode _node;

        public ActionConfigDialog(ActionNode node)
        {
            InitializeComponent();
            _node = node;

            CmbActionType.SelectedIndex = _node.ActionType == ActionTargetType.PrintLog ? 0 : 1;
            TxtMessage.Text = _node.CustomMessage;
            TxtCsvPath.Text = _node.ExportCsvPath;
        }

        private void CmbActionType_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (PnlCsvPath == null) return;
            PnlCsvPath.Visibility = CmbActionType.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnBrowseCsv_Click(object sender, RoutedEventArgs e)
        {
            // 使用 .NET 8 最新原生的 WPF 文件夹选择器
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择 CSV 导出目录"
            };

            if (dialog.ShowDialog() == true)
            {
                // 注意：新版获取路径的属性叫 FolderName
                TxtCsvPath.Text = dialog.FolderName;
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            _node.ActionType = CmbActionType.SelectedIndex == 0 ? ActionTargetType.PrintLog : ActionTargetType.ExportCsv;
            _node.CustomMessage = TxtMessage.Text.Trim();
            _node.ExportCsvPath = TxtCsvPath.Text.Trim();

            string typeStr = _node.ActionType == ActionTargetType.PrintLog ? "日志" : "存CSV";
            _node.ConfigDisplay = $"[动作] {typeStr} : {_node.CustomMessage}";

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