using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VisionInspect.Core.Models;

namespace VisionInspect.Core.Algorithms
{
    /// <summary>
    /// 算法引擎统一抽象。UI、检测控制器、结果后处理只依赖本接口，
    /// 不引用 VisionMaster 或 Halcon 的任何具体类型。
    ///
    /// 约定：
    /// 1. 所有图像输入使用 UniversalImageData；
    /// 2. 所有检测输出使用 DetectResult；
    /// 3. 参数读写使用 ParamItem/paramName，名称冲突时使用 "模块名|参数名"；
    /// 4. 实现内部负责线程串行化，调用方不得在 UI 线程执行同步 Run/LoadScheme。
    /// </summary>
    public interface IAlgorithmEngine : IDisposable
    {
        /// <summary>引擎显示名，例如 VisionMaster 4.3.0 / Halcon。</summary>
        string EngineName { get; }

        /// <summary>当前加载的方案绝对路径。</summary>
        string LoadedSchemePath { get; }

        /// <summary>是否已成功加载方案。</summary>
        bool IsLoaded { get; }

        /// <summary>引擎类型，仅用于显示/能力判断，不用于分支算法实现。</summary>
        AlgorithmEngineKind Kind { get; }

        /// <summary>同步加载方案；.sol 走 VM，.hdev 走 Halcon。禁止在 UI 线程直接调用。</summary>
        void LoadScheme(string filePath, string password = null);

        /// <summary>异步加载方案，UI 优先使用本方法。</summary>
        Task LoadSchemeAsync(string filePath, string password, CancellationToken cancellationToken);

        /// <summary>设置待检测图像。实现不得长期持有调用方的非托管指针。</summary>
        void SetInputImage(UniversalImageData image);

        /// <summary>异步设置待检测图像。</summary>
        Task SetInputImageAsync(UniversalImageData image, CancellationToken cancellationToken);

        /// <summary>同步执行检测。仅允许后台线程调用。</summary>
        DetectResult Run();

        /// <summary>异步执行检测。</summary>
        Task<DetectResult> RunAsync(CancellationToken cancellationToken);

        /// <summary>获取最近一次检测结果；未执行时返回 null。</summary>
        DetectResult GetResult();

        /// <summary>获取当前引擎全部可配置参数。</summary>
        IReadOnlyList<ParamItem> GetParamList();

        /// <summary>设置单个参数并立即生效。</summary>
        void SetParam(string paramName, object value);

        /// <summary>异步设置单个参数。</summary>
        Task SetParamAsync(string paramName, object value, CancellationToken cancellationToken);

        /// <summary>释放引擎资源；调用后不得再次执行检测。</summary>
        Task ShutdownAsync();
    }
}
