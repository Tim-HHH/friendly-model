using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace ModelHotSwapWorkflow.Services
{
    /// <summary>
    /// HTTP 通信服务：负责监听上位机的 POST 请求。
    /// 解析 HTTP 头部的指令字符串（X-Command）与请求体中的图像二进制流。
    /// </summary>
    public class HttpCommunicationService : IDisposable
    {
        private HttpListener _listener;
        private CancellationTokenSource _cts;

        /// <summary>
        /// 当接收到合法的 HTTP 触发请求时触发。
        /// 参数1：指令字符串 (如 "T0")；参数2：图像字节数组
        /// </summary>
        public event Action<string, byte[]> OnHttpPostReceived;
        public event Action<string> OnLog;

        public void StartServer(int port)
        {
            Stop();
            _listener = new HttpListener();
            // 绑定本地所有 IP 的指定端口
            _listener.Prefixes.Add($"http://+:{port}/api/trigger/");
            _listener.Start();
            _cts = new CancellationTokenSource();

            _ = AcceptRequestsAsync(_cts.Token);
            OnLog?.Invoke($"HTTP 接口已启动: http://127.0.0.1:{port}/api/trigger/");
        }

        private async Task AcceptRequestsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => ProcessRequest(context), token);
                }
                catch { break; }
            }
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                if (request.HttpMethod == "POST")
                {
                    // 1. 从 HTTP 头部获取上位机发送的字符串指令 (例如: X-Command: T0)
                    string command = request.Headers["X-Command"] ?? "UNKNOWN";

                    // 2. 从 HTTP 请求体 (Body) 获取图像的原始字节流
                    using (var ms = new MemoryStream())
                    {
                        request.InputStream.CopyTo(ms);
                        byte[] imageData = ms.ToArray();

                        if (imageData.Length > 0)
                        {
                            OnLog?.Invoke($"HTTP 收到指令 [{command}], 包含图像数据: {imageData.Length} bytes");
                            // 向上层触发事件，交出图片和指令
                            OnHttpPostReceived?.Invoke(command, imageData);

                            // 向上位机回复成功状态
                            SendResponse(response, 200, "{\"status\":\"OK\",\"message\":\"触发成功\"}");
                        }
                        else
                        {
                            SendResponse(response, 400, "{\"status\":\"Error\",\"message\":\"图像数据为空\"}");
                        }
                    }
                }
                else
                {
                    SendResponse(response, 405, "{\"status\":\"Error\",\"message\":\"仅支持 POST 方法\"}");
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"HTTP 处理异常: {ex.Message}");
            }
        }

        private void SendResponse(HttpListenerResponse response, int statusCode, string jsonMessage)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json";
            response.ContentEncoding = System.Text.Encoding.UTF8;
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(jsonMessage);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        public void Stop()
        {
            _cts?.Cancel();
            if (_listener != null && _listener.IsListening)
            {
                _listener.Stop();
                _listener.Close();
            }
        }

        public void Dispose() => Stop();
    }
}