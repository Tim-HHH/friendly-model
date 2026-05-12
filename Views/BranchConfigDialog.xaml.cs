using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ModelHotSwapWorkflow.Models;

namespace ModelHotSwapWorkflow.Views
{
    /// <summary>
    /// 用于数据绑定的动态路由规则模型。
    /// 封装 UI 层面的业务条件及其绑定的可选下游节点集合。
    /// </summary>
    public class RouteConditionItem
    {
        public string ConditionText { get; set; }
        public string TargetNodeId { get; set; }
        public List<NodeBase> AvailableNodes { get; set; }
    }

    /// <summary>
    /// 智能路由配置视窗后台交互逻辑。
    /// 负责节点数据的双向绑定与持久化写入。
    /// </summary>
    public partial class BranchConfigDialog : Window
    {
        private BranchNode _node;
        private List<NodeBase> _allNodes;

        /// <summary>
        /// 基于 ObservableCollection 实现 UI 列表的动态增删响应。
        /// </summary>
        public ObservableCollection<RouteConditionItem> RouteConditions { get; set; } = new ObservableCollection<RouteConditionItem>();

        public BranchConfigDialog(BranchNode node, List<NodeBase> allNodes)
        {
            InitializeComponent();
            _node = node;
            _allNodes = allNodes;

            // 过滤出除了自身以外的所有有效下游候选节点
            var candidateNodes = _allNodes.Where(n => n.Id != _node.Id).ToList();

            // 1. 初始化默认兜底分支下拉列表
            CmbDefaultTarget.ItemsSource = candidateNodes;
            CmbDefaultTarget.SelectedValue = _node.DefaultTargetNodeId;

            // 2. 初始化阈值参数
            TxtThreshold.Text = _node.Threshold.ToString("0.00");

            // 3. 加载已有路由表到动态列表中
            foreach (var kvp in _node.ConditionTargetMap)
            {
                RouteConditions.Add(new RouteConditionItem
                {
                    ConditionText = kvp.Key,
                    TargetNodeId = kvp.Value,
                    AvailableNodes = candidateNodes
                });
            }

            // 绑定数据源到界面列表
            IcConditions.ItemsSource = RouteConditions;
        }

        /// <summary>
        /// 触发添加新路由规则的交互指令。
        /// </summary>
        private void BtnAddCondition_Click(object sender, RoutedEventArgs e)
        {
            var candidateNodes = _allNodes.Where(n => n.Id != _node.Id).ToList();
            RouteConditions.Add(new RouteConditionItem
            {
                ConditionText = "输入条件",
                TargetNodeId = null,
                AvailableNodes = candidateNodes
            });
        }

        /// <summary>
        /// 触发移除特定路由规则的交互指令。
        /// </summary>
        private void BtnRemoveCondition_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is RouteConditionItem item)
            {
                RouteConditions.Remove(item);
            }
        }

        /// <summary>
        /// 执行配置参数的校验与业务实体的持久化更新。
        /// </summary>
        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(TxtThreshold.Text, out double threshold))
            {
                _node.Threshold = threshold;
            }

            _node.DefaultTargetNodeId = CmbDefaultTarget.SelectedValue as string;

            // 清理并重新构建业务核心的路由映射字典
            _node.ConditionTargetMap.Clear();
            foreach (var item in RouteConditions)
            {
                if (!string.IsNullOrWhiteSpace(item.ConditionText) && !string.IsNullOrEmpty(item.TargetNodeId))
                {
                    // 覆写可能存在的重复条件设定
                    _node.ConditionTargetMap[item.ConditionText.Trim()] = item.TargetNodeId;
                }
            }

            // 更新画布上的简略显示状态
            _node.ConfigDisplay = $"默认流向: {CmbDefaultTarget.Text ?? "未设置"} | 已配规则数: {_node.ConditionTargetMap.Count}";

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