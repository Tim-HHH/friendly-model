using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ModelHotSwapWorkflow.Models; // 确保能找到 Connection 类

namespace ModelHotSwapWorkflow
{
    public partial class SelectableLine : UserControl
    {
        public Connection Connection { get; set; }
        // 增加一个事件，告诉 MainWindow 我被选中了
        public event Action<SelectableLine> OnSelected;

        public SelectableLine()
        {
            InitializeComponent();
            // 【关键】：这里一定要是 true，否则点不到！
            this.IsHitTestVisible = true;
        }

        public void UpdatePath(Point start, Point end)
        {
            // 同步更新视觉线和捕鼠器的坐标
            LineFigure.StartPoint = HitFigure.StartPoint = start;
            double offset = Math.Abs(end.X - start.X) / 2;
            if (offset < 40) offset = 40;

            var p1 = new Point(start.X + offset, start.Y);
            var p2 = new Point(end.X - offset, end.Y);

            BezierSeg.Point1 = HitBezier.Point1 = p1;
            BezierSeg.Point2 = HitBezier.Point2 = p2;
            BezierSeg.Point3 = HitBezier.Point3 = end;
        }

        private void HitTestPath_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 触发选中事件
            OnSelected?.Invoke(this);
            e.Handled = true; // 防止点击穿透到下面的方块
        }
        /// <summary>
        /// 【核心补丁】：安装“变色开关”
        /// 当点击线条或按下删除键时，调用此方法切换外观
        /// </summary>
        public void SetSelected(bool isSelected)
        {
            if (isSelected)
            {
                // 选中时变成醒目的橙红色，线条加粗
                MainPath.Stroke = Brushes.OrangeRed;
                MainPath.StrokeThickness = 5;
            }
            else
            {
                // 没选中时恢复低调的蓝色
                MainPath.Stroke = new SolidColorBrush(Color.FromRgb(0, 122, 204));
                MainPath.StrokeThickness = 3;
            }
        }
    }
}