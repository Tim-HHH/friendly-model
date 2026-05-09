using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HMManager;

namespace ModelHotSwapWorkflow.Models
{
    /// <summary>
    /// 分支节点输出结果封装载荷
    /// </summary>
    public class BranchResult
    {
        public string TargetNodeId { get; set; }
        public object Data { get; set; }
    }

    /// <summary>
    /// 逻辑分支节点：根据上游推理结果进行路由分发
    /// </summary>
    public class BranchNode : NodeBase
    {
        public override string NodeType => "Branch";
        public override Type InputType => typeof(DetectionResultCollection);
        public override Type OutputType => typeof(BranchResult);

        // 路由映射表 (UI配置的条件将保存在这里)
        public Dictionary<string, string> ConditionTargetMap { get; set; } = new Dictionary<string, string>();
        public string DefaultTargetNodeId { get; set; }

        // 置信度及格线
        public double Threshold { get; set; } = 0.5;

        public override async Task<object> Process(object input)
        {
            var results = input as DetectionResultCollection;

            // 【关键修改1】：默认判定为 "0" (代表失败/未检测到目标)
            string decision = "0";

            if (results != null && results.DetectionCount > 0)
            {
                var allScores = results.SelectMany(r => r.Scores).ToList();
                if (allScores.Count > 0)
                {
                    float maxConfidence = allScores.Max();
                    if (maxConfidence >= Threshold)
                    {
                        // 【关键修改2】：置信度达标，判定为 "1" (代表成功/检测到有效目标)
                        decision = "1";
                    }
                }
            }

            // 根据决策去字典里找对应的下级节点 ID
            string targetId = DefaultTargetNodeId;
            if (ConditionTargetMap.TryGetValue(decision, out string mappedTargetId))
            {
                targetId = mappedTargetId;
            }

            // 【关键修改3】：贴心的报错提示。如果没找到路线，引擎会在控制台大声报警！
            if (string.IsNullOrEmpty(targetId))
            {
                throw new Exception($"无法路由：当前检测结果判定为 [{decision}]，但您在分支工具中没有为 [{decision}] 连线或配置目标！");
            }

            // 返回包装好的路由结果，交给引擎驱动下游
            return new BranchResult
            {
                TargetNodeId = targetId,
                Data = results
            };
        }
    }
}