using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ModelHotSwapWorkflow.Models
{
    // 动作类型：打印日志、导出CSV、高速HTTP推送
    public enum ActionTargetType { PrintLog, ExportCsv, HttpPush }

    /// <summary>
    /// 多模态 IO 动作触发中心。支持控制台输出、生产数据本地 CSV 固化以及极速 JSON 网络推送。
    /// </summary>
    public class ActionNode : NodeBase
    {
        public override string NodeType => "ActionNode";
        public override Type InputType => typeof(TensorPayload);
        public override Type OutputType => typeof(TensorPayload);

        // ================= 配置参数 =================
        public ActionTargetType ActionType { get; set; } = ActionTargetType.PrintLog;
        public string CustomMessage { get; set; } = "检测完成";

        // 存CSV专用参数
        public string ExportCsvPath { get; set; } = "C:\\ProductionData\\";

        // HTTP 推送专用参数
        public string PushUrl { get; set; } = "http://127.0.0.1:8081/api/result/";

        // 专用于通知 UI 界面更新黑色文本框日志的“通信电缆”
        public event Action<string>? OnActionExecuted;

        // ================= 核心执行逻辑 =================
        public override async Task<object> Process(object input)
        {
            if (!(input is TensorPayload payload)) return input;

            // 1. 统计当前图片中所有的缺陷总数
            int defectCount = 0;
            if (payload.RoiResults != null)
            {
                foreach (var res in payload.RoiResults) defectCount += res.Boxes.Count;
            }

            // 确定全局状态
            string resultStr = defectCount > 0 ? "NG" : "OK";

            // 2. 根据用户配置的动作类型，执行对应操作
            if (ActionType == ActionTargetType.PrintLog)
            {
                string logMsg = $"[Action日志] 消息:{CustomMessage} | 结果:{resultStr}";

                System.Diagnostics.Debug.WriteLine(logMsg);
                OnActionExecuted?.Invoke(logMsg); // 发送给主界面
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
                            sw.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff},{CustomMessage},{resultStr},{defectCount}");
                        }

                        OnActionExecuted?.Invoke($"[Action导出] 已成功保存 1 条记录到 CSV。");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"CSV导出失败: {ex.Message}");
                        OnActionExecuted?.Invoke($"[Action错误] CSV导出失败: {ex.Message}");
                    }
                });
            }
            else if (ActionType == ActionTargetType.HttpPush)
            {
                await Task.Run(async () =>
                {
                    try
                    {
                        // A. 提取出所有的缺陷坐标和类别信息
                        var defectList = new System.Collections.Generic.List<object>();
                        if (payload.RoiResults != null)
                        {
                            foreach (var res in payload.RoiResults)
                            {
                                for (int i = 0; i < res.Boxes.Count; i++)
                                {
                                    var box = res.Boxes[i];

                                    // 智能提取类别名
                                    string labelName = $"Class_{res.ClassIds[i]}";
                                    if (res.ClassNames != null && res.ClassNames.Count > i)
                                    {
                                        labelName = res.ClassNames[i];
                                    }
                                    else if (res.ClassDefinitions != null)
                                    {
                                        var def = res.ClassDefinitions.FirstOrDefault(d => d.Id == res.ClassIds[i]);
                                        if (def != null) labelName = def.Name;
                                    }

                                    float score = (res.Scores != null && res.Scores.Count > i) ? res.Scores[i] : 0f;

                                    // 将单个缺陷坐标打包
                                    defectList.Add(new
                                    {
                                        Label = labelName,
                                        Score = score,
                                        X = box.X,
                                        Y = box.Y,
                                        Width = box.Width,
                                        Height = box.Height
                                    });
                                }
                            }
                        }

                        // B. 组装极简的 JSON 报文
                        var data = new
                        {
                            Message = CustomMessage,
                            Status = resultStr,
                            DefectsCount = defectCount,
                            DefectDetails = defectList // 包含所有框的具体坐标
                        };
                        string jsonBody = System.Text.Json.JsonSerializer.Serialize(data);

                        // C. 高速网络推送
                        using (var client = new System.Net.Http.HttpClient())
                        {
                            var content = new System.Net.Http.StringContent(jsonBody, Encoding.UTF8, "application/json");
                            await client.PostAsync(PushUrl, content);
                            OnActionExecuted?.Invoke($"[极速推送] JSON 坐标数据已发往上位机");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"HTTP极速推送失败: {ex.Message}");
                        OnActionExecuted?.Invoke($"[Action错误] 推送至上位机失败: {ex.Message}");
                    }
                });
            }

            return input; // 将张量透传给下一个可能的节点
        }
    }
}