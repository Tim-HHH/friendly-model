using System;
using System.Windows;

namespace ModelHotSwapWorkflow.Views
{
    public partial class NetronViewerWindow : Window
    {
        public NetronViewerWindow()
        {
            InitializeComponent();
            InitializeBrowser();
        }

        private async void InitializeBrowser()
        {
            try
            {
                await NetronBrowser.EnsureCoreWebView2Async(null);
                // 页面加载完成后隐藏遮罩
                NetronBrowser.NavigationCompleted += (s, e) => {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                };
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法启动可视化组件。请确认已安装 Edge 浏览器环境。\n错误详情: " + ex.Message);
                this.Close();
            }
        }
    }
}