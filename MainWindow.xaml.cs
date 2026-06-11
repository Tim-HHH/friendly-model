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

using OpenCvSharp;

using Point = System.Windows.Point;
using Window = System.Windows.Window;
using Rect = System.Windows.Rect;

namespace ModelHotSwapWorkflow
{
    public partial class MainWindow : Window
    {
        // 1. 基础数据成员
        private Dictionary<string, NodeBase> nodes = new Dictionary<string, NodeBase>();
        private Dictionary<NodeControl, NodeBase> controlMap = new Dictionary<NodeControl, NodeBase>();
        private List<Connection> connections = new List<Connection>();
        private List<SelectableLine> selectableLines = new List<SelectableLine>();

        // 2. 交互状态成员
        private NodeControl selectedNodeControl = null;
        private SelectableLine selectedLine = null;

        // 3. 连线成员
        private NodeControl connectionStartControl;
        private SelectableLine tempLine;
        private string currentSourcePin;
        private Point connectionStartPoint;

        // 4. 业务引擎成员
        private WorkflowEngine engine;
        private bool isTriggerMode = false;

        // 5. 多选与框选状态成员
        private List<NodeControl> multiSelectedNodes = new List<NodeControl>();
        private List<SelectableLine> multiSelectedLines = new List<SelectableLine>();
        private Point selectionStartPoint;
        private Rectangle selectionBox;
        private bool isSelecting = false;

        // 6. 无限画布平移与抓取状态
        private bool isPanning = false;
        private Point panStartPoint;
        private Point panStartOffset;

        // 7. 小地图状态与节流控制
        private System.Windows.Threading.DispatcherTimer minimapTimer;
        private bool needMinimapUpdate = false;
        private bool isMinimapPanning = false;
        private Rect logicBounds;
        private double minimapScale = 1.0;

        // 8. 【BUG修复新增】：全局高帧率自适应边缘滚动引擎成员
        private bool isEdgeScrollingActive = false;
        private double autoPanDx = 0;
        private double autoPanDy = 0;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            InitializeGlobalTcpNode();

            // 性能优化：初始化节流定时器，限制小地图每秒最多刷新 30 次 (约 33ms)
            minimapTimer = new System.Windows.Threading.DispatcherTimer();
            minimapTimer.Interval = TimeSpan.FromMilliseconds(33);
            minimapTimer.Tick += (s, e) => {
                if (needMinimapUpdate)
                {
                    UpdateMinimapRender();
                    needMinimapUpdate = false;
                }
            };
            minimapTimer.Start();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
            WorkflowCanvas.Focusable = true;

            // 屏幕事件侦听管线
            WorkflowCanvas.MouseMove += WorkflowCanvas_MouseMove;
            WorkflowCanvas.MouseLeftButtonUp += WorkflowCanvas_MouseLeftButtonUp;

            // 挂载抓取平移与滚轮缩放交互
            WorkflowCanvas.MouseRightButtonDown += WorkflowCanvas_MouseRightButtonDown;
            WorkflowCanvas.MouseRightButtonUp += WorkflowCanvas_MouseRightButtonUp;
            WorkflowCanvas.MouseWheel += WorkflowCanvas_MouseWheel;
        }

        // ==========================================
        // 【BUG修复 1：全局丝滑边缘自动推图引擎】
        // ==========================================

        /// <summary>
        /// 检查当前鼠标绝对物理坐标是否贴近视口边缘，如果是，计算位移并激活动画心跳
        /// </summary>
        /// <summary>
        /// 检查当前鼠标绝对物理坐标是否贴近视口边缘，如果是，激活动画心跳，
        /// 并根据鼠标侵入边缘的【深度】计算动态无级变速（PRO 级手感）
        /// </summary>
        private void CheckAndTriggerEdgeScroll(Point mouseScreenPos)
        {
            // 【无级变速核心参数配置】
            double edgeThreshold = 120; // 敏感区加宽到 60 像素，给用户更多“微操”控制速度的空间
            double minSpeed = 0.5;     // 刚触碰边缘时的起步速度（极其缓慢，适合精细对准）
            double maxSpeed = 1;    // 鼠标死死贴住屏幕最边缘时的最高速度（适合大范围跑图）

            double dx = 0, dy = 0;
            double width = WorkflowCanvas.ActualWidth;
            double height = WorkflowCanvas.ActualHeight;

            // --- 1. 计算 X 轴的动态阻尼速度 ---
            if (mouseScreenPos.X < edgeThreshold)
            {
                // 靠近左侧：越靠近 0，侵入比例 ratio 越接近 1
                double ratio = (edgeThreshold - Math.Max(0, mouseScreenPos.X)) / edgeThreshold;
                // 使用 1.5 次方曲线实现“起步慢、后段快”的阻尼手感
                dx = minSpeed + (maxSpeed - minSpeed) * Math.Pow(ratio, 1.5);
            }
            else if (mouseScreenPos.X > width - edgeThreshold)
            {
                // 靠近右侧
                double ratio = (edgeThreshold - Math.Max(0, width - mouseScreenPos.X)) / edgeThreshold;
                dx = -(minSpeed + (maxSpeed - minSpeed) * Math.Pow(ratio, 1.5));
            }

            // --- 2. 计算 Y 轴的动态阻尼速度 ---
            if (mouseScreenPos.Y < edgeThreshold)
            {
                // 靠近顶部
                double ratio = (edgeThreshold - Math.Max(0, mouseScreenPos.Y)) / edgeThreshold;
                dy = minSpeed + (maxSpeed - minSpeed) * Math.Pow(ratio, 1.5);
            }
            else if (mouseScreenPos.Y > height - edgeThreshold)
            {
                // 靠近底部
                double ratio = (edgeThreshold - Math.Max(0, height - mouseScreenPos.Y)) / edgeThreshold;
                dy = -(minSpeed + (maxSpeed - minSpeed) * Math.Pow(ratio, 1.5));
            }

            // --- 3. 驱动心跳引擎 ---
            if (dx != 0 || dy != 0)
            {
                autoPanDx = dx;
                autoPanDy = dy;

                if (!isEdgeScrollingActive)
                {
                    isEdgeScrollingActive = true;
                    // 挂载到 WPF 渲染管线底层心跳
                    CompositionTarget.Rendering += OnEdgeScrollRenderTick;
                }
            }
            else
            {
                StopEdgeScrolling();
            }
        }

        private void StopEdgeScrolling()
        {
            if (isEdgeScrollingActive)
            {
                CompositionTarget.Rendering -= OnEdgeScrollRenderTick;
                isEdgeScrollingActive = false;
                autoPanDx = 0;
                autoPanDy = 0;
            }
        }

        private void OnEdgeScrollRenderTick(object sender, EventArgs e)
        {
            // 实时挪动矩阵
            CanvasTranslate.X += autoPanDx;
            CanvasTranslate.Y += autoPanDy;

            // 如果当前处于拉线状态，让正在被拉的线跟着刷新
            if (tempLine != null)
            {
                Point currentMousePos = Mouse.GetPosition(CanvasContent);
                tempLine.UpdatePath(connectionStartPoint, currentMousePos);
            }

            RequestMinimapUpdate();
        }

        // ==========================================
        // 【核心：无限画布交互控制】
        // ==========================================

        private void WorkflowCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == WorkflowCanvas || e.OriginalSource == CanvasContent)
            {
                isPanning = true;
                panStartPoint = e.GetPosition(WorkflowCanvas);
                panStartOffset = new Point(CanvasTranslate.X, CanvasTranslate.Y);
                WorkflowCanvas.CaptureMouse();
                e.Handled = true;
            }
        }

        private void WorkflowCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isPanning)
            {
                isPanning = false;
                WorkflowCanvas.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void WorkflowCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            Point mousePos = e.GetPosition(WorkflowCanvas);
            double zoomFactor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;

            double newScale = CanvasScale.ScaleX * zoomFactor;
            if (newScale < 0.2 || newScale > 3.0) return;

            double oldScaleX = CanvasScale.ScaleX;
            double oldScaleY = CanvasScale.ScaleY;

            CanvasScale.ScaleX = newScale;
            CanvasScale.ScaleY = newScale;

            CanvasTranslate.X = mousePos.X - (mousePos.X - CanvasTranslate.X) * (newScale / oldScaleX);
            CanvasTranslate.Y = mousePos.Y - (mousePos.Y - CanvasTranslate.Y) * (newScale / oldScaleY);

            RequestMinimapUpdate();
            e.Handled = true;
        }

        private void WorkflowCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && e.OriginalSource == WorkflowCanvas)
            {
                CanvasTranslate.X = 0; CanvasTranslate.Y = 0;
                CanvasScale.ScaleX = 1.0; CanvasScale.ScaleY = 1.0;
                AddLog("【视角复位】画布已回归初始位置。");
                RequestMinimapUpdate();
                return;
            }

            if (e.OriginalSource == WorkflowCanvas)
            {
                ClearAllSelections();

                isSelecting = true;
                selectionStartPoint = e.GetPosition(WorkflowCanvas);
                selectionBox = new Rectangle
                {
                    Stroke = Brushes.DodgerBlue,
                    StrokeThickness = 1,
                    Fill = new SolidColorBrush(Color.FromArgb(50, 30, 144, 255)),
                    StrokeDashArray = new DoubleCollection { 2, 2 }
                };
                Canvas.SetLeft(selectionBox, selectionStartPoint.X);
                Canvas.SetTop(selectionBox, selectionStartPoint.Y);
                WorkflowCanvas.Children.Add(selectionBox);
                WorkflowCanvas.CaptureMouse();
            }
        }

        private void WorkflowCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            Point currentScreenPos = e.GetPosition(WorkflowCanvas);

            // 1. 处理右键画布平移
            if (isPanning && !isSelecting)
            {
                CanvasTranslate.X = panStartOffset.X + (currentScreenPos.X - panStartPoint.X);
                CanvasTranslate.Y = panStartOffset.Y + (currentScreenPos.Y - panStartPoint.Y);
                RequestMinimapUpdate();
                return;
            }

            // 2. 处理拉框框选，同时注入【全局边缘滚动检测】
            if (isSelecting && selectionBox != null)
            {
                double x = Math.Min(currentScreenPos.X, selectionStartPoint.X);
                double y = Math.Min(currentScreenPos.Y, selectionStartPoint.Y);
                double width = Math.Abs(currentScreenPos.X - selectionStartPoint.X);
                double height = Math.Abs(currentScreenPos.Y - selectionStartPoint.Y);

                Canvas.SetLeft(selectionBox, x);
                Canvas.SetTop(selectionBox, y);
                selectionBox.Width = width;
                selectionBox.Height = height;

                CheckAndTriggerEdgeScroll(currentScreenPos); // 触发框选边缘推图
            }
        }

        private void WorkflowCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            StopEdgeScrolling(); // 无论在干嘛，松开鼠标立刻停止推图

            if (isSelecting)
            {
                isSelecting = false;
                WorkflowCanvas.ReleaseMouseCapture();

                Rect boxRect = new Rect(Canvas.GetLeft(selectionBox), Canvas.GetTop(selectionBox), selectionBox.Width, selectionBox.Height);
                GeneralTransform transformToLogical = WorkflowCanvas.TransformToDescendant(CanvasContent);
                Rect logicalBoxRect = transformToLogical.TransformBounds(boxRect);

                foreach (var ctrl in controlMap.Keys)
                {
                    Rect nodeRect = new Rect(Canvas.GetLeft(ctrl), Canvas.GetTop(ctrl), ctrl.ActualWidth, ctrl.ActualHeight);
                    if (logicalBoxRect.IntersectsWith(nodeRect))
                    {
                        multiSelectedNodes.Add(ctrl);
                        ctrl.SetSelected(true);
                    }
                }

                foreach (var line in selectableLines)
                {
                    var conn = line.Connection;
                    var sourceCtrl = controlMap.FirstOrDefault(kv => kv.Value?.Id == conn.SourceId).Key;
                    var targetCtrl = controlMap.FirstOrDefault(kv => kv.Value?.Id == conn.TargetId).Key;

                    if (sourceCtrl != null && targetCtrl != null)
                    {
                        Point p1 = GetPinPosition(sourceCtrl, conn.SourcePin);
                        Point p2 = GetPinPosition(targetCtrl, conn.TargetPin);
                        if (logicalBoxRect.IntersectsWith(new Rect(p1, p2)))
                        {
                            multiSelectedLines.Add(line);
                            line.SetSelected(true);
                        }
                    }
                }

                WorkflowCanvas.Children.Remove(selectionBox);
                selectionBox = null;
                RequestMinimapUpdate();
            }
        }

        // ==========================================
        // 【BUG修复 2：小地图高精度安全特征过滤渲染】
        // ==========================================

        private void RequestMinimapUpdate()
        {
            needMinimapUpdate = true;
        }

        /// <summary>
        /// 执行小地图重绘与探照灯计算
        /// </summary>
        private void UpdateMinimapRender()
        {
            if (MinimapCanvas == null || WorkflowCanvas.ActualWidth < 10) return;

            // 1. 获取所有真实节点的逻辑边界
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            int validNodeCount = 0;

            foreach (var node in nodes.Values)
            {
                // 排除全局指挥官
                if (node is TcpCommandNode && node.Name == "全局TCP指挥官") continue;

                minX = Math.Min(minX, node.X);
                minY = Math.Min(minY, node.Y);
                maxX = Math.Max(maxX, node.X + 180);
                maxY = Math.Max(maxY, node.Y + 90);
                validNodeCount++;
            }

            // 【核心修复】：获取当前相机视口（你眼睛看到的屏幕区域）在无限画布里的真实逻辑边界
            double viewX = -CanvasTranslate.X / CanvasScale.ScaleX;
            double viewY = -CanvasTranslate.Y / CanvasScale.ScaleY;
            double viewW = WorkflowCanvas.ActualWidth / CanvasScale.ScaleX;
            double viewH = WorkflowCanvas.ActualHeight / CanvasScale.ScaleY;

            // 【核心修复】：将相机视口强制并入“世界边界”中
            if (validNodeCount == 0)
            {
                // 如果画布全空，世界边界就是当前屏幕视口边界
                minX = viewX;
                minY = viewY;
                maxX = viewX + viewW;
                maxY = viewY + viewH;
            }
            else
            {
                // 如果有节点，世界边界 = 节点群的边界 与 屏幕视口边界 的【最大并集】
                minX = Math.Min(minX, viewX);
                minY = Math.Min(minY, viewY);
                maxX = Math.Max(maxX, viewX + viewW);
                maxY = Math.Max(maxY, viewY + viewH);
            }

            // 统一增加呼吸边距，防止节点或探照灯紧贴小地图边缘
            minX -= 400; minY -= 400; maxX += 400; maxY += 400;

            // 防止奇点：强制保持至少 2000x2000 的最小逻辑视野
            if (maxX - minX < 2000) { double center = (maxX + minX) / 2; minX = center - 1000; maxX = center + 1000; }
            if (maxY - minY < 2000) { double center = (maxY + minY) / 2; minY = center - 1000; maxY = center + 1000; }

            logicBounds = new Rect(minX, minY, maxX - minX, maxY - minY);

            // 2. 计算缩放比例
            double mapW = MinimapBorder.ActualWidth;
            double mapH = MinimapBorder.ActualHeight;
            minimapScale = Math.Min(mapW / logicBounds.Width, mapH / logicBounds.Height);

            // 3. 清空旧红点
            var toRemove = MinimapCanvas.Children.OfType<Ellipse>().ToList();
            foreach (var el in toRemove) MinimapCanvas.Children.Remove(el);

            // 4. 画节点红点
            foreach (var node in nodes.Values)
            {
                if (node is TcpCommandNode && node.Name == "全局TCP指挥官") continue;

                Ellipse dot = new Ellipse { Width = 6, Height = 6, Fill = Brushes.LightGreen, IsHitTestVisible = false };
                Canvas.SetLeft(dot, (node.X - logicBounds.X) * minimapScale);
                Canvas.SetTop(dot, (node.Y - logicBounds.Y) * minimapScale);
                MinimapCanvas.Children.Add(dot);
            }

            // 5. 更新探照灯
            double mapRectX = (viewX - logicBounds.X) * minimapScale;
            double mapRectY = (viewY - logicBounds.Y) * minimapScale;
            double mapRectW = viewW * minimapScale;
            double mapRectH = viewH * minimapScale;

            Canvas.SetLeft(MinimapViewportRect, Math.Max(0, mapRectX));
            Canvas.SetTop(MinimapViewportRect, Math.Max(0, mapRectY));
            MinimapViewportRect.Width = Math.Min(mapW, Math.Max(2, mapRectW));
            MinimapViewportRect.Height = Math.Min(mapH, Math.Max(2, mapRectH));
        }

        private void Minimap_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            isMinimapPanning = true;
            MinimapBorder.CaptureMouse();
            JumpToMinimapPos(e.GetPosition(MinimapCanvas));
            e.Handled = true;
        }

        private void Minimap_MouseMove(object sender, MouseEventArgs e)
        {
            if (isMinimapPanning) JumpToMinimapPos(e.GetPosition(MinimapCanvas));
        }

        private void Minimap_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            isMinimapPanning = false; MinimapBorder.ReleaseMouseCapture();
        }

        private void Minimap_MouseLeave(object sender, MouseEventArgs e)
        {
            if (isMinimapPanning) { isMinimapPanning = false; MinimapBorder.ReleaseMouseCapture(); }
        }

        private void JumpToMinimapPos(Point mapPos)
        {
            double targetLogicX = (mapPos.X / minimapScale) + logicBounds.X;
            double targetLogicY = (mapPos.Y / minimapScale) + logicBounds.Y;

            CanvasTranslate.X = -(targetLogicX * CanvasScale.ScaleX - WorkflowCanvas.ActualWidth / 2);
            CanvasTranslate.Y = -(targetLogicY * CanvasScale.ScaleY - WorkflowCanvas.ActualHeight / 2);
            RequestMinimapUpdate();
        }

        // ==========================================
        // 【业务流节点调度与生成管线】
        // ==========================================

        private void ToolboxList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var listBox = sender as ListBox;
            var item = listBox?.SelectedItem as ListBoxItem;
            if (item == null || item.Tag == null) return;
            SpawnNodeAtCenter(item.Tag.ToString());
        }

        private void ToolboxList_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var originalSource = e.OriginalSource as DependencyObject;
            var listBoxItem = FindParent<ListBoxItem>(originalSource);
            if (listBoxItem == null || listBoxItem.Tag == null) return;
            SpawnNodeAtCenter(listBoxItem.Tag.ToString());
            e.Handled = true;
        }

        private void SpawnNodeAtCenter(string nodeType)
        {
            if (WorkflowCanvas.ActualWidth < 10) WorkflowCanvas.UpdateLayout();

            double viewCenterX = WorkflowCanvas.ActualWidth / 2;
            double viewCenterY = WorkflowCanvas.ActualHeight / 2;
            if (viewCenterX < 10) viewCenterX = 100;
            if (viewCenterY < 10) viewCenterY = 100;

            double logicalX = (viewCenterX - CanvasTranslate.X) / CanvasScale.ScaleX - 45;
            double logicalY = (viewCenterY - CanvasTranslate.Y) / CanvasScale.ScaleY - 27;

            Point smartPos = GetSmartPosition(logicalX, logicalY);
            AddNode(nodeType, smartPos.X, smartPos.Y);
        }

        private Point GetSmartPosition(double initialX, double initialY)
        {
            double targetX = initialX; double targetY = initialY;
            bool hasOverlap = true; double threshold = 20; double offsetDelta = 25;

            while (hasOverlap)
            {
                hasOverlap = false;
                foreach (var node in nodes.Values)
                {
                    if (node is TcpCommandNode && node.Name == "全局TCP指挥官") continue;
                    if (Math.Abs(node.X - targetX) < threshold && Math.Abs(node.Y - targetY) < threshold)
                    {
                        targetX += offsetDelta; targetY += offsetDelta;
                        hasOverlap = true; break;
                    }
                }
            }
            return new Point(targetX, targetY);
        }

        private void SetupNodeEvents(NodeControl control)
        {
            control.OnDeleteRequested += DeleteNode;
            control.OnToggleSleepRequested += ToggleNodeSleepState;
            control.OnConfigRequested += ConfigureNode;
            control.OnConnectionStart += StartConnection;
            control.OnPositionChanged += (c) => UpdateConnections();
            control.OnSelected += OnNodeSelected;

            control.OnDraggedDelta += (draggedCtrl, dx, dy) =>
            {
                if (multiSelectedNodes.Contains(draggedCtrl))
                {
                    foreach (var member in multiSelectedNodes)
                    {
                        if (member == draggedCtrl) continue;
                        double newLeft = Canvas.GetLeft(member) + dx;
                        double newTop = Canvas.GetTop(member) + dy;
                        Canvas.SetLeft(member, newLeft); Canvas.SetTop(member, newTop);

                        if (controlMap.TryGetValue(member, out var vm))
                        {
                            vm.X = newLeft; vm.Y = newTop;
                        }
                    }
                    UpdateConnections();
                }

                // 【修复集成】：移动节点靠近物理边缘时，一并激活全局智能推图
                Point mousePos = Mouse.GetPosition(WorkflowCanvas);
                CheckAndTriggerEdgeScroll(mousePos);

                RequestMinimapUpdate();
            };
        }
         
        private void AddNode(string nodeType, double x, double y)
        {
            NodeBase node = null;
            switch (nodeType)
            {
                case "ImageSource": node = new ImageSourceNode { Name = $"图像源_{nodes.Count + 1}", X = x, Y = y, ConfigDisplay = "双击选择图像" }; break;
                case "ModelNode": node = new ModelNode() { Name = $"模型_{nodes.Count + 1}", X = x, Y = y, ConfigDisplay = "未选择模型" }; break;
                case "DisplayNode":
                    node = new DisplayNode { Name = $"显示_{nodes.Count + 1}", X = x, Y = y, ConfigDisplay = "[显示] 渲染框: 开" };
                    ((DisplayNode)node).OnImageProcessed += mat => Dispatcher.Invoke(() =>
                    {
                        using (var bmp = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(mat))
                        {
                            ResultImage.Source = ImageHelper.ToBitmapSource(bmp);
                        }
                    });
                    break;
                case "TcpCommand": node = new TcpCommandNode(AddLog) { Name = $"TCP命令_{nodes.Count + 1}", X = x, Y = y, ConfigDisplay = "端口:9999" }; break;
                case "Branch": node = new BranchNode { Name = $"分支_{nodes.Count + 1}", X = x, Y = y, ConfigDisplay = "未配置" }; break;
                case "Action":
                    node = new ActionNode { Name = $"动作_{nodes.Count + 1}", X = x, Y = y, ConfigDisplay = "[动作] 日志 : 检测完成" };
                    ((ActionNode)node).OnActionExecuted += msg => AddLog(msg);
                    break;
                default: return;
            }

            nodes[node.Id] = node;
            var control = new NodeControl(node);
            SetupNodeEvents(control);

            Canvas.SetLeft(control, x); Canvas.SetTop(control, y);
            CanvasContent.Children.Add(control);
            controlMap[control] = node;

            AddLog($"添加节点: {node.Name}");
            BuildEngine();
            RequestMinimapUpdate();
        }

        // ==========================================
        // 【基础通信、数据导入导出、连线构建管线】
        // ==========================================

        private void ModeChanged(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            bool isManual = ManualModeRadio.IsChecked == true;
            isTriggerMode = !isManual;

            BtnConfig.IsEnabled = isManual; BtnImport.IsEnabled = isManual; BtnExport.IsEnabled = isManual;
            BtnClear.IsEnabled = isManual; BtnRun.IsEnabled = isManual; WorkflowCanvas.IsEnabled = isManual; ToolboxList.IsEnabled = isManual;

            var tcpNode = nodes.Values.OfType<TcpCommandNode>().FirstOrDefault(n => n.Name != "全局TCP指挥官");
            if (tcpNode != null)
            {
                tcpNode.MessageReceived -= OnTriggerMessageReceived; tcpNode.HttpMessageReceived -= OnHttpTriggerReceived;

                if (TcpTriggerModeRadio.IsChecked == true)
                {
                    AddLog("【系统锁定】已切换至 TCP 触发模式。等待信号...");
                    tcpNode.MessageReceived += OnTriggerMessageReceived;
                }
                else if (HttpTriggerModeRadio.IsChecked == true)
                {
                    AddLog("【系统锁定】已切换至 上位机模式。等待 HTTP 传图...");
                    tcpNode.HttpMessageReceived += OnHttpTriggerReceived;
                }
                else
                {
                    AddLog("【系统解锁】已切换至 手动模式。");
                }
            }
            BuildEngine();
        }

        private void OnHttpTriggerReceived(string cmd, byte[] imgData)
        {
            if (!isTriggerMode) return;
            var tcpNode = nodes.Values.OfType<TcpCommandNode>().FirstOrDefault(n => n.Name != "全局TCP指挥官");
            if (tcpNode == null) return;

            if (tcpNode.CommandMapping.TryGetValue(cmd.Trim(), out string targetSourceId))
            {
                AddLog($"上位机 HTTP 触发！指令 [{cmd}]");
                Mat tensor = Cv2.ImDecode(imgData, ImreadModes.Color);
                if (tensor.Empty()) return;
                TensorPayload payload = new TensorPayload { BaseTensor = tensor };
                if (engine == null) BuildEngine();
                _ = engine.ExecuteSourcePathAsync(targetSourceId, payload);
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

        private void WorkflowCanvas_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.StringFormat))
            {
                string nodeType = e.Data.GetData(DataFormats.StringFormat).ToString();
                Point dropPoint = e.GetPosition(CanvasContent);
                AddNode(nodeType, dropPoint.X - 45, dropPoint.Y - 27);
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "工作流文件 (*.wf)|*.wf", DefaultExt = ".wf", FileName = "工作流" };
            if (dialog.ShowDialog() != true) return;

            var workflowData = new WorkflowData();
            foreach (var node in nodes.Values)
            {
                if (node.Name == "全局TCP指挥官") continue; // 导出时过滤全局专属节点
                var nodeData = new NodeData { Id = node.Id, NodeType = node.NodeType, Name = node.Name, X = node.X, Y = node.Y };
                switch (node)
                {
                    case ImageSourceNode img: nodeData.ImagePath = img.ImagePath; break;
                    case ModelNode mdl: nodeData.ModelPath = mdl.ModelPath; nodeData.ModelName = mdl.ModelName; break;
                    case TcpCommandNode tcp: nodeData.Port = tcp.Port; nodeData.Address = tcp.Address; nodeData.IsServer = tcp.IsServer; break;
                    case BranchNode br: nodeData.ConditionTargetMap = br.ConditionTargetMap; nodeData.DefaultTargetNodeId = br.DefaultTargetNodeId; break;
                    case ActionNode act: nodeData.ActionType = (int)act.ActionType; nodeData.CustomMessage = act.CustomMessage; nodeData.ExportCsvPath = act.ExportCsvPath; break;
                    case DisplayNode disp: nodeData.DrawBoundingBox = disp.DrawBoundingBox; nodeData.DrawLabel = disp.DrawLabel; nodeData.SaveImagePath = disp.SaveImagePath; break;
                }
                workflowData.Nodes.Add(nodeData);
            }

            foreach (var conn in connections)
            {
                workflowData.Connections.Add(new ConnectionData { SourceId = conn.SourceId, TargetId = conn.TargetId, SourcePin = conn.SourcePin, TargetPin = conn.TargetPin });
            }

            string json = JsonSerializer.Serialize(workflowData, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dialog.FileName, json);
            AddLog($"工作流已导出到: {dialog.FileName}");
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "工作流文件 (*.wf)|*.wf", DefaultExt = ".wf" };
            if (dialog.ShowDialog() != true) return;
            try
            {
                string json = File.ReadAllText(dialog.FileName);
                var workflowData = JsonSerializer.Deserialize<WorkflowData>(json);

                ClearCanvasInternal();

                foreach (var nd in workflowData.Nodes)
                {
                    NodeBase node = CreateNodeFromData(nd);
                    if (node != null) { node.Id = nd.Id; nodes[node.Id] = node; }
                }

                foreach (var nd in workflowData.Nodes)
                {
                    if (nodes.TryGetValue(nd.Id, out var node))
                    {
                        var control = new NodeControl(node);
                        CanvasContent.Children.Add(control);
                        controlMap[control] = node;
                        SetupNodeEvents(control);
                        Canvas.SetLeft(control, node.X); Canvas.SetTop(control, node.Y);

                        if (node is DisplayNode disp)
                        {
                            disp.OnImageProcessed += mat => Dispatcher.Invoke(() => {
                                using (var bmp = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(mat))
                                    ResultImage.Source = ImageHelper.ToBitmapSource(bmp);
                            });
                        }
                        else if (node is ActionNode act) { act.OnActionExecuted += msg => AddLog(msg); }
                        else if (node is TcpCommandNode tcp) { tcp.StartAsync(); }
                    }
                }
                WorkflowCanvas.UpdateLayout();

                connections.Clear();
                foreach (var cd in workflowData.Connections)
                {
                    if (nodes.ContainsKey(cd.SourceId) && nodes.ContainsKey(cd.TargetId))
                        connections.Add(new Connection { SourceId = cd.SourceId, TargetId = cd.TargetId, SourcePin = cd.SourcePin, TargetPin = cd.TargetPin });
                }
                RedrawAllConnections();
                ClearAllSelections();

                foreach (var node in nodes.Values.OfType<ModelNode>())
                {
                    var sourceNames = nodes.Values.Where(n => n.OutputType != null && n != node).Select(n => n.Name).ToList();
                    node.AvailableDataSources = sourceNames;
                    var ctrl = controlMap.FirstOrDefault(kv => kv.Value == node).Key;
                    ctrl?.UpdateDataSources(sourceNames);
                }

                BuildEngine(); AddLog($"工作流已导入");
                RequestMinimapUpdate();
            }
            catch (Exception ex) { AddLog($"导入失败: {ex.Message}"); }
        }

        private NodeBase CreateNodeFromData(NodeData nd)
        {
            NodeBase node = null;
            switch (nd.NodeType)
            {
                case "ImageSource": node = new ImageSourceNode { ImagePath = nd.ImagePath }; break;
                case "ModelNode": node = new ModelNode() { ModelPath = nd.ModelPath, ModelName = nd.ModelName }; break;
                case "DisplayNode": node = new DisplayNode { DrawBoundingBox = nd.DrawBoundingBox, DrawLabel = nd.DrawLabel, SaveImagePath = nd.SaveImagePath }; break;
                case "TcpCommand": node = new TcpCommandNode(AddLog) { Port = nd.Port, Address = nd.Address, IsServer = nd.IsServer }; break;
                case "Branch": node = new BranchNode { ConditionTargetMap = nd.ConditionTargetMap ?? new Dictionary<string, string>(), DefaultTargetNodeId = nd.DefaultTargetNodeId }; break;
                case "Action": node = new ActionNode { ActionType = (ActionTargetType)nd.ActionType, CustomMessage = nd.CustomMessage, ExportCsvPath = nd.ExportCsvPath }; break;
                default: return null;
            }
            node.Name = nd.Name; node.X = nd.X; node.Y = nd.Y; node.ConfigDisplay = GetConfigDisplay(node);
            return node;
        }

        private string GetConfigDisplay(NodeBase node)
        {
            switch (node)
            {
                case ImageSourceNode img: return string.IsNullOrEmpty(img.ImagePath) ? "双击选择图像" : System.IO.Path.GetFileName(img.ImagePath);
                case ModelNode mdl: return string.IsNullOrEmpty(mdl.ModelName) ? "未选择模型" : $"模型: {mdl.ModelName}";
                case DisplayNode disp: return $"[显示] 渲染框: {(disp.DrawBoundingBox ? "开" : "关")}";
                case TcpCommandNode tcp: return tcp.IsServer ? $"TCP服务端 监听:{tcp.Port}" : $"TCP客户端 {tcp.Address}:{tcp.Port}";
                case BranchNode br: return string.IsNullOrEmpty(br.ConditionTargetMap.FirstOrDefault().Key) ? "未配置" : $"分支已配";
                case ActionNode act: string typeStr = act.ActionType == ActionTargetType.PrintLog ? "日志" : "存CSV"; return $"[动作] {typeStr} : {act.CustomMessage}";
                default: return "未配置";
            }
        }

        private void ClearCanvasInternal()
        {
            foreach (var node in nodes.Values.OfType<TcpCommandNode>()) if (node.Name != "全局TCP指挥官") node.Stop();
            foreach (var sl in selectableLines) CanvasContent.Children.Remove(sl);
            selectableLines.Clear(); selectedLine = null; selectedNodeControl = null;

            CanvasContent.Children.Clear();
            nodes.Clear(); controlMap.Clear(); connections.Clear();
            InitializeGlobalTcpNode(); // 重新加载指挥官

            CanvasTranslate.X = 0; CanvasTranslate.Y = 0; CanvasScale.ScaleX = 1.0; CanvasScale.ScaleY = 1.0;
            RequestMinimapUpdate();
        }

        private void BtnOpenSlicer_Click(object sender, RoutedEventArgs e)
        {
            var slicerWindow = new Views.ModelSlicerWindow { Owner = this };
            slicerWindow.ShowDialog();
        }

        private void BuildEngine() { engine = new WorkflowEngine(nodes, connections, AddLog); }

        private void OnTriggerMessageReceived(string message)
        {
            if (!isTriggerMode) return;
            var tcpNode = nodes.Values.OfType<TcpCommandNode>().FirstOrDefault(n => n.Name != "全局TCP指挥官");
            if (tcpNode == null) return;
            string cmd = message.Trim();
            if (tcpNode.CommandMapping.TryGetValue(cmd, out string targetSourceId))
            {
                if (engine == null) BuildEngine();
                _ = engine.ExecuteSourcePathAsync(targetSourceId);
            }
        }

        private void OpenTcpMapping_Click(object sender, RoutedEventArgs e)
        {
            var tcpNode = nodes.Values.OfType<TcpCommandNode>().FirstOrDefault(n => n.Name != "全局TCP指挥官");
            if (tcpNode != null)
            {
                var dialog = new TcpConfigDialog(tcpNode, this.nodes.Values.ToList());
                if (dialog.ShowDialog() == true) { tcpNode.Stop(); _ = tcpNode.StartAsync(); }
            }
        }

        private async void RunWorkflow_Click(object sender, RoutedEventArgs e)
        {
            if (isTriggerMode) return;
            if (engine == null) BuildEngine();
            AddLog("开始执行工作流...");
            try
            {
                var tcpNode = nodes.Values.OfType<TcpCommandNode>().FirstOrDefault(n => n.Name != "全局TCP指挥官");
                if (tcpNode != null && !string.IsNullOrWhiteSpace(tcpNode.ManualCommand))
                {
                    string simCmd = tcpNode.ManualCommand.Trim();
                    if (tcpNode.CommandMapping.TryGetValue(simCmd, out string targetSourceId))
                        await engine.ExecuteSourcePathAsync(targetSourceId, null);
                    else
                        await engine.ExecuteAsync();
                }
                else { await engine.ExecuteAsync(); }
            }
            catch (Exception ex) { AddLog($"执行错误: {ex.Message}"); }
            finally { WorkflowCanvas.Focus(); }
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                foreach (var node in multiSelectedNodes.ToList()) DeleteNode(node);
                foreach (var line in multiSelectedLines.ToList()) DeleteLine(line);
                if (selectedNodeControl != null) DeleteNode(selectedNodeControl);
                if (selectedLine != null) DeleteLine(selectedLine);

                multiSelectedNodes.Clear(); multiSelectedLines.Clear();
                selectedNodeControl = null; selectedLine = null;
                RequestMinimapUpdate();
                e.Handled = true;
            }
        }

        private void ClearAllSelections()
        {
            if (selectedNodeControl != null) { selectedNodeControl.SetSelected(false); selectedNodeControl = null; }
            if (selectedLine != null) { selectedLine.SetSelected(false); selectedLine = null; }
            foreach (var node in multiSelectedNodes) node.SetSelected(false);
            multiSelectedNodes.Clear();
            foreach (var line in multiSelectedLines) line.SetSelected(false);
            multiSelectedLines.Clear();
        }

        private void OnNodeSelected(NodeControl control)
        {
            if (multiSelectedNodes.Contains(control)) return;
            ClearAllSelections();
            selectedNodeControl = control; selectedNodeControl.SetSelected(true);
        }

        private void DeleteNode(NodeControl control)
        {
            var node = controlMap[control];
            var toRemove = connections.Where(c => c.SourceId == node.Id || c.TargetId == node.Id).ToList();
            foreach (var conn in toRemove)
            {
                connections.Remove(conn);
                if (selectedLine?.Connection == conn) { selectedLine.SetSelected(false); selectedLine = null; }
            }
            if (selectedNodeControl == control) selectedNodeControl = null;

            CanvasContent.Children.Remove(control);
            controlMap.Remove(control); nodes.Remove(node.Id);

            RedrawAllConnections(); AddLog($"删除节点: {node.Name}"); BuildEngine();
            RequestMinimapUpdate();
        }

        private void ConfigureNode(NodeControl control)
        {
            var node = controlMap[control];
            if (node is ImageSourceNode imgNode)
            {
                var dialog = new Views.ImageSourceConfigDialog(imgNode) { Owner = this };
                if (dialog.ShowDialog() == true) control.UpdateConfigDisplay(imgNode.ConfigDisplay);
            }
            else if (node is ModelNode modelNode)
            {
                modelNode.AvailableDataSources = nodes.Values.Where(n => n.OutputType != null && n != node).Select(n => n.Name).ToList();
                var dialog = new Views.ModelConfigDialog(modelNode) { Owner = this };
                if (dialog.ShowDialog() == true) control.UpdateConfigDisplay(modelNode.ConfigDisplay);
            }
            else if (node is TcpCommandNode tcpCmd)
            {
                var dialog = new TcpConfigDialog(tcpCmd, this.nodes.Values.ToList()) { Owner = this };
                if (dialog.ShowDialog() == true) { tcpCmd.Stop(); _ = tcpCmd.StartAsync(); }
            }
            else if (node is BranchNode branch)
            {
                var dialog = new BranchConfigDialog(branch, this.nodes.Values.ToList()) { Owner = this };
                if (dialog.ShowDialog() == true) control.UpdateConfigDisplay(branch.ConfigDisplay);
            }
            else if (node is ActionNode actionNode)
            {
                var dialog = new ActionConfigDialog(actionNode) { Owner = this };
                if (dialog.ShowDialog() == true) control.UpdateConfigDisplay(actionNode.ConfigDisplay);
            }
            else if (node is DisplayNode dispNode)
            {
                var dialog = new DisplayConfigDialog(dispNode) { Owner = this };
                if (dialog.ShowDialog() == true) control.UpdateConfigDisplay(dispNode.ConfigDisplay);
            }
            RequestMinimapUpdate();
        }

        public void StartConnection(NodeControl control, string pin)
        {
            connectionStartControl = control; currentSourcePin = pin;
            connectionStartPoint = GetPinPosition(control, pin);
            tempLine = new SelectableLine();
            tempLine.UpdatePath(connectionStartPoint, connectionStartPoint);
            CanvasContent.Children.Add(tempLine);

            MouseMove += TempLineMouseMove;
            MouseLeftButtonUp += TempLineMouseUp;
        }

        private void TempLineMouseMove(object sender, MouseEventArgs e)
        {
            if (tempLine != null)
            {
                Point screenPos = e.GetPosition(WorkflowCanvas);
                // 【升级组件】：引入全新的全局心跳驱动，拖拽拉线到边缘时，画面会平稳自主移动
                CheckAndTriggerEdgeScroll(screenPos);

                Point currentMousePos = e.GetPosition(CanvasContent);
                tempLine.UpdatePath(connectionStartPoint, currentMousePos);
            }
        }

        private void TempLineMouseUp(object sender, MouseButtonEventArgs e)
        {
            MouseMove -= TempLineMouseMove;
            MouseLeftButtonUp -= TempLineMouseUp;
            StopEdgeScrolling(); // 连线松手，停止推图

            if (tempLine != null) { CanvasContent.Children.Remove(tempLine); tempLine = null; }

            var hitElement = WorkflowCanvas.InputHitTest(e.GetPosition(WorkflowCanvas)) as DependencyObject;
            var hitControl = FindParent<NodeControl>(hitElement);

            if (hitControl != null && hitControl != connectionStartControl)
            {
                var sourceNode = controlMap[connectionStartControl];
                var targetNode = controlMap[hitControl];
                bool alreadyConnected = connections.Any(c => (c.SourceId == sourceNode.Id && c.TargetId == targetNode.Id) || (c.SourceId == targetNode.Id && c.TargetId == sourceNode.Id));
                if (alreadyConnected) { connectionStartControl = null; return; }

                string targetPin = GetClosestPin(hitControl, e.GetPosition(hitControl));
                bool typeCompatible = false;

                if (sourceNode is BranchNode) typeCompatible = targetNode.InputType != null;
                else if (targetNode is ModelNode && sourceNode.OutputType == typeof(DetectionResult)) typeCompatible = true;
                else if (sourceNode.OutputType != null && targetNode.InputType != null) typeCompatible = sourceNode.OutputType.IsAssignableTo(targetNode.InputType);

                if (typeCompatible)
                {
                    connections.Add(new Connection { SourceId = sourceNode.Id, TargetId = targetNode.Id, SourcePin = currentSourcePin, TargetPin = targetPin });
                    RedrawAllConnections(); BuildEngine();
                }
            }
            connectionStartControl = null;
            RequestMinimapUpdate();
        }

        private void ToggleNodeSleepState(NodeControl control)
        {
            var node = controlMap[control]; node.ToggleEnableState();
            control.UpdateVisualState(node.IsEnabled);
        }

        private string GetClosestPin(NodeControl control, Point mousePos)
        {
            double w = control.ActualWidth, h = control.ActualHeight;
            double leftDist = mousePos.X, rightDist = w - mousePos.X, topDist = mousePos.Y, bottomDist = h - mousePos.Y;
            double min = Math.Min(Math.Min(leftDist, rightDist), Math.Min(topDist, bottomDist));
            if (min == leftDist) return "Left";
            if (min == rightDist) return "Right";
            if (min == topDist) return "Top";
            return "Bottom";
        }

        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T t) return t; child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private void RedrawAllConnections()
        {
            var oldLines = CanvasContent.Children.OfType<SelectableLine>().ToList();
            foreach (var line in oldLines) CanvasContent.Children.Remove(line);
            selectableLines.Clear();

            foreach (var conn in connections)
            {
                var sourceCtrl = controlMap.FirstOrDefault(kv => kv.Value.Id == conn.SourceId).Key;
                var targetCtrl = controlMap.FirstOrDefault(kv => kv.Value.Id == conn.TargetId).Key;
                if (sourceCtrl == null || targetCtrl == null) continue;

                Point start = GetPinPosition(sourceCtrl, conn.SourcePin);
                Point end = GetPinPosition(targetCtrl, conn.TargetPin);
                var curveLine = new SelectableLine();
                curveLine.UpdatePath(start, end); curveLine.Connection = conn;
                curveLine.OnSelected += (line) => { SetSelectedLine(line); }; curveLine.OnDeleteRequested += DeleteLine;

                CanvasContent.Children.Add(curveLine); selectableLines.Add(curveLine);
            }
        }

        private Point GetPinPosition(NodeControl control, string pin)
        {
            Point pos = control.TransformToAncestor(CanvasContent).Transform(new Point(0, 0));
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

        private void DeleteLine(SelectableLine line)
        {
            var conn = line.Connection;
            if (conn != null && connections.Contains(conn)) { connections.Remove(conn); RedrawAllConnections(); BuildEngine(); }
            if (selectedLine == line) selectedLine = null;
            RequestMinimapUpdate();
        }

        private void SetSelectedLine(SelectableLine line)
        {
            if (selectedNodeControl != null) { selectedNodeControl.SetSelected(false); selectedNodeControl = null; }
            if (selectedLine != null && selectedLine != line) selectedLine.SetSelected(false);
            selectedLine = line; if (selectedLine != null) selectedLine.SetSelected(true);
        }

        private void InitializeGlobalTcpNode()
        {
            var tcpNode = new TcpCommandNode(AddLog) { Name = "全局TCP指挥官", X = -1000, Y = -1000, IsServer = true, Port = 9999 };
            nodes[tcpNode.Id] = tcpNode;
            _ = tcpNode.StartAsync();
        }

        public void UpdateConnections()
        {
            if (selectableLines == null || controlMap == null) return;
            foreach (var sl in selectableLines.ToList())
            {
                var conn = sl.Connection; if (conn == null) continue;
                var sourceCtrl = controlMap.FirstOrDefault(kv => kv.Value?.Id == conn.SourceId).Key;
                var targetCtrl = controlMap.FirstOrDefault(kv => kv.Value?.Id == conn.TargetId).Key;
                if (sourceCtrl == null || targetCtrl == null) continue;
                try
                {
                    Point start = GetPinPosition(sourceCtrl, conn.SourcePin);
                    Point end = GetPinPosition(targetCtrl, conn.TargetPin);
                    sl.UpdatePath(start, end);
                }
                catch { continue; }
            }
        }

        private void ClearCanvas_Click(object sender, RoutedEventArgs e)
        {
            ClearCanvasInternal(); AddLog("已清空画布"); BuildEngine();
        }

        private void AddLog(string msg)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => AddLog(msg))); return; }
            if (LogTextBox == null) return;
            try
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                LogTextBox.AppendText($"[{timestamp}] {msg}\n"); LogTextBox.ScrollToEnd();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"日志写入失败: {ex.Message}"); }
        }
    }
}