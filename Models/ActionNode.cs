using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ModelHotSwapWorkflow.Models
{
    public enum ActionTargetType { PrintLog, ExportCsv }

    /// <summary>
    /// 多模态 IO 动作触发中心。支持控制台输出与生产数据本地 CSV 固化。
    /// </summary>
    public class ActionNode : NodeBase
    {
        public override string NodeType => "ActionNode";
        public override Type InputType => typeof(TensorPayload);
        public override Type OutputType => typeof(TensorPayload);

        // 新增参数
        public ActionTargetType ActionType { get; set; } = ActionTargetType.PrintLog;
        public string CustomMessage { get; set; } = "检测完成";
        public string ExportCsvPath { get; set; } = "C:\\ProductionData\\";

        // 【关键修复】：把这根“通讯电缆”接回来，让主界面能收到它的日志！
        public event Action<string>? OnActionExecuted;

        public override async Task<object> Process(object input)
        {
            if (!(input is TensorPayload payload)) return input;

            int defectCount = 0;
            if (payload.RoiResults != null)
            {
                foreach (var res in payload.RoiResults) defectCount += res.Boxes.Count;
            }

            string resultStr = defectCount > 0 ? $"NG (发现 {defectCount} 处缺陷)" : "OK";

            if (ActionType == ActionTargetType.PrintLog)
            {
                string logMsg = $"[Action日志] 消息:{CustomMessage} | 结果:{resultStr}";

                // 1. 打印到后台调试器
                System.Diagnostics.Debug.WriteLine(logMsg);
                // 2. 【关键修复】：通过通讯电缆，把日志发送给 UI 界面的黑色文本框显示
                OnActionExecuted?.Invoke(logMsg);
            }
            else if (ActionType == ActionTargetType.ExportCsv)
            {
                await Task.Run(() =>
                {
                    try
                    {
                        if (!Directory.Exists(ExportCsvPath)) Directory.CreateDirectory(ExportCsvPath);
                        string file = Path.Combine(ExportCsvPath, $"Report_{DateTime.Now:yyyyMMdd}.csv");

                        bool writeHeader = !File.Exists(file);
                        using (StreamWriter sw = new StreamWriter(file, true, Encoding.UTF8))
                        {
                            if (writeHeader) sw.WriteLine("Time,Message,Result,DefectCount");
                            sw.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{CustomMessage},{resultStr},{defectCount}");
                        }

                        // 存完文件后，也顺便给界面报个平安
                        OnActionExecuted?.Invoke($"[Action导出] 已成功保存 1 条记录到 CSV。");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"CSV导出失败: {ex.Message}");
                        OnActionExecuted?.Invoke($"[Action错误] CSV导出失败: {ex.Message}");
                    }
                });
            }

            return input;
        }
    }
}