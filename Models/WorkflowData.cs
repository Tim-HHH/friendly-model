using System.Collections.Generic;

namespace ModelHotSwapWorkflow.Models
{
    // 节点配置数据（可序列化部分）
    public class NodeData
    {
        public string Id { get; set; }
        public string NodeType { get; set; }
        public string Name { get; set; }
        public double X { get; set; }
        public double Y { get; set; }

        // 各类型节点的特定配置
        public string ImagePath { get; set; }           // ImageSourceNode
        public string ModelPath { get; set; }           // ModelNode
        public string ModelName { get; set; }           // ModelNode
        public int Port { get; set; }                   // TcpCommandNode
        public string Address { get; set; }             // TcpCommandNode
        public bool IsServer { get; set; }              // TcpCommandNode
        public Dictionary<string, string> ConditionTargetMap { get; set; } // BranchNode: 条件值 -> 目标节点ID
        public string DefaultTargetNodeId { get; set; } // BranchNode
        public string ActionName { get; set; }          // ActionNode
        public string ActionParameter { get; set; }     // ActionNode
    }

    // 连线数据
    public class ConnectionData
    {
        public string SourceId { get; set; }
        public string TargetId { get; set; }
        public string SourcePin { get; set; }
        public string TargetPin { get; set; }
    }

    // 完整工作流数据
    public class WorkflowData
    {
        public List<NodeData> Nodes { get; set; } = new List<NodeData>();
        public List<ConnectionData> Connections { get; set; } = new List<ConnectionData>();
    }
}