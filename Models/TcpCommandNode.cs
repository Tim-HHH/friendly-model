using System;
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

        // 新增：手动模式下的预设命令值
        public string ManualCommand { get; set; } = "1";

        public event Action<string> MessageReceived;

        private TcpCommunicationService tcpService;
        private string receivedCommand;
        private readonly object lockObj = new object();
        private readonly Action<string> logAction;

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
            tcpService = new TcpCommunicationService();
            tcpService.OnConnected += () =>
            {
                if (IsServer)
                    logAction?.Invoke($"TCP 服务端已建立连接 (端口 {Port})");
                else
                    logAction?.Invoke($"TCP 客户端已连接到 {Address}:{Port}");
            };
            tcpService.OnMessageReceived += OnTcpMessage;

            if (IsServer)
            {
                await tcpService.StartServerAsync(Port);
                logAction?.Invoke($"TCP 服务端已启动，监听端口 {Port}");
            }
            else
            {
                await tcpService.ConnectAsync(Address, Port);
                logAction?.Invoke($"TCP 客户端正在连接 {Address}:{Port} ...");
            }
        }

        private void OnTcpMessage(string message)
        {
            logAction?.Invoke($"TCP 收到消息: {message}");
            MessageReceived?.Invoke(message);
            lock (lockObj)
            {
                receivedCommand = message.Trim();
            }
        }

        public override async Task<object> Process(object input)
        {
            if (IsManualMode)
            {
                // 手动模式：直接返回预设命令值（无需等待）
                string cmd = ManualCommand ?? "1";
                logAction?.Invoke($"TCP 手动模式，使用预设命令: {cmd}");
                return cmd;
            }
            else
            {
                // 触发模式：等待新消息（超时 10 秒）
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
        }

        public void Stop()
        {
            tcpService?.Stop();
            tcpService = null;
            logAction?.Invoke($"TCP 连接已关闭");
        }
    }
}