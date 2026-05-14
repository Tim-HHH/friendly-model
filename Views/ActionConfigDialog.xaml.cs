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

            // 1. 初始化赋值：将节点中保存的数据填入界面
            CmbActionType.SelectedIndex = (int)_node.ActionType;
            TxtMessage.Text = _node.CustomMessage;
            TxtCsvPath.Text = _node.ExportCsvPath;
            TxtPushUrl.Text = _node.PushUrl;
        }

        private void CmbActionType_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // 确保控件加载完毕，防止空引用报错
            if (PnlCsvPath == null || PnlHttpUrl == null) return;

            // 选择 1 (CSV) 时，显示 CSV 路径输入框
            PnlCsvPath.Visibility = CmbActionType.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;

            // 选择 2 (HTTP网络推送) 时，显示 URL 输入框
            PnlHttpUrl.Visibility = CmbActionType.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
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
                TxtCsvPath.Text = dialog.FolderName; // 注意：新版获取路径的属性叫 FolderName
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            // 1. 将界面上填写的参数保存回底层节点数据中
            _node.ActionType = (ActionTargetType)CmbActionType.SelectedIndex;
            _node.CustomMessage = TxtMessage.Text.Trim();
            _node.ExportCsvPath = TxtCsvPath.Text.Trim();
            _node.PushUrl = TxtPushUrl.Text.Trim();

            // 2. 根据选中的类型，动态更新画布方块上显示的文本
            string[] types = { "日志", "存CSV", "网络推送" };
            _node.ConfigDisplay = $"[动作] {types[(int)_node.ActionType]} : {_node.CustomMessage}";

            // 3. 关闭窗口并返回成功状态
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