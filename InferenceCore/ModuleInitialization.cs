using System;

namespace HMManager
{
    public static class ModuleInitialization
    {
        // 由于使用了 NuGet 安装 OpenCV 和 ONNX，底层的 C++ DLL 会自动拷贝和解析。
        // 原有的解压内嵌资源的逻辑已不再需要，保留此空壳以保证外部调用不报错。
        public static bool Init()
        {
            return true;
        }
    }
}