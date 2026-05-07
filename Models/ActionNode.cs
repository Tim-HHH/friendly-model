using System;
using System.Threading.Tasks;

namespace ModelHotSwapWorkflow.Models
{
    public class ActionNode : NodeBase
    {
        public string ActionName { get; set; }
        public string ActionParameter { get; set; }

        public override string NodeType => "Action";
        public override Type InputType => typeof(object);
        public override Type OutputType => null;

        public override Task<object> Process(object input)
        {
            // 在这里执行自定义动作，例如写日志、调用API等
            string logMsg = $"执行动作: {ActionName}, 参数: {ActionParameter}, 输入值: {input}";
            // 可以通过事件让主窗口显示日志
            OnActionExecuted?.Invoke(logMsg);
            return Task.FromResult<object>(null);
        }

        public event Action<string> OnActionExecuted;
    }
}