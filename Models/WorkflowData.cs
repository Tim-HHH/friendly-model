using System.Collections.Generic;

namespace ModelHotSwapWorkflow.Models
{
    /// <summary>
    /// 表示工作流中两个节点之间的连接关系（运行时对象）
    /// </summary>
    public class Connection
    {
        /// <summary>
        /// 源节点（输出端）的唯一标识符
        /// </summary>
        public string SourceId { get; set; }

        /// <summary>
        /// 目标节点（输入端）的唯一标识符
        /// </summary>
        public string TargetId { get; set; }

        /// <summary>
        /// 源节点的引脚方向（例如：Right, Bottom）
        /// </summary>
        public string SourcePin { get; set; }

        /// <summary>
        /// 目标节点的引脚方向（例如：Left, Top）
        /// </summary>
        public string TargetPin { get; set; }
    }

    /// <summary>
    /// 工作流节点的基础序列化数据模型
    /// </summary>
    public class NodeData
    {
        public string Id { get; set; }
        public string NodeType { get; set; }
        public string Name { get; set; }
        public double X { get; set; }
        public double Y { get; set; }

        // 各类型节点的特定配置参数
        public string ImagePath { get; set; }
        public string ModelPath { get; set; }
        public string ModelName { get; set; }
        public int Port { get; set; }
        public string Address { get; set; }
        public bool IsServer { get; set; }
        public Dictionary<string, string> ConditionTargetMap { get; set; }
        public string DefaultTargetNodeId { get; set; }
        public string ActionName { get; set; }
        public string ActionParameter { get; set; }
    }

    /// <summary>
    /// 连线关系的序列化数据模型（用于本地文件存储）
    /// </summary>
    public class ConnectionData
    {
        public string SourceId { get; set; }
        public string TargetId { get; set; }
        public string SourcePin { get; set; }
        public string TargetPin { get; set; }
    }

    /// <summary>
    /// 完整工作流配置文件的序列化数据容器
    /// </summary>
    public class WorkflowData
    {
        /// <summary>
        /// 节点数据集合
        /// </summary>
        public List<NodeData> Nodes { get; set; } = new List<NodeData>();

        /// <summary>
        /// 连线数据集合
        /// </summary>
        public List<ConnectionData> Connections { get; set; } = new List<ConnectionData>();
    }
}