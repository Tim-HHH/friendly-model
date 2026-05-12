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

        // ==========================================
        // 【新增】：节点休眠功能的基础属性与方法
        // ==========================================

        /// <summary>
        /// 获取或设置节点是否处于启用（非休眠）状态。默认为 true（工作状态）。
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 触发节点状态切换逻辑。
        /// 核心模型节点（ModelNode）可重写此方法，在休眠时调用 Dispose() 释放显存，在唤醒时重新加载 ONNX 模型。
        /// </summary>
        public virtual void ToggleEnableState()
        {
            IsEnabled = !IsEnabled;
        }

        // ==========================================

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