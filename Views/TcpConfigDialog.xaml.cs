using System.Windows;

namespace ModelHotSwapWorkflow.Views
{
    public partial class TcpConfigDialog : Window
    {
        public string Address { get; private set; }
        public int Port { get; private set; }
        public bool IsServer { get; private set; }
        public string ManualCommand { get; private set; }

        public TcpConfigDialog(bool isServer, string defaultAddress = "127.0.0.1", int defaultPort = 8888, string defaultManualCommand = "1")
        {
            InitializeComponent();
            AddressBox.Text = defaultAddress;
            PortBox.Text = defaultPort.ToString();
            ManualCommandBox.Text = defaultManualCommand;

            if (isServer)
            {
                ServerRadio.IsChecked = true;
                AddressBox.IsEnabled = false;
            }
            else
            {
                ClientRadio.IsChecked = true;
                AddressBox.IsEnabled = true;
            }
            IsServer = isServer;
        }

        private void Role_Checked(object sender, RoutedEventArgs e)
        {
            AddressBox.IsEnabled = ClientRadio.IsChecked == true;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            IsServer = ServerRadio.IsChecked == true;
            Address = AddressBox.Text.Trim();
            ManualCommand = ManualCommandBox.Text.Trim();
            if (string.IsNullOrEmpty(Address) && !IsServer)
            {
                MessageBox.Show("请输入服务器地址", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!int.TryParse(PortBox.Text, out int port) || port <= 0 || port > 65535)
            {
                MessageBox.Show("请输入有效的端口号 (1-65535)", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Port = port;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}