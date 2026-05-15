import sys
import os
import argparse
import onnx
from onnx import helper, shape_inference
from onnx.utils import extract_model

# 【核心秘籍】：常见模型的标准切分点字典
PRESETS = {
    "yolov8": "model.9.cv2.act.Mul_output_0",
    "yolov5": "model.9.cv3.act.Mul_output_0",
    "yolov11": "model.22.cv2.act.Mul_output_0",
    "yolov12": "model.23.cv2.act.Mul_output_0"
}

def deep_clean_head_model(model_path, cut_node_name):
    """
    深度清理逻辑：强行剔除所有不属于 Head 路径的残留节点，解决拓扑排序错误。
    """
    m = onnx.load(model_path)
    graph = m.graph

    # 1. 强制重置图输入，只保留切分点作为唯一入口
    input_info = None
    # 在现有信息中寻找该张量的形状和类型定义
    for value in list(graph.value_info) + list(graph.input):
        if value.name == cut_node_name:
            input_info = value
            break
    
    graph.ClearField("input")
    if input_info:
        graph.input.extend([input_info])
    else:
        # 兜底：如果找不到，构造一个通用的 float 输入
        new_inp = helper.make_tensor_value_info(cut_node_name, onnx.TensorProto.FLOAT, None)
        graph.input.extend([new_inp])

    # 2. 物理修剪残留节点（解决 Topologically sorted 报错的关键）
    # 核心思路：只有从 cut_node 或 Initializer(权重) 出发的路径才是合法的
    for _ in range(5):  # 多轮迭代确保清理掉深层依赖链
        valid_tensors = {cut_node_name}
        for init in graph.initializer:
            valid_tensors.add(init.name)
        
        nodes_to_keep = []
        for node in graph.node:
            # 【核心修复】：如果节点没有输入（如 Constant 常量算子），或者它的输入在合法集合中，则保留！
            if len(node.input) == 0 or any(inp in valid_tensors for inp in node.input):
                nodes_to_keep.append(node)
                for out in node.output:
                    valid_tensors.add(out)
        
        if len(nodes_to_keep) == len(graph.node):
            break
        
        graph.ClearField("node")
        graph.node.extend(nodes_to_keep)

    # 3. 优化与验证
    try:
        # 移除未使用的初始化器（权重），减小体积并清理残留
        import onnxoptimizer
        m = onnxoptimizer.optimize(m, ["eliminate_unused_initializer"])
    except ImportError:
        pass

    try:
        m = shape_inference.infer_shapes(m)
        onnx.checker.check_model(m)
    except Exception as e:
        print(f"  [提示] 模型检查反馈: {e}")
    
    onnx.save(m, model_path)

def slice_model(input_path, model_type, custom_node):
    print(f"--- 正在启动【强力修剪版】模型切片引擎 ---")
    print(f"输入模型: {input_path}")
    
    cut_node = custom_node
    if model_type in PRESETS:
        cut_node = PRESETS[model_type]
        print(f"匹配到预设架构 [{model_type}], 自动设定切分点: {cut_node}")
    elif not cut_node:
        print("错误: 必须指定切分节点名称！")
        sys.exit(1)
        
    output_dir = os.path.dirname(input_path)
    backbone_path = os.path.join(output_dir, "backbone.onnx")
    head_path = os.path.join(output_dir, "head.onnx")

    try:
        # 1. 提取 Backbone 
        print("正在分离主干网络 (Backbone)...")
        extract_model(input_path, backbone_path, input_names=["images"], output_names=[cut_node])
        
        # 2. 提取 Head
        print("正在分离检测头 (Head)...")
        # 注意：这里 extract_model 经常会残留 images 相关的上游节点，必须后续深度清理
        extract_model(input_path, head_path, input_names=[cut_node], output_names=["output0"])
        
        # 3. 执行深度物理修剪（解决 Head 模型报错的核心步骤）
        print("正在执行 Head 模型的深度拓扑修复...")
        deep_clean_head_model(head_path, cut_node)
        
        # 4. 优化 Backbone
        m_back = onnx.load(backbone_path)
        onnx.save(shape_inference.infer_shapes(m_back), backbone_path)

        print(f"--- 切片成功！---")
        print(f"1. {backbone_path}")
        print(f"2. {head_path}")
        
    except Exception as e:
        print(f"切片失败: {str(e)}")
        sys.exit(1)

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--input', required=True, help='原始模型路径')
    parser.add_argument('--type', default='custom', help='模型类型: yolov8, yolov5, yolov11, yolov12, custom')
    parser.add_argument('--node', default='', help='自定义切分点(张量名)')
    
    args = parser.parse_args()
    slice_model(args.input, args.type.lower(), args.node)
