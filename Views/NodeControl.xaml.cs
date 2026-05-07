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
        public event Action<NodeControl> OnSelected;   // 新增选中事件

        private Point dragStart;
        private bool isDragging;
        private bool isSelected;

        public NodeControl(NodeBase node)
        {
            InitializeComponent();
            ViewModel = node;
            TitleText.Text = node.Name;
            ConfigDisplay.Text = node.ConfigDisplay;

            

            this.MouseLeftButtonDown += NodeControl_MouseLeftButtonDown;
            this.MouseMove += NodeControl_MouseMove;
            this.MouseLeftButtonUp += NodeControl_MouseLeftButtonUp;
            this.MouseDoubleClick += NodeControl_MouseDoubleClick;  // 双击触发配置
            SetConnectorsVisibility(Visibility.Collapsed);
        }

        // 双击打开配置
        private void NodeControl_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OnConfigRequested?.Invoke(this);
            e.Handled = true;
        }

        // 单击选中
        private void NodeControl_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 触发选中事件
            OnSelected?.Invoke(this);
            SetSelected(true);

            // 准备拖拽
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




        // 设置选中状态视觉反馈
        public void SetSelected(bool selected)
        {
            isSelected = selected;
            MainBorder.BorderBrush = selected ? new SolidColorBrush(Colors.OrangeRed) : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            MainBorder.BorderThickness = selected ? new Thickness(3) : new Thickness(1);
            SetConnectorsVisibility(selected ? Visibility.Visible : Visibility.Collapsed);
        }

        public bool IsSelected => isSelected;

        // 更新配置显示
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