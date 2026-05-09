using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ModelHotSwapWorkflow.Helpers;
using ModelHotSwapWorkflow.Models;
using ModelHotSwapWorkflow.Services;
using ModelHotSwapWorkflow.Views;
using System.IO;
using System.IO.Packaging;



namespace ModelHotSwapWorkflow
{
    public partial class MainWindow : Window
    {
        // 1. 基础数据成员（每个只准留一个！）
        private Dictionary<string, NodeBase> nodes = new Dictionary<string, NodeBase>();
        private Dictionary<NodeControl, NodeBase> controlMap = new Dictionary<NodeControl, NodeBase>();
        private List<Connection> connections = new List<Connection>();
        private List<SelectableLine> selectableLines = new List<SelectableLine>();

        // 2. 交互状态成员
        private NodeControl selectedNodeControl = null;
        private SelectableLine selectedLine = null;

        // 3. 【核心连线成员】 - 彻底清理后的版本，绝不再报错
        private NodeControl connectionStartControl; // 连线起点节点
        private SelectableLine tempLine;           // 正在拉动的那根发光线
        private string currentSourcePin;           // 记录起始引脚（Left/Right等）
        private Point connectionStartPoint;        // 记录起始坐标
        private bool isConnectingFromOutput;        // 连线方向逻辑标志

        // 4. 业务引擎成员
        private WorkflowEngine engine;
        private bool isTriggerMode = false;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            InitializeGlobalTcpNode();
        }

        private void TriggerModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            isTriggerMode = true;
            AddLog("切换到触发模式，等待TCP命令...");

            // 【关键绑定步骤】：告诉 TCP 节点收到消息后来找谁
            var tcpNode = nodes.Values.OfType<TcpCommandNode>().FirstOrDefault();
            if (tcpNode != null)
            {
                tcpNode.MessageReceived -= OnTriggerMessageReceived; // 先解绑，防止重复绑定触发多次
                tcpNode.MessageReceived += OnTriggerMessageReceived; // 正式绑定！
            }
            else
            {
                AddLog("警告：未找到全局 TCP 节点，请确保已配置。");
            }

            BuildEngine();
        }

        private void TriggerModeRadio_Unchecked(object sender, RoutedEventArgs e)
        {
            isTriggerMode = false;
            AddLog("切换到手动模式");

            // 【关键解绑步骤】：手动模式下，不再处理 TCP 自动触发
            var tcpNode = nodes.Values.OfType<TcpCommandNode>().FirstOrDefault();
            if (tcpNode != null)
            {
                tcpNode.MessageReceived -= OnTriggerMessageReceived; // 解绑
            }

            BuildEngine();
        }

        private void ToolboxList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // 获取双击的 ListBoxItem
            var listBox = sender as ListBox;
            var item = listBox?.SelectedItem as ListBoxItem;
            if (item == null || item.Tag == null)
                return;

            string nodeType = item.Tag.ToString();

            // 计算画布中心位置（考虑滚动偏移，但当前画布无滚动条，直接用实际宽高）
            double centerX = WorkflowCanvas.ActualWidth / 2 - 45;   // 节点宽度90的一半
            double centerY = WorkflowCanvas.ActualHeight / 2 - 27;  // 节点高度55的一半

            // 防止画布尚未加载完成时 ActualWidth/Height 为 0
            if (WorkflowCanvas.ActualWidth < 10 || WorkflowCanvas.ActualHeight < 10)
            {
                centerX = 100;
                centerY = 100;
            }

            AddNode(nodeType, centerX, centerY);
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "工作流文件 (*.wf)|*.wf|所有文件 (*.*)|*.*",
                DefaultExt = ".wf",
                FileName = "工作流"
            };
            if (dialog.ShowDialog() != true) return;

            var workflowData = new WorkflowData();

            // 导出节点数据
            foreach (var node in nodes.Values)
            {
                var nodeData = new NodeData
                {
                    Id = node.Id,
                    NodeType = node.NodeType,
                    Name = node.Name,
                    X = node.X,
                    Y = node.Y
                };

                // 根据具体类型填充配置
                switch (node)
                {
                    case ImageSourceNode img:
                        nodeData.ImagePath = img.ImagePath;
                        break;
                    case ModelNode mdl:
                        nodeData.ModelPath = mdl.ModelPath;
                        nodeData.ModelName = mdl.ModelName;
                        break;
                    case TcpCommandNode tcp:
                        nodeData.Port = tcp.Port;
                        nodeData.Address = tcp.Address;
                        nodeData.IsServer = tcp.IsServer;
                        break;
                    case BranchNode br:
                        nodeData.ConditionTargetMap = br.ConditionTargetMap;
                        nodeData.DefaultTargetNodeId = br.DefaultTargetNodeId;
                        break;
                    case ActionNode act:
                        nodeData.ActionName = act.ActionName;
                        nodeData.ActionParameter = act.ActionParameter;
                        break;
                        // DisplayNode 无额外配置
                }

                workflowData.Nodes.Add(nodeData);
            }

            // 导出连线数据
            foreach (var conn in connections)
            {
                workflowData.Connections.Add(new ConnectionData
                {
                    SourceId = conn.SourceId,
                    TargetId = conn.TargetId,
                    SourcePin = conn.SourcePin,
                    TargetPin = conn.TargetPin
                });
            }

            // 序列化为 JSON
            string json = JsonSerializer.Serialize(workflowData, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dialog.FileName, json);
            AddLog($"工作流已导出到: {dialog.FileName}");
        }


        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "工作流文件 (*.wf)|*.wf|所有文件 (*.*)|*.*",
                DefaultExt = ".wf"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                string json = File.ReadAllText(dialog.FileName);
                var workflowData = JsonSerializer.Deserialize<WorkflowData>(json);

                // 清空当前画布（注意：停止TCP等）
                ClearCanvasInternal();

                // 第一步：创建所有节点（但不恢复连线）
                var idMap = new Dictionary<string, string>(); // 旧ID -> 新ID（因GUID重新生成，但我们要保持关系，所以使用原ID）
                                                              // 但实际上我们希望在导入时保持原ID不变，以便连线正确关联。所以修改NodeBase，允许设置Id？
                                                              // 简单方案：让NodeBase的Id可设置（添加setter），导入时强制使用原ID。

                // 临时修改 NodeBase.Id 为可设置（添加 set;）
                // 若不想修改NodeBase，可新建节点后记录旧ID到新节点的映射，并更新连线中的ID。
                // 为简化，我们直接修改 NodeBase 的 Id 属性为 { get; set; }（稍后给出修改）

                // 此处假设 NodeBase.Id 已可写
                foreach (var nd in workflowData.Nodes)
                {
                    NodeBase node = CreateNodeFromData(nd);
                    if (node != null)
                    {
                        node.Id = nd.Id; // 强制使用原ID
                        nodes[node.Id] = node;
                    }
                }

                // 第二步：创建UI控件并放置
                foreach (var nd in workflowData.Nodes)
                {
                    if (nodes.TryGetValue(nd.Id, out var node))
                    {
                        var control = new NodeControl(node);
                        control.OnDeleteRequested += DeleteNode;
                        control.OnConfigRequested += ConfigureNode;
                        control.OnConnectionStart += StartConnection;
                        control.OnPositionChanged += (c) => UpdateConnections();
                        control.OnSelected += OnNodeSelected;
                        // 关键：设置节点位置
                        Canvas.SetLeft(control, node.X);
                        Canvas.SetTop(control, node.Y);

                        WorkflowCanvas.Children.Add(control);
                        controlMap[control] = node;

                        // 特殊节点的额外初始化
                        if (node is DisplayNode disp)
                        {
                            disp.OnImageUpdated += img => Dispatcher.Invoke(() => ResultImage.Source = ImageHelper.ToBitmapSource(img));
                        }
                        else if (node is ActionNode act)
                        {
                            act.OnActionExecuted += msg => AddLog(msg);
                        }
                        else if (node is ModelNode mdl)
                        {
                            // 异步加载模型
                            //if (!string.IsNullOrEmpty(mdl.ModelPath))
                               // _ = pythonRunner.LoadModelStage1Async(mdl.ModelPath, mdl.ModelName);
                            // 更新数据源列表（稍后统一处理）
                        }
                        else if (node is TcpCommandNode tcp)
                        {
                            tcp.StartAsync(); // 启动TCP服务/客户端
                        }
                    }
                }

                // 强制更新布局，确保所有控件的 ActualWidth/ActualHeight 有效
                WorkflowCanvas.UpdateLayout();

                // 第三步：恢复连线
                connections.Clear();
                foreach (var cd in workflowData.Connections)
                {
                    if (nodes.ContainsKey(cd.SourceId) && nodes.ContainsKey(cd.TargetId))
                    {
                        connections.Add(new Connection
                        {
                            SourceId = cd.SourceId,
                            TargetId = cd.TargetId,
                            SourcePin = cd.SourcePin,
                            TargetPin = cd.TargetPin
                        });
                    }
                }
                RedrawAllConnections();

                selectedNodeControl?.SetSelected(false);
                selectedNodeControl = null;
                selectedLine?.SetSelected(false);
                selectedLine = null;


                // 第四步：为模型节点刷新数据源列表（因为此时所有节点已就绪）
                foreach (var node in nodes.Values.OfType<ModelNode>())
                {
                    var sourceNames = nodes.Values.Where(n => n.OutputType != null && n != node)
                        .Select(n => n.Name).ToList();
                    node.AvailableDataSources = sourceNames;
                    var ctrl = controlMap.FirstOrDefault(kv => kv.Value == node).Key;
                    ctrl?.UpdateDataSources(sourceNames);
                }

                BuildEngine(); // 重建引擎

                // 清空选中状态（防止旧引用干扰）
                if (selectedNodeControl != null)
                {
                    selectedNodeControl.SetSelected(false);
                    selectedNodeControl = null;
                }
                if (selectedLine != null)
                {
                    selectedLine.SetSelected(false);
                    selectedLine = null;
                }


                AddLog($"工作流已从 {dialog.FileName} 导入");

                

            }
            catch (Exception ex)
            {
                AddLog($"导入失败: {ex.Message}");
                MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void About_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("模型热插拔工作流\n版本 1.0\n\n支持 ONNX 模型推理与 TCP 触发",
                            "关于", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // 根据数据创建节点实例（不设置Id）
        private NodeBase CreateNodeFromData(NodeData nd)
        {
            NodeBase node = null;
            switch (nd.NodeType)
            {
                case "ImageSource":
                    node = new ImageSourceNode { ImagePath = nd.ImagePath };
                    break;
                case "ModelNode":
                    node = new ModelNode() 
                    {
                        ModelPath = nd.ModelPath,
                        ModelName = nd.ModelName
                    };
                    break;
                case "DisplayNode":
                    node = new DisplayNode();
                    break;
                case "TcpCommand":
                    node = new TcpCommandNode(AddLog) { Port = nd.Port, Address = nd.Address, IsServer = nd.IsServer };
                    break;
                case "Branch":
                    node = new BranchNode
                    {
                        ConditionTargetMap = nd.ConditionTargetMap ?? new Dictionary<string, string>(),
                        DefaultTargetNodeId = nd.DefaultTargetNodeId
                    };
                    break;
                case "Action":
                    node = new ActionNode { ActionName = nd.ActionName, ActionParameter = nd.ActionParameter };
                    break;
                default: return null;
            }
            node.Name = nd.Name;
            node.X = nd.X;
            node.Y = nd.Y;
            node.ConfigDisplay = GetConfigDisplay(node); // 辅助方法，更新显示文本
            return node;
        }

        // 生成配置显示文本
        private string GetConfigDisplay(NodeBase node)
        {
            switch (node)
            {
                case ImageSourceNode img:
                    return string.IsNullOrEmpty(img.ImagePath) ? "双击选择图像" : System.IO.Path.GetFileName(img.ImagePath);
                case ModelNode mdl:
                    return string.IsNullOrEmpty(mdl.ModelName) ? "未选择模型" : $"模型: {mdl.ModelName}";
                case DisplayNode _:
                    return "结果展示";
                case TcpCommandNode tcp:
                    return tcp.IsServer ? $"TCP服务端 监听:{tcp.Port}" : $"TCP客户端 {tcp.Address}:{tcp.Port}";
                case BranchNode br:
                    var mappings = br.ConditionTargetMap.Select(kv => $"{kv.Key}->{nodes.GetValueOrDefault(kv.Value)?.Name ?? "?"}");
                    return string.IsNullOrEmpty(mappings.FirstOrDefault()) ? "未配置" : $"分支: {string.Join(", ", mappings)}";
                case ActionNode act:
                    return string.IsNullOrEmpty(act.ActionName) ? "未配置" : $"动作: {act.ActionName}";
                default: return "未配置";
            }
        }

        // 内部清空方法（不重复记录日志）
        private void ClearCanvasInternal()
        {
            // 1. 停止所有正在运行的 TCP 通讯
            foreach (var node in nodes.Values.OfType<TcpCommandNode>())
                node.Stop();

            // 2. 【核心修复】：直接移除整个连线零件
            // 因为现在 SelectableLine 是个整体，不再有 VisualPath 这些内部零件暴露在外面
            foreach (var sl in selectableLines)
            {
                WorkflowCanvas.Children.Remove(sl); // 直接移除这根“发光曲线”
            }

            // 3. 清理账本
            selectableLines.Clear();
            selectedLine = null;
            selectedNodeControl = null;

            // 4. 清空画布上的所有其余节点和数据
            WorkflowCanvas.Children.Clear();
            nodes.Clear();
            controlMap.Clear();
            connections.Clear();
        }




        private void ToolboxList_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // 直接从鼠标点击的原始元素向上查找 ListBoxItem
            var originalSource = e.OriginalSource as DependencyObject;
            var listBoxItem = FindParent<ListBoxItem>(originalSource);

            if (listBoxItem == null || listBoxItem.Tag == null)
                return;

            string nodeType = listBoxItem.Tag.ToString();

            // 确保画布已完成布局，避免 ActualWidth/Height 为 0
            if (WorkflowCanvas.ActualWidth < 10 || WorkflowCanvas.ActualHeight < 10)
            {
                WorkflowCanvas.UpdateLayout();
            }

            // 计算画布中心位置
            double centerX = WorkflowCanvas.ActualWidth / 2 - 45;   // 节点宽90的一半
            double centerY = WorkflowCanvas.ActualHeight / 2 - 27;  // 节点高55的一半

            // 保底值
            if (centerX < 0) centerX = 100;
            if (centerY < 0) centerY = 100;

            AddNode(nodeType, centerX, centerY);

            e.Handled = true; // 阻止事件继续传播
        }

        private void BuildEngine()
        {
            // 停止旧的触发监听
            engine = new WorkflowEngine(nodes, connections, AddLog);
        }

        private void OnTriggerMessageReceived(string message)
        {
            if (!isTriggerMode) return;

            // 1. 获取当前画布上的全局 TCP 节点
            var tcpNode = nodes.Values.OfType<TcpCommandNode>().FirstOrDefault();
            if (tcpNode == null) return;

            string cmd = message.Trim();
            AddLog($"收到TCP原始信号: {cmd}");

            // 2. 查找指令映射
            if (tcpNode.CommandMapping.TryGetValue(cmd, out string targetSourceId))
            {
                AddLog($"匹配成功！指令 [{cmd}] 触发路径起点 ID: {targetSourceId}");

                // 3. 调用引擎启动独立线程路径
                if (engine == null) BuildEngine();
                _ = engine.ExecuteSourcePathAsync(targetSourceId);
            }
            else
            {
                AddLog($"收到未定义指令: {cmd}，跳过处理。");
            }
        }


        private void OpenTcpMapping_Click(object sender, RoutedEventArgs e)
        {
            // 获取全局唯一的 TCP 节点
            var tcpNode = nodes.Values.OfType<TcpCommandNode>().FirstOrDefault();

            if (tcpNode != null)
            {
                // 弹出你原来的配置对话框
                var dialog = new TcpConfigDialog(tcpNode.IsServer, tcpNode.Address, tcpNode.Port, tcpNode.ManualCommand);
                if (dialog.ShowDialog() == true)
                {
                    tcpNode.IsServer = dialog.IsServer;
                    tcpNode.Address = dialog.Address;
                    tcpNode.Port = dialog.Port;
                    tcpNode.ManualCommand = dialog.ManualCommand;

                    AddLog($"全局 TCP 配置已更新: {tcpNode.Port}");

                    // 重启服务
                    tcpNode.Stop();
                    _ = tcpNode.StartAsync();
                }
            }
            else
            {
                // 如果画布上没有 TCP 节点，由于它是全局工具，我们可以自动创建一个隐藏的节点来管理
                AddLog("提示：请先在手动模式下运行一次，或检查初始化逻辑。");
            }
        }

        // 当触发模式被TCP命令激活时，执行工作流并更新UI
        private async void OnWorkflowTriggered()
        {
            AddLog("收到触发信号，开始执行工作流...");
            try
            {
                await engine.ExecuteAsync();
                AddLog("工作流执行完成");
            }
            catch (Exception ex)
            {
                AddLog($"执行错误: {ex.Message}");
            }
            finally
            {
                // 归还焦点到画布，确保 Delete 键生效
                WorkflowCanvas.Focus();
            }
        }

        private async void RunWorkflow_Click(object sender, RoutedEventArgs e)
        {
            if (engine == null) BuildEngine();
            AddLog("开始执行工作流...");
            try
            {
                await engine.ExecuteAsync();
                AddLog("工作流执行完成");
            }
            catch (Exception ex)
            {
                AddLog($"执行错误: {ex.Message}");
            }
            finally
            {
                WorkflowCanvas.Focus();
            }
        }


        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
            WorkflowCanvas.Focusable = true;
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                if (selectedNodeControl != null)
                {
                    DeleteNode(selectedNodeControl);
                    selectedNodeControl = null;
                    e.Handled = true;
                }
                else if (selectedLine != null)
                {
                    var conn = selectedLine.Connection;
                    if (conn != null && connections.Contains(conn))
                    {
                        connections.Remove(conn);
                        nodes.TryGetValue(conn.SourceId, out var sourceNode);
                        nodes.TryGetValue(conn.TargetId, out var targetNode);
                        string sourceName = sourceNode?.Name ?? "未知节点";
                        string targetName = targetNode?.Name ?? "未知节点";
                        AddLog($"删除连线: {sourceName} → {targetName}");
                        RedrawAllConnections();
                        BuildEngine();
                    }
                    selectedLine = null;
                    e.Handled = true;
                }
            }
        }

        private void ToolboxList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBox listBox && listBox.SelectedItem is ListBoxItem item && item.Tag != null)
            {
                string nodeType = item.Tag.ToString();
                DataObject data = new DataObject(DataFormats.StringFormat, nodeType);
                DragDrop.DoDragDrop(listBox, data, DragDropEffects.Copy);
            }
        }

        private void WorkflowCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 只有点击到画布本身（而非子元素）时才取消选中
            if (e.OriginalSource == WorkflowCanvas)
            {
                ClearAllSelections();
            }
        }

        private void ClearAllSelections()
        {
            // 取消节点选中
            if (selectedNodeControl != null)
            {
                selectedNodeControl.SetSelected(false);
                selectedNodeControl = null;
            }
            // 取消连线选中
            if (selectedLine != null)
            {
                selectedLine.SetSelected(false);
                selectedLine = null;
            }
        }

        private void WorkflowCanvas_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.StringFormat))
            {
                string nodeType = e.Data.GetData(DataFormats.StringFormat).ToString();
                Point dropPoint = e.GetPosition(WorkflowCanvas);
                AddNode(nodeType, dropPoint.X - 90, dropPoint.Y - 55);
            }
        }

        private void AddNode(string nodeType, double x, double y)
        {
            NodeBase node = null;
            switch (nodeType)
            {
                case "ImageSource":
                    node = new ImageSourceNode { Name = $"图像源_{nodes.Count + 1}", X = x, Y = y, ConfigDisplay = "双击选择图像" };
                    break;
                case "ModelNode":
                    node = new ModelNode() { Name = $"模型_{nodes.Count + 1}", X = x, Y = y, ConfigDisplay = "未选择模型" };
                    break;
                case "DisplayNode":
                    node = new DisplayNode { Name = $"显示_{nodes.Count + 1}", X = x, Y = y, ConfigDisplay = "结果展示" };
                    ((DisplayNode)node).OnImageUpdated += img => Dispatcher.Invoke(() => ResultImage.Source = ImageHelper.ToBitmapSource(img));
                    break;
                case "TcpCommand":
                    node = new TcpCommandNode(AddLog) { Name = $"TCP命令_{nodes.Count + 1}", X = x, Y = y, ConfigDisplay = "端口:9999" };
                    break;
                case "Branch":
                    node = new BranchNode { Name = $"分支_{nodes.Count + 1}", X = x, Y = y, ConfigDisplay = "未配置" };
                    break;
                case "Action":
                    node = new ActionNode { Name = $"动作_{nodes.Count + 1}", X = x, Y = y, ConfigDisplay = "未配置" };
                    ((ActionNode)node).OnActionExecuted += msg => AddLog(msg);
                    break;
                default:
                    return;
            }

            // 将节点添加到字典
            nodes[node.Id] = node;

            // 创建对应的UI控件
            var control = new NodeControl(node);
            // 【关键】：只要方块动了，就执行 UpdateConnections 方法
            control.OnPositionChanged += (c) => UpdateConnections();

            // 订阅控件事件
            control.OnDeleteRequested += DeleteNode;
            control.OnConfigRequested += ConfigureNode;
            control.OnConnectionStart += StartConnection;
            control.OnPositionChanged += (c) => UpdateConnections();
            control.OnSelected += OnNodeSelected;   // 订阅选中事件

            // 设置控件在画布上的位置
            Canvas.SetLeft(control, x);
            Canvas.SetTop(control, y);

            // 添加到画布
            WorkflowCanvas.Children.Add(control);

            // 维护控件到节点的映射
            controlMap[control] = node;

            AddLog($"添加节点: {node.Name}");

            // 重建工作流引擎（使节点变更生效）
            BuildEngine();
        }

        private void OnNodeSelected(NodeControl control)
        {
            // 取消之前选中的节点
            if (selectedNodeControl != null && selectedNodeControl != control)
                selectedNodeControl.SetSelected(false);

            // 【关键】：取消之前选中的“新版”连线
            if (selectedLine != null)
            {
                selectedLine.SetSelected(false); // 这一行现在不会报错了
                selectedLine = null;
            }

            selectedNodeControl = control;
            selectedNodeControl.SetSelected(true);
        }

        private void DeleteNode(NodeControl control)
        {
            var node = controlMap[control];
            // 删除所有相关连线
            var toRemove = connections.Where(c => c.SourceId == node.Id || c.TargetId == node.Id).ToList();
            foreach (var conn in toRemove)
            {
                connections.Remove(conn);
                // 如果删除的连线正是选中的连线，清空选中状态
                if (selectedLine?.Connection == conn)
                {
                    selectedLine.SetSelected(false);
                    selectedLine = null;
                }
            }

            // 清除选中状态
            if (selectedNodeControl == control)
                selectedNodeControl = null;

            // 移除控件
            WorkflowCanvas.Children.Remove(control);
            controlMap.Remove(control);
            nodes.Remove(node.Id);

            // 重绘连线（会清除旧的）
            RedrawAllConnections();
            AddLog($"删除节点: {node.Name}");

            BuildEngine();
        }

        /// <summary>
        /// 处理节点的配置请求（双击触发）
        /// </summary>
        private void ConfigureNode(NodeControl control)
        {
            var node = controlMap[control];

            // 1. 图像源配置
            if (node is ImageSourceNode imgNode)
            {
                // 弹出咱们刚刚写的高级图像源配置对话框
                var dialog = new Views.ImageSourceConfigDialog(imgNode);

                // ShowDialog 是阻塞式的，会等待您在窗口点击“确定”或“取消”
                if (dialog.ShowDialog() == true)
                {
                    // 配置成功后，同步更新节点上的文字显示
                    control.UpdateConfigDisplay(imgNode.ConfigDisplay);
                    AddLog($"图像源节点 [{imgNode.Name}] 配置已更新。");
                }
            }
            // 2. 模型节点配置（【这里用上了咱们刚刚写的高级专用配置界面】）
            else if (node is ModelNode modelNode)
            {
                // 1. 提取所有具备输出能力的节点名称，作为该模型节点的潜在输入源
                var sourceNames = nodes.Values
                    .Where(n => n.OutputType != null && n != node)
                    .Select(n => n.Name).ToList();

                modelNode.AvailableDataSources = sourceNames;

                // 2. 实例化并弹出专业的模型配置对话框
                var dialog = new Views.ModelConfigDialog(modelNode);

                if (dialog.ShowDialog() == true)
                {
                    // 配置成功后，同步更新 UI 控件的显示状态
                    control.UpdateConfigDisplay(modelNode.ConfigDisplay);
                    AddLog($"模型节点 [{modelNode.Name}] 配置已更新。");
                }
            }
            // 3. TCP 命令节点配置
            else if (node is TcpCommandNode tcpCmd)
            {
                var dialog = new TcpConfigDialog(tcpCmd.IsServer, tcpCmd.Address, tcpCmd.Port, tcpCmd.ManualCommand);
                if (dialog.ShowDialog() == true)
                {
                    tcpCmd.IsServer = dialog.IsServer;
                    tcpCmd.Address = dialog.Address;
                    tcpCmd.Port = dialog.Port;
                    tcpCmd.ManualCommand = dialog.ManualCommand;

                    string modeText = tcpCmd.IsServer ? $"监听端口:{tcpCmd.Port}" : $"{tcpCmd.Address}:{tcpCmd.Port}";
                    tcpCmd.ConfigDisplay = $"TCP {(tcpCmd.IsServer ? "服务端" : "客户端")} {modeText} | 手动:{tcpCmd.ManualCommand}";
                    control.UpdateConfigDisplay(tcpCmd.ConfigDisplay);

                    tcpCmd.Stop();
                    _ = tcpCmd.StartAsync();
                }
            }
            // 4. 逻辑分支节点配置
            else if (node is BranchNode branch)
            {
                var dialog = new BranchConfigDialog(branch, this.nodes);
                if (dialog.ShowDialog() == true)
                {
                    var mappings = branch.ConditionTargetMap.Select(kv => $"{kv.Key}->{nodes[kv.Value]?.Name}");
                    branch.ConfigDisplay = $"分支: {string.Join(", ", mappings)}";
                    control.UpdateConfigDisplay(branch.ConfigDisplay);
                }
            }
            // 5. 动作节点配置
            else if (node is ActionNode actionNode)
            {
                var dialog = new ActionConfigDialog(actionNode);
                if (dialog.ShowDialog() == true)
                {
                    actionNode.ConfigDisplay = $"动作: {actionNode.ActionName}";
                    control.UpdateConfigDisplay(actionNode.ConfigDisplay);
                }
            }
        }



        private void TempLineMouseMove(object sender, MouseEventArgs e)
        {
            if (tempLine != null)
            {
                // 获取鼠标现在的坐标
                Point currentMousePos = e.GetPosition(WorkflowCanvas);

                // 【核心修复】：不再说 X2、Y2，直接用 UpdatePath 
                tempLine.UpdatePath(connectionStartPoint, currentMousePos);
            }
        }


       

        public void StartConnection(NodeControl control, string pin)
        {
            connectionStartControl = control;
            currentSourcePin = pin;

            // 获取圆点在画布上的精确位置
            connectionStartPoint = GetPinPosition(control, pin);

            // 1. 造出咱们发光的新线条
            tempLine = new SelectableLine();

            // 2. 重点：立即让它显示在起点，别去 (0,0) 乱跑
            tempLine.UpdatePath(connectionStartPoint, connectionStartPoint);

            // 3. 把线放到画布上
            WorkflowCanvas.Children.Add(tempLine);

            // 4. 让鼠标移动和松开事件准备好
            MouseMove += TempLineMouseMove;
            MouseLeftButtonUp += TempLineMouseUp;
        }



        private void TempLineMouseUp(object sender, MouseButtonEventArgs e)
        {
            // 1. 清理拖拽临时线和事件
            MouseMove -= TempLineMouseMove;
            MouseLeftButtonUp -= TempLineMouseUp;
            if (tempLine != null)
            {
                WorkflowCanvas.Children.Remove(tempLine);
                tempLine = null;
            }

            // 2. 获取鼠标下方的目标节点
            var hitElement = WorkflowCanvas.InputHitTest(e.GetPosition(WorkflowCanvas)) as DependencyObject;
            var hitControl = FindParent<NodeControl>(hitElement);

            // 3. 有效目标且不是自身
            if (hitControl != null && hitControl != connectionStartControl)
            {
                var sourceNode = controlMap[connectionStartControl];
                var targetNode = controlMap[hitControl];

                // 4. 检查是否已存在相同节点间的连线（无论方向）
                bool alreadyConnected = connections.Any(c =>
                    (c.SourceId == sourceNode.Id && c.TargetId == targetNode.Id) ||
                    (c.SourceId == targetNode.Id && c.TargetId == sourceNode.Id));

                if (alreadyConnected)
                {
                    AddLog($"节点 {sourceNode.Name} 与 {targetNode.Name} 之间已存在连线，不能重复连接");
                    connectionStartControl = null;
                    return;
                }

                // 5. 计算目标连接点方向（根据鼠标在目标节点上的位置自动选择最近的边）
                string targetPin = GetClosestPin(hitControl, e.GetPosition(hitControl));

                // 6. 类型兼容性检查
                bool typeCompatible = false;

                // 分支节点特殊处理：可以连接任何有输入的目标节点
                if (sourceNode is BranchNode)
                {
                    typeCompatible = targetNode.InputType != null;
                }
                // ModelNode 特殊处理：允许接收 DetectionResult（用于链式 ROI 推理）
                else if (targetNode is ModelNode && sourceNode.OutputType == typeof(DetectionResult))
                {
                    typeCompatible = true;
                }
                // 常规类型检查
                else if (sourceNode.OutputType != null && targetNode.InputType != null)
                {
                    typeCompatible = sourceNode.OutputType.IsAssignableTo(targetNode.InputType);
                }

                if (typeCompatible)
                {
                    // 7. 创建连线，记录引脚信息
                    connections.Add(new Connection
                    {
                        SourceId = sourceNode.Id,
                        TargetId = targetNode.Id,
                        SourcePin = currentSourcePin,   // 起始引脚（从 StartConnection 传入）
                        TargetPin = targetPin           // 自动计算的目标引脚
                    });

                    AddLog($"连线: {sourceNode.Name} ({currentSourcePin}) → {targetNode.Name} ({targetPin})");
                    RedrawAllConnections();
                    BuildEngine();
                }
                else
                {
                    string reason = (targetNode.InputType == null)
                        ? $"{targetNode.Name} 不需要输入数据"
                        : $"{sourceNode.OutputType?.Name ?? "未知"} 无法连接到 {targetNode.InputType.Name}";
                    AddLog($"类型不兼容: {reason}");
                }
            }

            // 8. 重置起始控件
            connectionStartControl = null;
        }



        private string GetClosestPin(NodeControl control, Point mousePos)
        {
            double w = control.ActualWidth, h = control.ActualHeight;
            double leftDist = mousePos.X;
            double rightDist = w - mousePos.X;
            double topDist = mousePos.Y;
            double bottomDist = h - mousePos.Y;
            double min = Math.Min(Math.Min(leftDist, rightDist), Math.Min(topDist, bottomDist));
            if (min == leftDist) return "Left";
            if (min == rightDist) return "Right";
            if (min == topDist) return "Top";
            return "Bottom";
        }


        // 辅助方法：向上查找指定类型的父级元素
        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T t) return t;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private Point GetConnectorPosition(NodeControl control, bool isOutput)
        {
            Point point = isOutput ? new Point(control.ActualWidth, control.ActualHeight / 2) : new Point(0, control.ActualHeight / 2);
            return control.TransformToAncestor(WorkflowCanvas).Transform(point);
        }

        private void RedrawAllConnections()
        {
            // 1. 清掉画布上所有的旧线（通过类型找，更干净）
            var oldLines = WorkflowCanvas.Children.OfType<SelectableLine>().ToList();
            foreach (var line in oldLines) WorkflowCanvas.Children.Remove(line);

            selectableLines.Clear();

            // 2. 遍历账本，把每一根线重新画成贝塞尔曲线
            foreach (var conn in connections)
            {
                var sourceCtrl = controlMap.FirstOrDefault(kv => kv.Value.Id == conn.SourceId).Key;
                var targetCtrl = controlMap.FirstOrDefault(kv => kv.Value.Id == conn.TargetId).Key;

                if (sourceCtrl == null || targetCtrl == null) continue;

                // 计算起点和终点
                Point start = GetPinPosition(sourceCtrl, conn.SourcePin);
                Point end = GetPinPosition(targetCtrl, conn.TargetPin);

                // 创建新零件并画线
                var curveLine = new SelectableLine();
                curveLine.UpdatePath(start, end);

                // 这里我们要把数据存进去，方便以后选中或删除
                // 注意：由于 SelectableLine 用户控件现在没有 Connection 属性，
                // 您可以在 SelectableLine.xaml.cs 里加一个：public Connection Connection { get; set; }

                WorkflowCanvas.Children.Add(curveLine);
                selectableLines.Add(curveLine);
            }
        }

        private Point GetPinPosition(NodeControl control, string pin)
        {
            Point pos = control.TransformToAncestor(WorkflowCanvas).Transform(new Point(0, 0));
            double w = control.ActualWidth, h = control.ActualHeight;
            return pin switch
            {
                "Left" => new Point(pos.X, pos.Y + h / 2),
                "Right" => new Point(pos.X + w, pos.Y + h / 2),
                "Top" => new Point(pos.X + w / 2, pos.Y),
                "Bottom" => new Point(pos.X + w / 2, pos.Y + h),
                _ => new Point(pos.X + w, pos.Y + h / 2)
            };
        }

        private void Line_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Shapes.Path path && path.Tag is Connection conn)
            {
                var selectable = selectableLines.FirstOrDefault(sl => sl.Connection == conn);
                if (selectable != null)
                    SetSelectedLine(selectable);
            }
            e.Handled = true;
        }

        private PathGeometry CreateOrthogonalGeometry(Point start, Point end, string sourcePin, string targetPin)
        {
            var geometry = new PathGeometry();
            var figure = new PathFigure { StartPoint = start };

            // 根据连接点方向计算偏移，使连线不重叠在节点上
            double offset = 10;
            Point p1 = start, p2 = end;

            // 计算中间转折点（简单正交：先横后竖或先竖后横）
            if (Math.Abs(start.X - end.X) > Math.Abs(start.Y - end.Y))
            {
                // 水平差异大，先水平再垂直
                double midX = (start.X + end.X) / 2;
                figure.Segments.Add(new LineSegment(new Point(midX, start.Y), true));
                figure.Segments.Add(new LineSegment(new Point(midX, end.Y), true));
            }
            else
            {
                // 垂直差异大，先垂直再水平
                double midY = (start.Y + end.Y) / 2;
                figure.Segments.Add(new LineSegment(new Point(start.X, midY), true));
                figure.Segments.Add(new LineSegment(new Point(end.X, midY), true));
            }
            figure.Segments.Add(new LineSegment(end, true));
            geometry.Figures.Add(figure);
            return geometry;
        }

        private void SetSelectedLine(SelectableLine line)
        {
            if (selectedNodeControl != null)
            {
                selectedNodeControl.SetSelected(false);
                selectedNodeControl = null;
            }
            if (selectedLine != null && selectedLine != line)
                selectedLine.SetSelected(false);

            selectedLine = line;
            if (selectedLine != null)
                selectedLine.SetSelected(true);
        }

        // 这是一段新建的方法，用来生成藏在幕后的全局TCP指挥官
        private void InitializeGlobalTcpNode()
        {
            var tcpNode = new TcpCommandNode(AddLog)
            {
                Name = "全局TCP指挥官",
                X = -1000, // 我们把它藏在画布的外面（负1000的位置），不让它占地方
                Y = -1000,
                IsServer = true,
                Port = 9999
            };

            // 把这位指挥官登记在系统的花名册（nodes）里
            nodes[tcpNode.Id] = tcpNode;

            // 让指挥官立刻上岗，开始竖起耳朵听外面的消息
            _ = tcpNode.StartAsync();
        }
        public void UpdateConnections()
        {
            // 1. 安全护航：如果账本本身就是空的，直接返回，不干活
            if (selectableLines == null || controlMap == null) return;

            // 使用 ToList() 是为了防止在遍历时账本发生变化导致程序崩溃
            foreach (var sl in selectableLines.ToList())
            {
                var conn = sl.Connection;
                if (conn == null) continue;

                // 【核心修复】：在对比 ID 之前，先用 ?. 检查 Value 是否为空
                // 只有当方块（Value）存在且 ID 匹配时，才把控制权（Key）拿出来
                var sourceCtrl = controlMap.FirstOrDefault(kv => kv.Value?.Id == conn.SourceId).Key;
                var targetCtrl = controlMap.FirstOrDefault(kv => kv.Value?.Id == conn.TargetId).Key;

                // 如果连线的两头有一头找不到了（可能刚被删掉），就跳过这根线
                if (sourceCtrl == null || targetCtrl == null) continue;

                // 只有确定两个方块都在，才去算位置并画曲线
                try
                {
                    Point start = GetPinPosition(sourceCtrl, conn.SourcePin);
                    Point end = GetPinPosition(targetCtrl, conn.TargetPin);
                    sl.UpdatePath(start, end);
                }
                catch
                {
                    // 如果计算坐标时出错了（比如控件还没加载好），也安静地跳过，不报红字
                    continue;
                }
            }
        }



        private void ClearCanvas_Click(object sender, RoutedEventArgs e)
        {
            // 停止所有TCP命令节点
            ClearCanvasInternal();
            AddLog("已清空画布");
            BuildEngine();
        }

        private void AddLog(string msg)
        {
            // 如果当前不在 UI 线程，则调度到 UI 线程；如果已在 UI 线程则直接执行
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AddLog(msg)));
                return;
            }

            // 确保控件存在且未被释放
            if (LogTextBox == null) return;

            try
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                LogTextBox.AppendText($"[{timestamp}] {msg}\n");
                LogTextBox.ScrollToEnd();
            }
            catch (Exception ex)
            {
                // 日志写入失败时，至少输出到调试窗口，避免程序崩溃
                System.Diagnostics.Debug.WriteLine($"日志写入失败: {ex.Message}");
            }
        }

        


    }

    
}