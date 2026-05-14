using System;
using System.IO;
using System.Threading.Tasks;
using OpenCvSharp;

namespace ModelHotSwapWorkflow.Models
{
    /// <summary>
    /// 终端视觉渲染节点。支持动态渲染检测结果，并具备坏样本地固化存储功能。
    /// </summary>
    public class DisplayNode : NodeBase
    {
        public override string NodeType => "DisplayNode";
        public override Type InputType => typeof(TensorPayload);
        public override Type OutputType => typeof(TensorPayload);

        // ================= 新增配置参数 =================
        public bool DrawBoundingBox { get; set; } = true;
        public bool DrawLabel { get; set; } = true;
        public string SaveImagePath { get; set; } = "";

        // 专门通知 UI 层刷新图片的事件
        public event Action<Mat>? OnImageProcessed;

        public override async Task<object> Process(object input)
        {
            if (!(input is TensorPayload payload) || payload.BaseTensor == null)
                return input;

            await Task.Run(() =>
            {
                // 出于安全考虑，克隆一张图用于画框显示，避免污染主线张量
                using (Mat renderMat = payload.BaseTensor.Clone())
                {
                    bool hasDefect = false;

                    // 1. 如果配置了渲染，则调用 OpenCV 在图上画框
                    if (payload.RoiResults != null)
                    {
                        foreach (var result in payload.RoiResults)
                        {
                            if (result.Boxes.Count > 0) hasDefect = true; // 只要有框，就标记为不良图

                            if (DrawBoundingBox || DrawLabel)
                            {
                                // 【关键修复】：使用 for 循环，通过索引 i 把坐标、名字和分数对应起来
                                for (int i = 0; i < result.Boxes.Count; i++)
                                {
                                    var box = result.Boxes[i];

                                    if (DrawBoundingBox)
                                    {
                                        Cv2.Rectangle(renderMat, new OpenCvSharp.Rect(box.X, box.Y, box.Width, box.Height), Scalar.Red, 2);
                                    }

                                    if (DrawLabel)
                                    {
                                        // 智能推断类别名称：优先找 ClassNames，其次找 ClassDefinitions，兜底用 ID
                                        string labelName = $"Class_{result.ClassIds[i]}";
                                        if (result.ClassNames != null && result.ClassNames.Count > i)
                                        {
                                            labelName = result.ClassNames[i];
                                        }
                                        else if (result.ClassDefinitions != null)
                                        {
                                            var def = result.ClassDefinitions.FirstOrDefault(d => d.Id == result.ClassIds[i]);
                                            if (def != null) labelName = def.Name;
                                        }

                                        // 获取分数
                                        float score = (result.Scores != null && result.Scores.Count > i) ? result.Scores[i] : 0f;

                                        // 在图上写上黄色的字（类别名 + 分数）
                                        Cv2.PutText(renderMat, $"{labelName} {score:F2}", new OpenCvSharp.Point(box.X, box.Y - 5), HersheyFonts.HersheySimplex, 0.7, Scalar.Yellow, 2);
                                    }
                                }
                            }
                        }
                    }

                    // 2. 自动保存不良品图片 (自动存坏样功能)
                    if (hasDefect && !string.IsNullOrEmpty(SaveImagePath))
                    {
                        try
                        {
                            if (!Directory.Exists(SaveImagePath)) Directory.CreateDirectory(SaveImagePath);
                            string filename = Path.Combine(SaveImagePath, $"Defect_{DateTime.Now:yyyyMMdd_HHmmss_fff}.jpg");
                            Cv2.ImWrite(filename, renderMat);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[DisplayNode] 存图失败: {ex.Message}");
                        }
                    }

                    // 3. 把画好框的图扔给 UI 层显示
                    OnImageProcessed?.Invoke(renderMat);
                }
            });

            return payload; // 原样传给下一个节点
        }
    }
}