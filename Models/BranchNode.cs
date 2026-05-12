using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HMManager;

namespace ModelHotSwapWorkflow.Models
{
    /// <summary>
    /// 分支节点输出结果封装载荷。
    /// </summary>
    public class BranchResult
    {
        /// <summary>获取或设置决策后的目标下游节点标识。</summary>
        public string TargetNodeId { get; set; }
        /// <summary>获取或设置向下游透传的数据载体。</summary>
        public object Data { get; set; }
    }

    /// <summary>
    /// 工业级多策略分支节点。
    /// 适配 TensorPayload 传输协议，支持“安检门（存在性校验）”与“终极裁判（结果分拣）”双重逻辑。
    /// </summary>
    public class BranchNode : NodeBase
    {
        public override string NodeType => "Branch";

        // 【关键升级】：输入输出全面适配张量包裹协议
        public override Type InputType => typeof(TensorPayload);
        public override Type OutputType => typeof(BranchResult);

        /// <summary>路由映射表：决策字符串 (如 "OK", "NG") -> 目标节点 ID。</summary>
        public Dictionary<string, string> ConditionTargetMap { get; set; } = new Dictionary<string, string>();

        /// <summary>默认流向节点 ID（兜底路径）。</summary>
        public string DefaultTargetNodeId { get; set; }

        /// <summary>置信度判定阈值，默认为 0.5。</summary>
        public double Threshold { get; set; } = 0.5;

        /// <summary>
        /// 执行核心分支逻辑。
        /// 实现思路：
        /// 1. 拆解 TensorPayload 包裹。
        /// 2. 扫描 RoiResults 结果集进行多准则判定。
        /// 3. 根据判定生成的 Decision 字符串执行路由映射。
        /// </summary>
        public override async Task<object> Process(object input)
        {
            var payload = input as TensorPayload;

            // 预设决策为 "NG" (未通过)
            string decision = "NG";

            // ---------------------------------------------------------
            // 核心判定逻辑（兼顾“安检门”与“裁判员”职责）
            // ---------------------------------------------------------
            if (payload != null && payload.RoiResults != null && payload.RoiResults.DetectionCount > 0)
            {
                // 提取所有检测项的置信度，评估是否存在达标的识别目标
                var allScores = payload.RoiResults.SelectMany(r => r.Scores).ToList();

                if (allScores.Count > 0 && allScores.Max() >= Threshold)
                {
                    // 若存在达标结果，初步判定为 OK。
                    // 此处可扩展逻辑：例如遍历 ClassNames，若包含 "Defect" 字样，则强制修正为 "NG"。
                    decision = "OK";
                }
            }

            // ---------------------------------------------------------
            // 路由寻址逻辑
            // ---------------------------------------------------------
            string targetId = DefaultTargetNodeId;

            // 优先根据决策结果从映射表中匹配目标路径
            if (ConditionTargetMap.TryGetValue(decision, out string mappedTargetId))
            {
                targetId = mappedTargetId;
            }

            // 容错处理：若无匹配路径则抛出异常
            if (string.IsNullOrEmpty(targetId))
            {
                throw new Exception($"[分支拦截] 决策结果为 [{decision}]，但未配置对应的下游流向，请检查配置界面。");
            }

            return new BranchResult
            {
                TargetNodeId = targetId,
                Data = payload // 将原始张量包裹原封不动透传给下游，实现零拷贝
            };
        }
    }
}