using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using HMManager;

namespace ModelHotSwapWorkflow.Models
{
    public class ModelNode : NodeBase
    {
        private ModelManager _modelManager;

        // UI 和导入导出需要的属性（补回来了！）
        public string ModelPath { get; set; }
        public string ModelName { get; set; }
        public List<string> AvailableDataSources { get; set; } = new List<string>();

        // 新引擎配置
        public string ConfigPath { get; set; } = "model_config.json";

        public override string NodeType => "ModelNode"; // 必须是 "ModelNode" 匹配你的 MainWindow
        public override Type InputType => typeof(Bitmap);
        public override Type OutputType => typeof(DetectionResultCollection);

        public void InitializeEngine()
        {
            if (_modelManager == null)
            {
                if (!File.Exists(ConfigPath))
                {
                    throw new FileNotFoundException($"找不到配置文件: {ConfigPath}");
                }
                _modelManager = new ModelManager(ConfigPath);
            }
        }

        public override async Task<object> Process(object input)
        {
            InitializeEngine();

            Bitmap inputImage = null;
            if (input is Bitmap bmp) inputImage = bmp;
            else if (input is string imagePath && File.Exists(imagePath)) inputImage = new Bitmap(imagePath);
            else throw new ArgumentException("ModelNode 输入无效");

            DetectionResultCollection results = null;

            await Task.Run(() =>
            {
                results = _modelManager.Run(inputImage);
            });

            return results;
        }

        public void StopAndDispose()
        {
            _modelManager?.Dispose();
            _modelManager = null;
        }
    }
}