using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ModelHotSwapWorkflow.Models;
using System.Drawing;

namespace ModelHotSwapWorkflow.Services
{
    public class WorkflowEngine
    {
        private readonly Dictionary<string, NodeBase> nodes;
        private readonly List<Connection> connections;
        private readonly Action<string> logAction;

        private bool isTriggerMode = false;
        private Action onTriggeredCallback;
        private TcpCommandNode triggerNode;

        public WorkflowEngine(Dictionary<string, NodeBase> nodes, List<Connection> connections, Action<string> logAction = null)
        {
            this.nodes = nodes;
            this.connections = connections;
            this.logAction = logAction;
        }

        public void StartTriggerMode(Action onTriggered)
        {
            StopTriggerMonitoring();
            triggerNode = nodes.Values.OfType<TcpCommandNode>().FirstOrDefault();
            if (triggerNode == null)
            {
                logAction?.Invoke("触发模式启动失败：画布上没有TCP命令节点");
                return;
            }
            onTriggeredCallback = onTriggered;
            isTriggerMode = true;
            _ = triggerNode.StartAsync();
            triggerNode.MessageReceived += OnTriggerMessageReceived;
            logAction?.Invoke($"触发模式已激活，等待TCP命令...");
        }

        private void OnTriggerMessageReceived(string message)
        {
            if (!isTriggerMode) return;
            logAction?.Invoke($"触发信号: {message}");
            onTriggeredCallback?.Invoke();
        }

        public void StopTriggerMonitoring()
        {
            if (triggerNode != null)
            {
                triggerNode.MessageReceived -= OnTriggerMessageReceived;
                triggerNode = null;
            }
            isTriggerMode = false;
        }

        public async Task ExecuteAsync()
        {
            // ---------- 1. 确定起始节点 ----------
            List<NodeBase> startNodes;
            if (isTriggerMode && triggerNode != null)
            {
                startNodes = new List<NodeBase> { triggerNode };
                startNodes.AddRange(nodes.Values.OfType<ImageSourceNode>());
            }
            else
            {
                startNodes = nodes.Values.Where(n => !connections.Any(c => c.TargetId == n.Id)).ToList();
                if (startNodes.Count == 0) throw new Exception("没有起始节点");
            }

            // ---------- 2. 第一遍：执行所有非模型节点，收集分支决策和图像 ----------
            var dataStore = new Dictionary<string, object>();      // 节点ID -> 输出数据
            var executed = new HashSet<string>();
            var pending = new Queue<string>(startNodes.Select(n => n.Id));
            string selectedTargetId = null;

            while (pending.Count > 0)
            {
                var nodeId = pending.Dequeue();
                if (executed.Contains(nodeId)) continue;
                var node = nodes[nodeId];

                // 跳过模型节点，稍后统一处理
                if (node is ModelNode) continue;

                logAction?.Invoke($"执行节点: {node.Name}");

                // 获取输入（对于有输入连线的节点）
                object input = null;
                var incomingConn = connections.FirstOrDefault(c => c.TargetId == nodeId);
                if (incomingConn != null && dataStore.ContainsKey(incomingConn.SourceId))
                    input = dataStore[incomingConn.SourceId];

                // 执行节点
                object result;
                try
                {
                    result = await node.Process(input);
                }
                catch (Exception ex)
                {
                    logAction?.Invoke($"节点 {node.Name} 执行失败: {ex.Message}");
                    throw;
                }

                dataStore[nodeId] = result;
                executed.Add(nodeId);

                // 特殊处理分支节点：记录选中的目标
                if (node is BranchNode && result is BranchResult branchResult)
                {
                    selectedTargetId = branchResult.TargetNodeId;
                    if (!string.IsNullOrEmpty(selectedTargetId) && nodes.ContainsKey(selectedTargetId))
                        logAction?.Invoke($"分支决策: 激活目标 {nodes[selectedTargetId]?.Name}");
                }

                // 特殊处理图像源：将图像存入全局键
                if (node is ImageSourceNode)
                {
                    dataStore["GlobalImage"] = result;
                }

                // 将下游非模型节点加入队列
                var downstream = connections.Where(c => c.SourceId == nodeId).Select(c => c.TargetId);
                foreach (var downId in downstream)
                {
                    if (!executed.Contains(downId) && nodes[downId] is not ModelNode)
                        pending.Enqueue(downId);
                }
            }

            // ---------- 3. 计算激活路径（从选中的目标开始，包含所有下游模型）----------
            var activatedModelIds = new HashSet<string>();
            if (!string.IsNullOrEmpty(selectedTargetId) && nodes.ContainsKey(selectedTargetId))
            {
                CollectDownstreamModels(selectedTargetId, activatedModelIds);
            }

            // 如果没有分支选中任何模型，但有模型节点，则全部激活（兼容无分支流程）
            if (activatedModelIds.Count == 0)
            {
                foreach (var node in nodes.Values.OfType<ModelNode>())
                    activatedModelIds.Add(node.Id);
            }

            // ---------- 4. 按正确的拓扑顺序执行激活的模型节点 ----------
            // 构建激活模型内部的依赖顺序（根据连线，上游优先）
            var modelOrder = GetCorrectTopologicalOrder(activatedModelIds);
            foreach (var modelId in modelOrder)
            {
                var model = nodes[modelId] as ModelNode;
                logAction?.Invoke($"执行节点: {model.Name}");

                // 确定输入数据
                object input = null;
                var incomingConn = connections.FirstOrDefault(c => c.TargetId == modelId);
                if (incomingConn != null && dataStore.ContainsKey(incomingConn.SourceId))
                {
                    input = dataStore[incomingConn.SourceId];
                }

                // 如果输入无效（比如来自分支节点的 BranchResult），则改用全局图像
                if (!(input is Image) && !(input is DetectionResult))
                {
                    if (dataStore.TryGetValue("GlobalImage", out var globalImg))
                    {
                        input = globalImg;
                        logAction?.Invoke($"模型 [{model.Name}] 使用全局图像作为输入");
                    }
                    else
                    {
                        logAction?.Invoke($"模型 [{model.Name}] 无法获取有效输入，跳过");
                        continue;
                    }
                }

                object result;
                try
                {
                    result = await model.Process(input);
                }
                catch (Exception ex)
                {
                    logAction?.Invoke($"模型 [{model.Name}] 执行失败: {ex.Message}");
                    throw;
                }

                dataStore[modelId] = result;
                executed.Add(modelId);

                // 将输出传递给下游非模型节点（如显示、动作）
                var downstream = connections.Where(c => c.SourceId == modelId).Select(c => c.TargetId);
                foreach (var downId in downstream)
                {
                    if (!executed.Contains(downId) && nodes[downId] is not ModelNode)
                        pending.Enqueue(downId);
                }
            }

            // ---------- 5. 执行剩余的非模型节点（显示、动作等）----------
            while (pending.Count > 0)
            {
                var nodeId = pending.Dequeue();
                if (executed.Contains(nodeId)) continue;
                var node = nodes[nodeId];
                if (node is ModelNode) continue;

                logAction?.Invoke($"执行节点: {node.Name}");
                object input = null;
                var incomingConn = connections.FirstOrDefault(c => c.TargetId == nodeId);
                if (incomingConn != null && dataStore.ContainsKey(incomingConn.SourceId))
                    input = dataStore[incomingConn.SourceId];

                object result;
                try
                {
                    result = await node.Process(input);
                }
                catch (Exception ex)
                {
                    logAction?.Invoke($"节点 {node.Name} 执行失败: {ex.Message}");
                    throw;
                }
                dataStore[nodeId] = result;
                executed.Add(nodeId);

                var downstream = connections.Where(c => c.SourceId == nodeId).Select(c => c.TargetId);
                foreach (var downId in downstream)
                    if (!executed.Contains(downId) && nodes[downId] is not ModelNode)
                        pending.Enqueue(downId);
            }

            logAction?.Invoke("工作流执行完毕");
        }
        // 递归收集下游模型节点
        private void CollectDownstreamModels(string startId, HashSet<string> result)
        {
            if (!nodes.ContainsKey(startId)) return;
            if (nodes[startId] is ModelNode)
                result.Add(startId);
            foreach (var conn in connections.Where(c => c.SourceId == startId))
                CollectDownstreamModels(conn.TargetId, result);
        }

        // 简单拓扑排序（保证上游在前）
        // 正确的拓扑排序：从根节点开始，广度优先遍历，保证上游在前
        private List<string> GetCorrectTopologicalOrder(HashSet<string> modelIds)
        {
            var result = new List<string>();
            var visited = new HashSet<string>();

            // 找出所有激活模型中没有输入连线的模型（根节点）
            var roots = modelIds.Where(id => !connections.Any(c => c.TargetId == id && modelIds.Contains(c.SourceId))).ToList();

            var queue = new Queue<string>(roots);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (visited.Contains(id)) continue;
                visited.Add(id);
                result.Add(id);

                // 将下游激活模型加入队列
                foreach (var conn in connections.Where(c => c.SourceId == id && modelIds.Contains(c.TargetId)))
                {
                    if (!visited.Contains(conn.TargetId))
                        queue.Enqueue(conn.TargetId);
                }
            }

            // 如果有环或未访问的节点，按原顺序补充
            foreach (var id in modelIds)
            {
                if (!visited.Contains(id))
                    result.Add(id);
            }

            return result;
        }

        private void TopologicalVisit(string id, HashSet<string> validIds, HashSet<string> visited, HashSet<string> tempVisited, List<string> result)
        {
            if (tempVisited.Contains(id)) throw new Exception("检测到循环依赖");
            if (visited.Contains(id)) return;

            tempVisited.Add(id);
            foreach (var conn in connections.Where(c => c.SourceId == id))
                if (validIds.Contains(conn.TargetId))
                    TopologicalVisit(conn.TargetId, validIds, visited, tempVisited, result);
            tempVisited.Remove(id);
            visited.Add(id);
            result.Add(id);
        }
    }

    public class Connection
    {
        public string SourceId { get; set; }
        public string TargetId { get; set; }
        public string SourcePin { get; set; } = "default";
        public string TargetPin { get; set; } = "default";
    }
}