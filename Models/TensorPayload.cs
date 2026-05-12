using System;
using OpenCvSharp;
using HMManager;

namespace ModelHotSwapWorkflow.Models
{
    /// <summary>
    /// 级联推理工作流中的标准张量数据载体。
    /// 封装底层连续内存矩阵（Mat）与特征区域集合，避免 GDI+ 对象的序列化与内存分配开销。
    /// </summary>
    public class TensorPayload : IDisposable
    {
        /// <summary>
        /// 获取或设置核心图像张量（基于连续内存块的 N 维矩阵）。
        /// </summary>
        public Mat BaseTensor { get; set; }

        /// <summary>
        /// 获取或设置上游模型输出的感兴趣区域（ROI）集合。
        /// 若为空，则表示当前需进行全图级联推理。
        /// </summary>
        public DetectionResultCollection RoiResults { get; set; }

        /// <summary>
        /// 释放底层非托管张量内存资源。
        /// </summary>
        public void Dispose()
        {
            if (BaseTensor != null && !BaseTensor.IsDisposed)
            {
                BaseTensor.Dispose();
                BaseTensor = null;
            }
        }
    }
}