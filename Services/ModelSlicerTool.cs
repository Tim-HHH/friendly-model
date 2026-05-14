using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace ModelHotSwapWorkflow.Services
{
    public class ModelSlicerTool
    {
        /// <summary>
        /// 呼叫 Python 脚本执行切片
        /// </summary>
        /// <param name="originalModelPath">原始 ONNX 路径</param>
        /// <param name="modelType">模型类型 (YOLOv8, YOLOv5, Custom)</param>
        /// <param name="customNodeName">如果是 Custom，需要传入具体的节点名</param>
        /// <param name="onLog">用于向界面输出实时日志的回调</param>
        public static async Task<bool> RunSlicerAsync(string originalModelPath, string modelType, string customNodeName, Action<string> onLog)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Python 脚本的相对路径或绝对路径
                    string pythonScriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "model_slicer.py");

                    if (!File.Exists(pythonScriptPath))
                    {
                        onLog?.Invoke("错误：找不到切片脚本 model_slicer.py！");
                        return false;
                    }

                    // 构建给 Python 的命令行参数
                    string args = $"\"{pythonScriptPath}\" --input \"{originalModelPath}\" --type \"{modelType}\"";
                    if (modelType.ToLower() == "custom")
                    {
                        args += $" --node \"{customNodeName}\"";
                    }

                    // 配置进程启动信息
                    // 配置进程启动信息
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        // 【核心修复】：强行指定您那个装了 onnx 库的 Anaconda 虚拟环境
                        FileName = @"D:\APP\Anaconda\envs\yolov11\python.exe",
                        Arguments = args,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using (Process process = new Process { StartInfo = psi })
                    {
                        // 监听 Python 的输出日志
                        process.OutputDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) onLog?.Invoke(e.Data); };
                        process.ErrorDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) onLog?.Invoke("[报错] " + e.Data); };

                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        process.WaitForExit(); // 等待脚本切完

                        return process.ExitCode == 0;
                    }
                }
                catch (Exception ex)
                {
                    onLog?.Invoke($"执行过程发生异常: {ex.Message}");
                    return false;
                }
            });
        }
    }
}