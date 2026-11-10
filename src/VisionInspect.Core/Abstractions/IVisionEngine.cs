using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VisionInspect.Core.Configuration;
using VisionInspect.Core.Models;

namespace VisionInspect.Core.Abstractions
{
    /// <summary>
    /// 视觉引擎抽象。实现必须将所有 VisionMaster SDK 调用串行化，且不得在 UI 线程执行阻塞调用。
    /// 返回的 object 为 VM 原生流程/模块对象，仅供 Windows 控件绑定，业务层不应依赖其类型。
    /// </summary>
    public interface IVisionEngine
    {
        bool IsLoaded { get; }
        string LoadedSolutionPath { get; }
        VisionSolutionInfo CurrentSolution { get; }
        IReadOnlyList<VisionModuleInfo> Modules { get; }

        Task<VisionSolutionInfo> LoadSolutionAsync(string solutionPath, string password, CancellationToken cancellationToken);
        Task UnloadSolutionAsync(CancellationToken cancellationToken);
        Task SaveSolutionAsync(CancellationToken cancellationToken);

        Task<VisionInspectionResult> RunOnceAsync(
            RawImage image,
            string procedureName,
            ResultJudgeOptions judgeOptions,
            CancellationToken cancellationToken);

        Task<object> GetRenderSourceAsync(string procedureName, CancellationToken cancellationToken);
        Task<object> GetModuleSourceAsync(string moduleFullName, CancellationToken cancellationToken);

        Task<IReadOnlyList<VisionParameterInfo>> GetParametersAsync(string moduleFullName, CancellationToken cancellationToken);
        Task<ParameterApplyResult> SetParameterAsync(string moduleFullName, string parameterName, string value,
            bool persistSolution, CancellationToken cancellationToken);
        Task<ParameterSnapshot> CaptureParametersAsync(string moduleFullName, CancellationToken cancellationToken);
        Task<ParameterApplyResult> ApplyParametersAsync(string moduleFullName, ParameterSnapshot snapshot,
            bool persistSolution, CancellationToken cancellationToken);
        Task SaveParameterPresetAsync(string moduleFullName, string presetFilePath, CancellationToken cancellationToken);
        Task<ParameterApplyResult> LoadParameterPresetAsync(string moduleFullName, string presetFilePath,
            bool persistSolution, CancellationToken cancellationToken);
        Task<ParameterApplyResult> ResetParametersToDefaultAsync(string moduleFullName, bool persistSolution,
            CancellationToken cancellationToken);

        /// <summary>最终退出时调用：释放方案、VM 单例和 SDK 工作线程。</summary>
        Task ShutdownAsync();
    }
}
