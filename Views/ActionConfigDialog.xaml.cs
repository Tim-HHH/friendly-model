using System.Windows;
using ModelHotSwapWorkflow.Models;

namespace ModelHotSwapWorkflow.Views
{
    public partial class ActionConfigDialog : Window
    {
        private ActionNode actionNode;
        public ActionConfigDialog(ActionNode node)
        {
            InitializeComponent();
            actionNode = node;
            ActionNameBox.Text = node.ActionName ?? "写日志";
            ParamBox.Text = node.ActionParameter ?? "";
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            actionNode.ActionName = ActionNameBox.Text;
            actionNode.ActionParameter = ParamBox.Text;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}