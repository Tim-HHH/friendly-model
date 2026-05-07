using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HMManager;

namespace ModelHotSwapWorkflow.Models
{
    // 如果你之前的代码里已经有了 BranchResult 类的定义，请把下面这几行删掉。
    // 如果没有，就保留它，这是 WorkflowEngine.cs 需要的。
    public class BranchResult
    {
        public string TargetNodeId { get; set; }
        public object Data { get; set; }
    }

    public class BranchNode : NodeBase
    {
        public override string NodeType => "Branch";
        public override Type InputType => typeof(DetectionResultCollection);
        public override Type OutputType => typeof(BranchResult); // 匹配你的 WorkflowEngine

        // 恢复 UI 需要的路由映射属性
        public Dictionary<string, string> ConditionTargetMap { get; set; } = new Dictionary<string, string>();
        public string DefaultTargetNodeId { get; set; }

        public double Threshold { get; set; } = 0.5;

        public override async Task<object> Process(object input)
        {
            var results = input as DetectionResultCollection;
            string decision = "FailRoute"; // 默认判定为失败

            if (results != null && results.DetectionCount > 0)
            {
                var allScores = results.SelectMany(r => r.Scores).ToList();
                if (allScores.Count > 0)
                {
                    float maxConfidence = allScores.Max();
                    if (maxConfidence >= Threshold)
                    {
                        decision = "PassRoute"; // 置信度达标
                    }
                }
            }

            // 根据决策去 Map 里找对应的目标节点 ID
            string targetId = DefaultTargetNodeId;
            if (ConditionTargetMap.TryGetValue(decision, out string mappedTargetId))
            {
                targetId = mappedTargetId;
            }

            // 返回包装好的 BranchResult 给 WorkflowEngine
            return new BranchResult
            {
                TargetNodeId = targetId,
                Data = results
            };
        }
    }
}