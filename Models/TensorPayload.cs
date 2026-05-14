using System;
using OpenCvSharp;
using HMManager;

namespace ModelHotSwapWorkflow.Models
{
    /// <summary>
    /// 基于内存复用的跨节点张量零拷贝（Zero-Copy）总线传输载体。
    /// 【专利核心点】：支持宏观图像像素张量与微观高维特征张量的双轨并行传输，
    /// 避免了传统级联推理中多余的图像解码、拷贝与显存分配开销。
    /// </summary>
    public class TensorPayload : IDisposable
    {
        /// <summary>
        /// 舱位 A：获取或设置核心图像张量（基于连续内存块的 N 维原始图片矩阵）。
        /// 用于模式一（标准级联）的全链路透传，或模式二（特征共享）的一阶段输入。
        /// </summary>
        public Mat BaseTensor { get; set; }

        /// <summary>
        /// 舱位 B：获取或设置共享高维特征张量（Shared Feature Space）。
        /// 【模式二专用】：由主干网络（Backbone）提取并向下游（Head）广播，供子节点进行 RoI 特征裁剪。
        /// </summary>
        public Mat FeatureTensor { get; set; }

        /// <summary>
        /// 舱位 C：获取或设置上游模型输出的感兴趣区域（RoI）集合及推理结果。
        /// 若为空，则表示当前节点需进行全图尺寸的特征提取或推理。
        /// </summary>
        public DetectionResultCollection RoiResults { get; set; }

        /// <summary>
        /// 释放底层非托管张量内存资源，严格防止高速流水线发生显存泄漏（OOM）。
        /// </summary>
        public void Dispose()
        {
            if (BaseTensor != null && !BaseTensor.IsDisposed)
            {
                BaseTensor.Dispose();
                BaseTensor = null;
            }

            if (FeatureTensor != null && !FeatureTensor.IsDisposed)
            {
                FeatureTensor.Dispose();
                FeatureTensor = null;
            }
        }
    }
}