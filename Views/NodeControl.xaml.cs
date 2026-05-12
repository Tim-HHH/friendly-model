using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ModelHotSwapWorkflow.Models;

namespace ModelHotSwapWorkflow.Views
{
    public partial class NodeControl : UserControl
    {
        public NodeBase ViewModel { get; private set; }
        public event Action<NodeControl> OnDeleteRequested;
        public event Action<NodeControl> OnConfigRequested;
        public event Action<NodeControl, string> OnConnectionStart;
        public event Action<NodeControl> OnPositionChanged;
        public event Action<NodeControl> OnSelected;

        // 【新增】：向主大厅报告“我要休眠/唤醒”的事件
        public event Action<NodeControl> OnToggleSleepRequested;

        private Point dragStart;
        private bool isDragging;
        private bool isSelected;

        public NodeControl(NodeBase node)
        {
            InitializeComponent();
            ViewModel = node;
            TitleText.Text = node.Name;
            ConfigDisplay.Text = node.ConfigDisplay;

            // ==========================================
            // 【新增】：初始化右键菜单（包含删除和休眠功能）
            // ==========================================
            var contextMenu = new ContextMenu();

            var deleteMenuItem = new MenuItem { Header = "删除节点" };
            deleteMenuItem.Click += (s, e) => OnDeleteRequested?.Invoke(this);
            contextMenu.Items.Add(deleteMenuItem);

            var sleepMenuItem = new MenuItem { Header = "休眠 / 唤醒" };
            sleepMenuItem.Click += (s, e) => OnToggleSleepRequested?.Invoke(this);
            contextMenu.Items.Add(sleepMenuItem);

            this.ContextMenu = contextMenu;
            // ==========================================

            this.MouseLeftButtonDown += NodeControl_MouseLeftButtonDown;
            this.MouseMove += NodeControl_MouseMove;
            this.MouseLeftButtonUp += NodeControl_MouseLeftButtonUp;
            this.MouseDoubleClick += NodeControl_MouseDoubleClick;
            SetConnectorsVisibility(Visibility.Collapsed);
        }

        // ==========================================
        // 【新增】：更新界面的视觉效果 (变灰/变亮)
        // ==========================================
        /// <summary>
        /// 更新 UI 视觉状态：休眠时节点半透明，唤醒时恢复完全不透明。
        /// </summary>
        public void UpdateVisualState(bool isEnabled)
        {
            this.Opacity = isEnabled ? 1.0 : 0.4;
        }

        private void NodeControl_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OnConfigRequested?.Invoke(this);
            e.Handled = true;
        }

        private void NodeControl_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            OnSelected?.Invoke(this);
            SetSelected(true);

            dragStart = e.GetPosition(this.Parent as Canvas);
            isDragging = true;
            this.CaptureMouse();
            e.Handled = true;
        }

        private void NodeControl_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging)
            {
                Point current = e.GetPosition(this.Parent as Canvas);
                double left = Canvas.GetLeft(this) + (current.X - dragStart.X);
                double top = Canvas.GetTop(this) + (current.Y - dragStart.Y);
                Canvas.SetLeft(this, left);
                Canvas.SetTop(this, top);
                dragStart = current;
                OnPositionChanged?.Invoke(this);
            }
        }

        private void NodeControl_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isDragging)
            {
                isDragging = false;
                this.ReleaseMouseCapture();
                if (ViewModel != null)
                {
                    ViewModel.X = Canvas.GetLeft(this);
                    ViewModel.Y = Canvas.GetTop(this);
                }
            }
        }

        private void LeftConnector_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            OnConnectionStart?.Invoke(this, "Left");
            e.Handled = true;
        }
        private void RightConnector_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            OnConnectionStart?.Invoke(this, "Right");
            e.Handled = true;
        }
        private void TopConnector_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            OnConnectionStart?.Invoke(this, "Top");
            e.Handled = true;
        }
        private void BottomConnector_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            OnConnectionStart?.Invoke(this, "Bottom");
            e.Handled = true;
        }

        public void SetSelected(bool selected)
        {
            isSelected = selected;
            MainBorder.BorderBrush = selected ? new SolidColorBrush(Colors.OrangeRed) : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            MainBorder.BorderThickness = selected ? new Thickness(3) : new Thickness(1);
            SetConnectorsVisibility(selected ? Visibility.Visible : Visibility.Collapsed);
        }

        public bool IsSelected => isSelected;

        public void UpdateConfigDisplay(string text)
        {
            ConfigDisplay.Text = text;
        }

        private void NodeControl_MouseEnter(object sender, MouseEventArgs e)
        {
            SetConnectorsVisibility(Visibility.Visible);
        }

        private void NodeControl_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!isSelected)
                SetConnectorsVisibility(Visibility.Collapsed);
        }

        private void SetConnectorsVisibility(Visibility visibility)
        {
            LeftConnector.Visibility = visibility;
            RightConnector.Visibility = visibility;
            TopConnector.Visibility = visibility;
            BottomConnector.Visibility = visibility;
        }

        public void UpdateDataSources(List<string> sources)
        {

        }
    }
}