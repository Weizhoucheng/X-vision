using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VisionInspect.Core;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Algorithms;
using VisionInspect.Core.Configuration;
using VisionInspect.Core.Models;

namespace VisionInspect.VisionMaster
{
    /// <summary>
    /// VisionMaster 4.3.0 算法引擎适配器。
    /// 该类是 VM SDK 与统一 IAlgorithmEngine 之间唯一的桥接层：
    /// - 上层只看到 UniversalImageData / DetectResult / ParamItem；
    /// - 原有 VisionMasterEngine 的流程、模块状态、格式化输出和参数能力全部保留；
    /// - NativeResult 保留原 VisionInspectionResult，供现有 UI 继续显示模块/输出。
    /// </summary>
    public sealed class VmAlgorithmEngine : IAlgorithmEngine
    {
        private readonly VisionMasterEngine _inner;
        private readonly VisionSettings _visionSettings;
        private readonly ResultJudgeOptions _judgeOptions;
        private readonly IAppLogger _logger;
        private readonly object _sync = new object();

        private UniversalImageData _input;
        private DetectResult _lastResult;
        private bool _disposed;

        public VmAlgorithmEngine(VisionMasterEngine inner, VisionSettings visionSettings,
            ResultJudgeOptions judgeOptions, IAppLogger logger)
        {
            if (inner == null) throw new ArgumentNullException("inner");
            _inner = inner;
            _visionSettings = visionSettings ?? new VisionSettings();
            _judgeOptions = judgeOptions ?? new ResultJudgeOptions();
            _logger = logger;
        }

        public string EngineName { get { return "VisionMaster 4.3.0"; } }
        public string LoadedSchemePath { get { return _inner.LoadedSolutionPath; } }
        public bool IsLoaded { get { return _inner.IsLoaded; } }
        public AlgorithmEngineKind Kind { get { return AlgorithmEngineKind.VisionMaster; } }

        /// <summary>现有 UI 需要绑定的 VM 原生引擎；业务层不应直接使用。</summary>
        public VisionMasterEngine Inner { get { return _inner; } }

        public void LoadScheme(string filePath, string password = null)
        {
            LoadSchemeAsync(filePath, password, CancellationToken.None).GetAwaiter().GetResult();
        }

        public async Task LoadSchemeAsync(string filePath, string password, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await _inner.LoadSolutionAsync(filePath, password ?? string.Empty, cancellationToken)
                .ConfigureAwait(false);
            if (_logger != null)
                _logger.Info("Algorithm", "VM 方案加载完成：" + filePath);
        }

        public void SetInputImage(UniversalImageData image)
        {
            ThrowIfDisposed();
            if (image == null) throw new ArgumentNullException("image");
            lock (_sync)
            {
                _input = image;
            }
        }

        public Task SetInputImageAsync(UniversalImageData image, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetInputImage(image);
            return Task.CompletedTask;
        }

        public DetectResult Run()
        {
            return RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public async Task<DetectResult> RunAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            UniversalImageData input;
            lock (_sync) input = _input;
            if (input == null) throw new VisionInspectException("尚未设置输入图像。");
            if (!_inner.IsLoaded) throw new VisionInspectException("尚未加载 VM 方案。");

            string procedure = _visionSettings.PrimaryProcedureName;
            VisionInspectionResult inspection = await _inner.RunOnceAsync(
                input.ToRawImage(), procedure, _judgeOptions, cancellationToken).ConfigureAwait(false);
            if (inspection == null) throw new VisionInspectException("VM 未返回检测结果。");

            DetectResult result = AlgorithmResultConverter.FromVisionInspectionResult(inspection);
            result.EngineName = EngineName;
            result.SchemePath = _inner.LoadedSolutionPath;
            lock (_sync) _lastResult = result;
            return result;
        }

        public DetectResult GetResult()
        {
            lock (_sync) return _lastResult;
        }

        public IReadOnlyList<ParamItem> GetParamList()
        {
            ThrowIfDisposed();
            var result = new List<ParamItem>();
            if (!_inner.IsLoaded || _inner.Modules == null) return result;

            foreach (VisionModuleInfo module in _inner.Modules)
            {
                IReadOnlyList<VisionParameterInfo> parameters;
                try
                {
                    parameters = _inner.GetParametersAsync(module.FullName, CancellationToken.None)
                        .GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    if (_logger != null)
                        _logger.Warn("Algorithm", "读取 VM 模块参数失败：" + module.FullName + "；" + ex.Message);
                    continue;
                }

                foreach (VisionParameterInfo parameter in parameters ?? new List<VisionParameterInfo>())
                {
                    var item = new ParamItem
                    {
                        Name = module.FullName + "|" + parameter.Name,
                        DisplayName = string.IsNullOrWhiteSpace(parameter.DisplayName)
                            ? parameter.Name : parameter.DisplayName,
                        Group = string.IsNullOrWhiteSpace(parameter.Category)
                            ? module.DisplayName : parameter.Category,
                        Description = module.DisplayName + " / " + parameter.Name,
                        DataType = AlgorithmResultConverter.ResolveParamType(
                            parameter.TypeName, parameter.IsEnum, parameter.EnumValues),
                        Value = parameter.ValueText,
                        DefaultValue = parameter.ValueText,
                        IsReadOnly = !parameter.IsWritable,
                        Options = parameter.EnumValues == null
                            ? new List<string>() : new List<string>(parameter.EnumValues)
                    };
                    result.Add(item);
                }
            }
            return result;
        }

        public void SetParam(string paramName, object value)
        {
            SetParamAsync(paramName, value, CancellationToken.None).GetAwaiter().GetResult();
        }

        public async Task SetParamAsync(string paramName, object value, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(paramName))
                throw new ArgumentException("参数名不能为空。", "paramName");

            string moduleName;
            string name;
            int separator = paramName.IndexOf('|');
            if (separator > 0)
            {
                moduleName = paramName.Substring(0, separator);
                name = paramName.Substring(separator + 1);
            }
            else
            {
                VisionModuleInfo first = _inner.Modules == null ? null : _inner.Modules.FirstOrDefault();
                if (first == null) throw new VisionInspectException("当前方案没有可配置模块。");
                moduleName = first.FullName;
                name = paramName;
            }

            ParameterApplyResult applied = await _inner.SetParameterAsync(
                moduleName, name, AlgorithmResultConverter.Format(value), false, cancellationToken)
                .ConfigureAwait(false);
            if (applied == null || !applied.Success)
            {
                string details = applied == null || applied.Failures == null
                    ? "未知错误" : string.Join("; ", applied.Failures);
                throw new VisionInspectException("VM 参数写入失败：" + details);
            }
        }

        public async Task ShutdownAsync()
        {
            if (_disposed) return;
            _disposed = true;
            try { await _inner.ShutdownAsync().ConfigureAwait(false); }
            finally
            {
                lock (_sync)
                {
                    _input = null;
                    _lastResult = null;
                }
            }
        }

        public void Dispose()
        {
            try { ShutdownAsync().GetAwaiter().GetResult(); } catch { }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
        }
    }
}
