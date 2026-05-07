using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using ModelHotSwapWorkflow.Models;

namespace ModelHotSwapWorkflow.Views
{
    public partial class BranchConfigDialog : Window
    {
        private BranchNode branchNode;
        private Dictionary<string, NodeBase> allNodes;
        private ObservableCollection<ConditionMapping> mappings = new ObservableCollection<ConditionMapping>();

        public BranchConfigDialog(BranchNode node, Dictionary<string, NodeBase> nodes)
        {
            InitializeComponent();
            branchNode = node;
            allNodes = nodes;

            // 获取所有其他节点的名称作为下拉选项
            var nodeNames = nodes.Values
                .Where(n => n.Id != node.Id)
                .Select(n => n.Name)
                .ToList();
            TargetComboColumn.ItemsSource = nodeNames;

            // 加载已有的条件映射
            foreach (var kv in branchNode.ConditionTargetMap)
            {
                if (nodes.TryGetValue(kv.Value, out var targetNode))
                {
                    mappings.Add(new ConditionMapping { Condition = kv.Key, TargetNodeName = targetNode.Name });
                }
            }
            MappingGrid.ItemsSource = mappings;
        }

        // 添加新映射行
        private void AddMapping_Click(object sender, RoutedEventArgs e)
        {
            mappings.Add(new ConditionMapping());
            MappingGrid.SelectedItem = mappings.Last();
            MappingGrid.ScrollIntoView(mappings.Last());
        }

        // 删除选中行
        private void DeleteMapping_Click(object sender, RoutedEventArgs e)
        {
            var selected = MappingGrid.SelectedItem as ConditionMapping;
            if (selected != null)
            {
                mappings.Remove(selected);
            }
            else
            {
                MessageBox.Show("请先选中要删除的行", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            branchNode.ConditionTargetMap.Clear();
            foreach (var mapping in mappings)
            {
                if (!string.IsNullOrEmpty(mapping.Condition) && !string.IsNullOrEmpty(mapping.TargetNodeName))
                {
                    var targetNode = allNodes.Values.FirstOrDefault(n => n.Name == mapping.TargetNodeName);
                    if (targetNode != null)
                        branchNode.ConditionTargetMap[mapping.Condition] = targetNode.Id;
                }
            }
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class ConditionMapping
    {
        public string Condition { get; set; }
        public string TargetNodeName { get; set; }
    }
}