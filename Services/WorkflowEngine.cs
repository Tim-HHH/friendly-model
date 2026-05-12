using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ModelHotSwapWorkflow.Models;

namespace ModelHotSwapWorkflow.Services
{
    /// <summary>
    /// 工作流核心调度引擎
    /// 采用深度优先 (DFS) 异步递归算法，驱动整个计算图的正确流转
    /// </summary>
    public class WorkflowEngine
    {
        private readonly Dictionary<string, NodeBase> nodes;
        private readonly List<Connection> connections;
        private readonly Action<string> logAction;

        public WorkflowEngine(Dictionary<string, NodeBase> nodes, List<Connection> connections, Action<string> logAction = null)
        {
            this.nodes = nodes;
            this.connections = connections;
            this.logAction = logAction;
        }

        public async Task ExecuteAsync()
        {
            logAction?.Invoke("引擎启动：开始解析工作流拓扑...");
            var startNodes = nodes.Values.Where(n => n.NodeType == "ImageSource").ToList();

            if (startNodes.Count == 0)
            {
                logAction?.Invoke("警告：当前工作流中未检测到图像源节点，执行中止。");
                return;
            }

            foreach (var startNode in startNodes)
            {
                await ExecuteSourcePathAsync(startNode.Id);
            }
        }

        public async Task ExecuteSourcePathAsync(string sourceNodeId)
        {
            if (!nodes.ContainsKey(sourceNodeId)) return;

            // 开启独立线程，支持并发无阻塞执行
            _ = Task.Run(async () =>
            {
                try
                {
                    logAction?.Invoke($"[线程分配] 路径触发源: {nodes[sourceNodeId].Name}");

                    // 【核心修复】：直接从源节点开始执行其 Process 方法
                    await ExecuteNodeAsync(sourceNodeId, null);
                }
                catch (Exception ex)
                {
                    logAction?.Invoke($"执行路径异常 ({sourceNodeId}): {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 核心执行器：精确处理每一个节点，并决定下一步去哪
        /// </summary>
        private async Task ExecuteNodeAsync(string nodeId, object inputData)
        {
            // 如果节点不存在，立刻停止
            if (!nodes.TryGetValue(nodeId, out var currentNode)) return;

            // ==========================================
            // 【核心新增】：休眠拦截与数据透传逻辑
            // ==========================================
            if (!currentNode.IsEnabled)
            {
                // 如果不是起点，就打印一句透传日志
                if (currentNode.NodeType != "ImageSource")
                {
                    logAction?.Invoke($"节点处于休眠状态，直接透传数据: {currentNode.Name}");
                }

                // 工人不在工位，数据直接跳过当前节点，送给所有下游节点
                var bypassDownstreams = connections.Where(c => c.SourceId == nodeId).ToList();
                foreach (var conn in bypassDownstreams)
                {
                    await ExecuteNodeAsync(conn.TargetId, inputData);
                }
                return; // 提前结束当前节点的处理，绝不执行重度计算！
            }
            // ==========================================


            // 打印日志 (过滤掉图像源，因为前面已经打印过了，保持日志清爽)
            if (currentNode.NodeType != "ImageSource")
            {
                logAction?.Invoke($"执行节点运算: {currentNode.Name}");
            }

            // 1. 【执行当前节点】
            object result = await currentNode.Process(inputData);

            // 2. 【寻找并驱动下一步】
            if (currentNode is BranchNode && result is BranchResult br)
            {
                // 如果是分支节点：精准拦截！只走向条件匹配的唯一目标节点
                if (!string.IsNullOrEmpty(br.TargetNodeId))
                {
                    // 明确指定目标，并立刻执行目标节点！(修复了跳过执行的漏洞)
                    await ExecuteNodeAsync(br.TargetNodeId, br.Data);
                }
            }
            else
            {
                // 常规节点：顺着所有连线往下游执行
                var downstreams = connections.Where(c => c.SourceId == nodeId).ToList();
                foreach (var conn in downstreams)
                {
                    await ExecuteNodeAsync(conn.TargetId, result);
                }
            }
        }
    }
}