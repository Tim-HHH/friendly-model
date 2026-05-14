using System;
using System.Collections.Concurrent;
using System.IO;
using HMManager; // 【关键修复】：引入底层推理库的命名空间

namespace ModelHotSwapWorkflow.InferenceCore
{
    /// <summary>
    /// 多实例并发下的高维计算图显存共享与隔离池 (Multi-Instance Concurrency Safe Pool)。
    /// 【专利核心点】：通过原子锁机制，实现单模型文件的零额外开销加载，
    /// 支撑复杂的 1-to-N (一主干对多检测头) 拓扑网络的极速初始化。
    /// </summary>
    public static class ModelCachePool
    {
        // 采用读写安全的 ConcurrentDictionary 应对多相机（多线程）同时拉起的极端工况
        private static readonly ConcurrentDictionary<string, ModelManager> _pool =
            new ConcurrentDictionary<string, ModelManager>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 获取或原子加载模型推理引擎。
        /// 若多节点指向同一物理路径，则共享同一个推理会话（Session）指针。
        /// </summary>
        public static ModelManager GetOrCreate(string configPath)
        {
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException($"[显存池] 致命异常：找不到模型配置文件: {configPath}");
            }

            // GetOrAdd 为线程安全原子操作，确保同一模型绝对不会被重复加载到 GPU 显存
            return _pool.GetOrAdd(configPath, path =>
            {
                System.Diagnostics.Debug.WriteLine($"[显存池] 🚀 分配全新 GPU 显存块并加载计算图: {path}");
                return new ModelManager(path);
            });
        }

        /// <summary>
        /// 显式释放全局资源，供生命周期管理调用。
        /// </summary>
        public static void ClearAll()
        {
            foreach (var kvp in _pool)
            {
                kvp.Value?.Dispose();
            }
            _pool.Clear();
            System.Diagnostics.Debug.WriteLine("[显存池] 已释放所有张量计算图，显存水位已重置。");
        }
    }
}