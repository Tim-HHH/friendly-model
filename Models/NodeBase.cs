using System;
using System.Threading.Tasks;

namespace ModelHotSwapWorkflow.Models
{
    public abstract class NodeBase
    {
        /// <summary>
        /// 节点唯一标识符，支持读写以便导入导出时保持原ID
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 节点显示名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 节点在画布上的 X 坐标
        /// </summary>
        public double X { get; set; }

        /// <summary>
        /// 节点在画布上的 Y 坐标
        /// </summary>
        public double Y { get; set; }

        /// <summary>
        /// 配置信息的简短显示文本
        /// </summary>
        public string ConfigDisplay { get; set; } = "未配置";

        /// <summary>
        /// 节点类型名称（用于序列化和显示）
        /// </summary>
        public abstract string NodeType { get; }

        /// <summary>
        /// 节点接受的输入数据类型（null 表示不需要输入）
        /// </summary>
        public abstract Type InputType { get; }

        /// <summary>
        /// 节点产生的输出数据类型（null 表示无输出）
        /// </summary>
        public abstract Type OutputType { get; }

        /// <summary>
        /// 执行节点的核心逻辑
        /// </summary>
        /// <param name="input">上游节点传入的数据</param>
        /// <returns>处理结果，可传递给下游节点</returns>
        public abstract Task<object> Process(object input);
    }
}