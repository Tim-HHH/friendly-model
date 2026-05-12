using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ModelHotSwapWorkflow.Models;

namespace ModelHotSwapWorkflow.Views
{
    /// <summary>
    /// 指令路由映射项：用于在 UI 层面绑定外部指令字符串与目标图像源节点。
    /// </summary>
    public class CommandMappingItem
    {
        /// <summary>外部触发口令（如 "T0"）。</summary>
        public string CommandText { get; set; }

        /// <summary>目标图像源节点的唯一标识符。</summary>
        public string TargetSourceId { get; set; }

        /// <summary>当前工作流中所有可选的图像源节点集合。</summary>
        public List<NodeBase> AvailableSources { get; set; }
    }

    /// <summary>
    /// 全局通信配置对话框后台逻辑。
    /// 负责 TCP 与 HTTP 通信参数的配置，以及外部指令与逻辑路径起点的映射管理。
    /// </summary>
    public partial class TcpConfigDialog : Window
    {
        private TcpCommandNode _node;
        private List<NodeBase> _sourceNodes;

        /// <summary>
        /// 指令映射表的动态观察集合，支持 UI 列表的实时增删同步。
        /// </summary>
        public ObservableCollection<CommandMappingItem> Mappings { get; set; } = new ObservableCollection<CommandMappingItem>();

        // 【关键修复点】：这就是报错的原因。我们将构造函数修改为接收节点对象和节点列表。
        public TcpConfigDialog(TcpCommandNode node, List<NodeBase> allNodes)
        {
            InitializeComponent();
            _node = node;

            // 业务校验：提取当前画布上所有类型为“图像源”的节点
            _sourceNodes = allNodes.Where(n => n.NodeType == "ImageSource").ToList();

            // 1. 初始化基础网络配置参数
            RbServer.IsChecked = _node.IsServer;
            RbClient.IsChecked = !_node.IsServer;
            TxtAddress.Text = _node.Address;
            TxtPort.Text = _node.Port.ToString();
            TxtHttpPort.Text = _node.HttpPort.ToString();
            TxtManualCommand.Text = _node.ManualCommand;

            // 2. 将节点内存中保存的映射字典转换为 UI 可绑定的列表模型
            if (_node.CommandMapping != null)
            {
                foreach (var kvp in _node.CommandMapping)
                {
                    Mappings.Add(new CommandMappingItem
                    {
                        CommandText = kvp.Key,
                        TargetSourceId = kvp.Value,
                        AvailableSources = _sourceNodes
                    });
                }
            }

            // 建立数据绑定关联
            IcMappings.ItemsSource = Mappings;
        }

        /// <summary>
        /// 向路由表中追加新的映射规则。
        /// </summary>
        private void BtnAddMapping_Click(object sender, RoutedEventArgs e)
        {
            Mappings.Add(new CommandMappingItem
            {
                CommandText = "T0",
                TargetSourceId = null,
                AvailableSources = _sourceNodes
            });
        }

        /// <summary>
        /// 从路由表中移除选定的映射规则。
        /// </summary>
        private void BtnRemoveMapping_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CommandMappingItem item)
            {
                Mappings.Remove(item);
            }
        }

        /// <summary>
        /// 执行配置参数的业务持久化：将 UI 设定回写至节点实例。
        /// </summary>
        /// <summary>
        /// 执行配置参数的业务持久化：将 UI 设定回写至节点实例。
        /// </summary>
        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            // 1. 更新并保存基础通信参数
            _node.IsServer = RbServer.IsChecked == true;
            _node.Address = TxtAddress.Text;

            // 使用 TryParse 确保端口转换的安全性，防止非法输入导致程序崩溃
            if (int.TryParse(TxtPort.Text, out int port))
            {
                _node.Port = port;
            }
            if (int.TryParse(TxtHttpPort.Text, out int httpPort))
            {
                _node.HttpPort = httpPort;
            }
            _node.ManualCommand = TxtManualCommand.Text;

            // 2. 核心业务更新：清理旧路由并构建新的指令映射字典
            _node.CommandMapping.Clear();
            foreach (var item in Mappings)
            {
                // 校验：触发指令和目标图像源都不能为空
                if (!string.IsNullOrWhiteSpace(item.CommandText) && !string.IsNullOrEmpty(item.TargetSourceId))
                {
                    // 剔除首尾空格，确保口令匹配的严谨性
                    // 注：字典赋值方式 [key] = value 可自动覆盖重复的指令规则
                    _node.CommandMapping[item.CommandText.Trim()] = item.TargetSourceId;
                }
            }

            // 3. 更新节点在画布上的状态摘要（采用工业级规范用语）
            string protocolInfo = _node.IsServer ? $"TCP Server:{_node.Port}" : $"TCP Client:{_node.Address}:{_node.Port}";
            _node.ConfigDisplay = $"通信配置 | {protocolInfo} | HTTP:{_node.HttpPort} | 路由规则数:{_node.CommandMapping.Count}";

            // 4. 关闭窗口并向主线程返回成功状态
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