using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ModelHotSwapWorkflow.Services
{
    public class TcpCommunicationService : IDisposable
    {
        private TcpListener listener;
        private TcpClient client;
        private NetworkStream stream;
        private CancellationTokenSource cts;
        public event Action<string> OnMessageReceived;
        public event Action OnConnected;

        public async Task StartServerAsync(int port)
        {
            Stop();
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            cts = new CancellationTokenSource();
            _ = AcceptClientsAsync(cts.Token);
        }

        private async Task AcceptClientsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var tcpClient = await listener.AcceptTcpClientAsync();
                    client = tcpClient;
                    stream = client.GetStream();
                    OnConnected?.Invoke();
                    _ = ReceiveMessagesAsync(token);
                }
                catch { break; }
            }
        }

        public async Task ConnectAsync(string address, int port)
        {
            Stop();
            client = new TcpClient();
            await client.ConnectAsync(address, port);
            stream = client.GetStream();
            cts = new CancellationTokenSource();
            OnConnected?.Invoke();
            _ = ReceiveMessagesAsync(cts.Token);
        }

        private async Task ReceiveMessagesAsync(CancellationToken token)
        {
            byte[] buffer = new byte[65536];
            while (!token.IsCancellationRequested && stream != null && stream.CanRead)
            {
                try
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                    if (bytesRead > 0)
                    {
                        string msg = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        OnMessageReceived?.Invoke(msg);
                    }
                    else break;
                }
                catch { break; }
            }
        }

        public async Task SendMessageAsync(string message)
        {
            if (stream != null && stream.CanWrite)
            {
                byte[] data = Encoding.UTF8.GetBytes(message);
                await stream.WriteAsync(data, 0, data.Length);
            }
        }

        public void Stop()
        {
            cts?.Cancel();
            stream?.Close();
            client?.Close();
            listener?.Stop();
        }

        public void Dispose() => Stop();
    }
}