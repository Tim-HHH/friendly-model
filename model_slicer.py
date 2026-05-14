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

def slice_model(input_path, model_type, custom_node):
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
        # 假设原图输入节点统一叫 "images"，最终输出统一叫 "output0"
        # （根据您实际模型修改）
        
        # 切前半截 (Backbone)
        print("正在分离主干网络 (Backbone)...")
        extract_model(input_path, backbone_path, input_names=["images"], output_names=[cut_node])
        
        # 切后半截 (Head)
        print("正在分离检测头 (Head)...")
        extract_model(input_path, head_path, input_names=[cut_node], output_names=["output0"])
        
        print(f"--- 切片成功！---")
        print(f"Backbone 已保存至: {backbone_path}")
        print(f"Head 已保存至: {head_path}")
        
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