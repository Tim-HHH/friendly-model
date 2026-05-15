import sys
import os
import argparse
import onnx
from onnx.utils import extract_model

# 【核心秘籍】：常见模型的标准切分点字典
# 注意：这里的名字只是举例。您需要用 Netron 看一次你们团队导出的 YOLO，把真实的节点名字填在这里。
PRESETS = {
    "yolov8": "model.9.cv2.act.Mul_output_0",  # 假设这是 YOLOv8 SPPF 层的输出
    "yolov5": "model.9.cv3.act.Mul_output_0" ,  # 假设这是 YOLOv5 SPPF 层的输出
    "yolov11": "model.22.cv2.act.Mul_output_0",  # YOLOv11 常见的 SPPF 层输出节点
    "yolov12": "model.23.cv2.act.Mul_output_0"   # YOLOv12 预想的主干分界点
}
#111111111111111111111
def slice_modell(input_path, model_type, custom_node):
    print(f"--- 正在启动模型切片引擎 ---")
    print(f"输入模型: {input_path}")
    
    # 1. 确定下刀点
    cut_node = custom_node
    if model_type in PRESETS:
        cut_node = PRESETS[model_type]
        print(f"匹配到预设架构 [{model_type}], 自动设定切分点为: {cut_node}")
    elif not cut_node:
        print("错误: 必须指定切分节点名称！")
        sys.exit(1)
        
    output_dir = os.path.dirname(input_path)
    backbone_path = os.path.join(output_dir, "backbone.onnx")
    head_path = os.path.join(output_dir, "head.onnx")

    try:
        # 1. 分离 Backbone
        print("正在分离主干网络 (Backbone)...")
        extract_model(input_path, backbone_path, input_names=["images"], output_names=[cut_node])
        
        # 2. 分离 Head
        print("正在分离检测头 (Head)...")
        # 这里关键：我们必须明确告诉 ONNX，Head 的输入现在就是那个切分点节点
        extract_model(input_path, head_path, input_names=[cut_node], output_names=["output0"])
        
        # 3. 核心修复：清理 Head 模型的残留冗余节点
        # 有时候 extract_model 会留下一些断开连接的旧节点（如原本的 images），导致排序报错
        for path in [backbone_path, head_path]:
            m = onnx.load(path)
            # 自动推导形状并清理无效节点
            m = onnx.shape_inference.infer_shapes(m)
            
            # 如果是 Head 模型，额外执行一次清理，确保除了 cut_node 以外没有其他“悬空”输入
            if path == head_path:
                from onnx import optimizer
                # 注意：如果环境中没有 onnxoptimizer，可以跳过这一步，直接依赖下方的重新保存
                try:
                    import onnxoptimizer
                    m = onnxoptimizer.optimize(m)
                except ImportError:
                    pass

            # 强制重新生成规范的计算图
            onnx.checker.check_model(m)
            onnx.save(m, path)

        print(f"--- 切片成功！---")
        
    except Exception as e:
        print(f"切片失败: {str(e)}")
        sys.exit(1)

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, help="原始模型路径")
    parser.add_argument("--type", default="custom", help="预设模型类型: yolov8, yolov5, custom")
    parser.add_argument("--node", default="", help="自定义切分节点名 (type为custom时必填)")
    
    args = parser.parse_args()
    slice_model(args.input, args.type.lower(), args.node)