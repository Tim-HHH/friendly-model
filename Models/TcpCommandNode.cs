using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ModelHotSwapWorkflow.Services;

namespace ModelHotSwapWorkflow.Models
{
    public class TcpCommandNode : NodeBase
    {
        public static bool IsManualMode { get; set; } = true;

        public int Port { get; set; } = 9999;
        public string Address { get; set; } = "127.0.0.1";
        public bool IsServer { get; set; } = true;

        // 【新增】：HTTP 监听端口
        public int HttpPort { get; set; } = 8080;

        public string ManualCommand { get; set; } = "1";

        // TCP 事件
        public event Action<string> MessageReceived;
        // 【新增】：HTTP 事件（附带图片字节）
        public event Action<string, byte[]> HttpMessageReceived;

        private TcpCommunicationService tcpService;
        private HttpCommunicationService httpService; // 【新增】HTTP 服务

        private string receivedCommand;
        private readonly object lockObj = new object();
        private readonly Action<string> logAction;

        public Dictionary<string, string> CommandMapping { get; set; } = new Dictionary<string, string>();

        public TcpCommandNode(Action<string> logAction = null)
        {
            this.logAction = logAction;
        }

        public override string NodeType => "TcpCommand";
        public override Type InputType => null;
        public override Type OutputType => typeof(string);

        public async Task StartAsync()
        {
            Stop();

            // 1. 启动原来的 TCP 服务
            tcpService = new TcpCommunicationService();
            tcpService.OnConnected += () =>
            {
                if (IsServer) logAction?.Invoke($"TCP 服务端已建立连接 (端口 {Port})");
                else logAction?.Invoke($"TCP 客户端已连接到 {Address}:{Port}");
            };
            tcpService.OnMessageReceived += OnTcpMessage;

            if (IsServer) await tcpService.StartServerAsync(Port);
            else await tcpService.ConnectAsync(Address, Port);

            // 2. 【新增】：同步启动 HTTP 服务
            httpService = new HttpCommunicationService();
            httpService.OnLog += logAction;
            httpService.OnHttpPostReceived += (cmd, img) => HttpMessageReceived?.Invoke(cmd, img);
            httpService.StartServer(HttpPort);
        }

        private void OnTcpMessage(string message)
        {
            logAction?.Invoke($"TCP 收到测试消息: {message}");
            MessageReceived?.Invoke(message);
            lock (lockObj) { receivedCommand = message.Trim(); }
        }

        public override async Task<object> Process(object input)
        {
            // 这里保留您原有的流程阻塞逻辑
            if (IsManualMode) return ManualCommand ?? "1";

            int timeout = 10000;
            int elapsed = 0;
            while (string.IsNullOrEmpty(receivedCommand) && elapsed < timeout)
            {
                await Task.Delay(100);
                elapsed += 100;
            }
            lock (lockObj)
            {
                string cmd = receivedCommand;
                receivedCommand = null;
                return cmd;
            }
        }

        public void Stop()
        {
            tcpService?.Stop();
            tcpService = null;

            httpService?.Stop();
            httpService = null;

            logAction?.Invoke($"双通道通信 (TCP/HTTP) 已关闭");
        }
    }
}