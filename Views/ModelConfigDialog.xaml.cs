using System;
using System.IO;
using System.Linq;
using System.Windows;
using ModelHotSwapWorkflow.Models;
using System.Text.Json.Nodes;

namespace ModelHotSwapWorkflow.Views
{
    public partial class ModelConfigDialog : Window
    {
        private ModelNode _node;

        public ModelConfigDialog(ModelNode node)
        {
            InitializeComponent();
            _node = node;

            LoadNodeDataToUI();
        }

        private void LoadNodeDataToUI()
        {
            TxtModelPath.Text = _node.ModelPath;

            // 【恢复您的逻辑】：如果有模型路径，立刻去同目录下找 txt
            if (!string.IsNullOrEmpty(_node.ModelPath))
            {
                TryAutoLoadClasses(_node.ModelPath);
            }

            // 无论有没有 txt，之前保存的文本都要回显上去
            CmbTargetClass.Text = _node.TargetClassId;
            TxtStride.Text = _node.FeatureStride.ToString();

            if (_node.EngineMode == InferenceEngineMode.FeatureSlicing)
            {
                RbSlicing.IsChecked = true;
                GbSlicingConfig.Visibility = Visibility.Visible;
            }
            else
            {
                RbStandard.IsChecked = true;
            }
            CmbSliceRole.SelectedIndex = (int)_node.SliceRole;
        }

       
        

       

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "ONNX模型|*.onnx" };
            if (dlg.ShowDialog() == true)
            {
                TxtModelPath.Text = dlg.FileName;

                // 【恢复您的逻辑】：浏览完新模型，立马去嗅探它的配套 txt 类别文件
                TryAutoLoadClasses(dlg.FileName);
            }
        }

        // 【完整恢复您原本的智能寻找方法】
        private void TryAutoLoadClasses(string onnxFilePath)
        {
            try
            {
                if (string.IsNullOrEmpty(onnxFilePath)) return;

                string directory = Path.GetDirectoryName(onnxFilePath);
                string modelName = Path.GetFileNameWithoutExtension(onnxFilePath);

                string specificClassFile = Path.Combine(directory, modelName + ".txt");
                string generalClassFile = Path.Combine(directory, "classes.txt");

                string targetFileToRead = null;

                if (File.Exists(specificClassFile))
                    targetFileToRead = specificClassFile;
                else if (File.Exists(generalClassFile))
                    targetFileToRead = generalClassFile;

                if (targetFileToRead != null)
                {
                    var classNames = File.ReadAllLines(targetFileToRead)
                                           .Where(line => !string.IsNullOrWhiteSpace(line))
                                           .ToArray();

                    if (classNames.Length > 0)
                    {
                        CmbTargetClass.ItemsSource = classNames;
                        // 如果用户之前没选过，就默认选中第一个
                        if (string.IsNullOrEmpty(CmbTargetClass.Text) && string.IsNullOrEmpty(_node.TargetClassId))
                        {
                            CmbTargetClass.SelectedIndex = 0;
                        }
                    }
                }
                else
                {
                    CmbTargetClass.ItemsSource = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"读取类别名单失败: {ex.Message}");
            }
        }

        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            if (GbSlicingConfig == null) return;
            GbSlicingConfig.Visibility = RbSlicing.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            _node.ModelPath = TxtModelPath.Text;
            _node.TargetClassId = CmbTargetClass.Text.Trim();

            _node.EngineMode = RbSlicing.IsChecked == true ? InferenceEngineMode.FeatureSlicing : InferenceEngineMode.StandardCascade;
            _node.SliceRole = (ModelSliceRole)CmbSliceRole.SelectedIndex;

            if (int.TryParse(TxtStride.Text, out int stride)) _node.FeatureStride = stride;

            _node.ConfigDisplay = $"[{(_node.EngineMode == InferenceEngineMode.StandardCascade ? "模式一" : "模式二")}] {System.IO.Path.GetFileName(_node.ModelPath)}";


            // ====================================================================
            // 【核心修复：将界面选择的模型，真实地写入硬盘的专属配置文件中！】
            // ====================================================================
            try
            {
                string templatePath = "model_config.json"; // 咱们的老模板
                if (File.Exists(templatePath))
                {
                    // 1. 读取老模板
                    string jsonString = File.ReadAllText(templatePath);
                    JsonNode rootNode = JsonNode.Parse(jsonString);

                    // 2. 强行把配置里的模型路径，改成您在界面上选中的新路径！
                    // 【真正的修复点】：它确实是 JsonArray，但键名必须是 "model_path"！
                    if (rootNode is JsonArray array && array.Count > 0)
                    {
                        array[0]["model_path"] = _node.ModelPath; // 严格匹配您 JSON 里的拼写
                    }

                    // 3. 另存为您刚才生成的专属文件
                    File.WriteAllText(_node.ConfigPath, rootNode.ToString());
                }
                else
                {
                    MessageBox.Show($"警告：找不到基础模板 {templatePath}，将无法生成专属配置！", "提示");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"生成节点专属配置文件失败: {ex.Message}", "错误");
            }
            // ====================================================================

            this.DialogResult = true;
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => this.Close();
    }
}