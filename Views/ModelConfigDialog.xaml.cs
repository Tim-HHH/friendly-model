using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ModelHotSwapWorkflow.Models;

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

        /// <summary>
        /// 将底层节点数据回显到界面（双向绑定初始化）
        /// </summary>
        /// <summary>
        /// 将底层节点数据回显到界面（双向绑定初始化）
        /// </summary>
        private void LoadNodeDataToUI()
        {
            // 1. 回显模型路径
            TxtModelPath.Text = _node.ModelPath;

            // 2. 【核心修复】：如果已经有模型路径了，窗口一打开就自动加载类别名单
            if (!string.IsNullOrEmpty(_node.ModelPath))
            {
                TryAutoLoadClasses(_node.ModelPath);
            }

            // 3. 回显之前保存的专注类别（必须在加载名单之后赋值，否则名单还没出来，文字可能显示不出来）
            CmbTargetClass.Text = _node.TargetClassId;

            // 4. 回显步长
            TxtStride.Text = _node.FeatureStride.ToString();

            // 5. 回显引擎模式
            if (_node.EngineMode == InferenceEngineMode.StandardCascade)
            {
                RbModeStandard.IsChecked = true;
            }
            else
            {
                RbModeSlicing.IsChecked = true;
            }

            // 6. 回显切片角色
            CmbSliceRole.SelectedIndex = _node.SliceRole == ModelSliceRole.BackboneExtractor ? 0 : 1;
        }

        /// <summary>
        /// 引擎架构切换事件：控制高级面板的显示与隐藏，展现动态交互专利点
        /// </summary>
        private void EngineMode_Changed(object sender, RoutedEventArgs e)
        {
            if (BrdAdvancedSlicing == null) return;

            if (RbModeSlicing.IsChecked == true)
            {
                BrdAdvancedSlicing.Visibility = Visibility.Visible;
            }
            else
            {
                BrdAdvancedSlicing.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 切片角色切换事件：只有选择“检测头(Head)”时，才需要配置坐标映射步长
        /// </summary>
        private void SliceRole_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PnlStride == null) return;

            if (CmbSliceRole.SelectedIndex == 1) // 选中了 Head
            {
                PnlStride.Visibility = Visibility.Visible;
            }
            else
            {
                PnlStride.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog
            {
                Filter = "ONNX 模型文件 (*.onnx)|*.onnx|所有文件 (*.*)|*.*",
                Title = "选择深度学习推理模型"
            };

            if (dlg.ShowDialog() == true)
            {
                TxtModelPath.Text = dlg.FileName;

                // 【核心新增】：触发智能类别嗅探
                TryAutoLoadClasses(dlg.FileName);
            }
        }

        /// <summary>
        /// 智能类别嗅探器：当用户选中 .onnx 模型时，尝试在同目录下寻找配套的类别清单。
        /// 比如模型叫 "yolov8_defect.onnx"，它会尝试寻找 "yolov8_defect.txt" 或通用的 "classes.txt"。
        /// </summary>
        private void TryAutoLoadClasses(string onnxFilePath)
        {
            try
            {
                string directory = System.IO.Path.GetDirectoryName(onnxFilePath);
                string modelName = System.IO.Path.GetFileNameWithoutExtension(onnxFilePath);

                // 猜测1：找同名的 txt 文件 (例如: defect_model.txt)
                string specificClassFile = System.IO.Path.Combine(directory, modelName + ".txt");
                // 猜测2：找通用的 classes.txt
                string generalClassFile = System.IO.Path.Combine(directory, "classes.txt");

                string targetFileToRead = null;

                if (System.IO.File.Exists(specificClassFile))
                    targetFileToRead = specificClassFile;
                else if (System.IO.File.Exists(generalClassFile))
                    targetFileToRead = generalClassFile;

                // 如果找到了名单文件
                if (targetFileToRead != null)
                {
                    // 逐行读取文件内容（假设每行是一个类别名字，比如第一行是"巴片"，第二行是"极柱"）
                    var classNames = System.IO.File.ReadAllLines(targetFileToRead)
                                           .Where(line => !string.IsNullOrWhiteSpace(line))
                                           .ToArray();

                    if (classNames.Length > 0)
                    {
                        // 把名字全部塞进下拉框！
                        CmbTargetClass.ItemsSource = classNames;
                        // 默认选中第一个给用户看
                        CmbTargetClass.SelectedIndex = 0;
                    }
                }
                else
                {
                    // 没找到文件的话，清空下拉列表，留给用户手动输入
                    CmbTargetClass.ItemsSource = null;
                    CmbTargetClass.Text = "";
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"读取类别名单失败: {ex.Message}");
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            // 1. 保存基础参数
            _node.ModelPath = TxtModelPath.Text;
            _node.ModelName = Path.GetFileNameWithoutExtension(_node.ModelPath);
            _node.TargetClassId = CmbTargetClass.Text.Trim();

            // 2. 保存引擎模式与高级参数
            if (RbModeStandard.IsChecked == true)
            {
                _node.EngineMode = InferenceEngineMode.StandardCascade;
                _node.SliceRole = ModelSliceRole.BackboneExtractor; // 默认
            }
            else
            {
                _node.EngineMode = InferenceEngineMode.FeatureSlicing;
                _node.SliceRole = CmbSliceRole.SelectedIndex == 0 ? ModelSliceRole.BackboneExtractor : ModelSliceRole.DetectionHead;

                if (int.TryParse(TxtStride.Text, out int stride))
                {
                    _node.FeatureStride = stride;
                }
            }

            // 3. 【重点】更新节点在画布上的专业显示文字，方便写专利截图
            string shortName = string.IsNullOrEmpty(_node.ModelName) ? "未选择模型" : _node.ModelName;
            string classInfo = string.IsNullOrEmpty(_node.TargetClassId) ? "全盘接收" : $"类:{_node.TargetClassId}";

            if (_node.EngineMode == InferenceEngineMode.StandardCascade)
            {
                _node.ConfigDisplay = $"[标准] {shortName} | {classInfo}";
            }
            else
            {
                string roleTag = _node.SliceRole == ModelSliceRole.BackboneExtractor ? "[提取主干]" : "[检测头]";
                _node.ConfigDisplay = $"{roleTag} {shortName} | {classInfo}";
            }

            this.DialogResult = true;
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}