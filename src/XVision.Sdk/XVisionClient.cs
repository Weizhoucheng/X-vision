using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using VisionInspect.Core;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Configuration;
using VisionInspect.Core.Models;
using VisionInspect.VisionMaster.Runtime;

namespace XVision.Sdk
{
    public sealed class XVisionSdkOptions
    {
        public string VisionMasterDirectory { get; set; }
        public string ParameterDirectory { get; set; }
        public string BaseDirectory { get; set; }
        public VisionSettings Vision { get; set; } = new VisionSettings();
        public ResultJudgeOptions ResultJudge { get; set; } = new ResultJudgeOptions();
    }
    /// <summary>独立于 X Vision GUI 的 .sol 运行接口。底层运行库与官方许可仍由 VisionMaster 提供。</summary>
    public sealed class XVisionClient : IDisposable
    {
        private readonly IVisionEngine _engine;
        private readonly XVisionSdkOptions _options;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1,1);
        private bool _disposed;
        private XVisionClient(IVisionEngine engine, XVisionSdkOptions options) { _engine=engine; _options=options; }
        public bool IsLoaded { get { return !_disposed && _engine.IsLoaded; } }
        public VisionSolutionInfo CurrentSolution { get { return _engine.CurrentSolution; } }
        public static XVisionClient Create(XVisionSdkOptions options, IAppLogger logger = null)
        {
            if(options==null)throw new ArgumentNullException(nameof(options));
            var json=new JavaScriptSerializer(); options=json.Deserialize<XVisionSdkOptions>(json.Serialize(options));
            options.BaseDirectory=Path.GetFullPath(string.IsNullOrWhiteSpace(options.BaseDirectory)?Environment.CurrentDirectory:options.BaseDirectory);
            options.Vision=options.Vision??new VisionSettings();options.Vision.Normalize();
            options.ResultJudge=options.ResultJudge??new ResultJudgeOptions();options.ResultJudge.Normalize();
            if(string.IsNullOrWhiteSpace(options.ParameterDirectory))throw new ArgumentException("请指定可写的 ParameterDirectory。",nameof(options));
            options.ParameterDirectory=Path.GetFullPath(options.ParameterDirectory);
            VisionMasterEnvironment.Prepare(new AppSettings{VisionMasterInstallDir=options.VisionMasterDirectory},logger);
            return new XVisionClient(CreateEngine(options,logger),options);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IVisionEngine CreateEngine(XVisionSdkOptions options,IAppLogger logger)
        { return new VisionInspect.VisionMaster.VisionMasterEngine(options.ParameterDirectory,options.Vision,logger); }
        public static VmRuntimeReport InspectEnvironment(string configuredDirectory=null) { return VmRuntimeDiagnostics.Inspect(configuredDirectory); }
        public Task<VisionSolutionInfo> LoadAsync(string solutionPath,string password=null,CancellationToken token=default(CancellationToken))
        {
            if(!string.Equals(Path.GetExtension(solutionPath),".sol",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("请选择 .sol 方案。",nameof(solutionPath));
            string full=Path.IsPathRooted(solutionPath)?Path.GetFullPath(solutionPath):Path.GetFullPath(Path.Combine(_options.BaseDirectory,solutionPath));
            return Execute(()=>_engine.LoadSolutionAsync(full,password,token),token);
        }
        public async Task<VisionInspectionResult> InspectFileAsync(string file,string procedure=null,CancellationToken token=default(CancellationToken))
        {
            if(string.IsNullOrWhiteSpace(file))throw new ArgumentException("请提供图片路径。",nameof(file));
            string full=Path.IsPathRooted(file)?Path.GetFullPath(file):Path.GetFullPath(Path.Combine(_options.BaseDirectory,file));
            var image=await Task.Run(()=>RawImage.FromFile(full),token).ConfigureAwait(false);
            return await InspectAsync(image,procedure,token).ConfigureAwait(false);
        }
        public Task<VisionInspectionResult> InspectAsync(RawImage image,string procedure=null,CancellationToken token=default(CancellationToken))
        {
            if(image==null)throw new ArgumentNullException(nameof(image));
            return Execute(()=>{
                if(!_engine.IsLoaded)throw new InvalidOperationException("请先成功加载 .sol 方案。");
                string name=string.IsNullOrWhiteSpace(procedure)?_engine.CurrentSolution.PrimaryProcedureName:procedure;
                return _engine.RunOnceAsync(image,name,_options.ResultJudge,token);
            },token);
        }
        public Task SaveAsync(CancellationToken token=default(CancellationToken)) { return Execute(()=>_engine.SaveSolutionAsync(token),token); }
        public Task UnloadAsync(CancellationToken token=default(CancellationToken)) { return Execute(()=>_engine.UnloadSolutionAsync(token),token); }
        public Task<IReadOnlyList<VisionParameterInfo>> GetParametersAsync(string module,CancellationToken token=default(CancellationToken))
        { return Execute(()=>_engine.GetParametersAsync(module,token),token); }
        public Task<ParameterApplyResult> SetParameterAsync(string module,string parameter,string value,bool save=false,CancellationToken token=default(CancellationToken))
        { return Execute(()=>_engine.SetParameterAsync(module,parameter,value,save,token),token); }
        private async Task<T> Execute<T>(Func<Task<T>> action,CancellationToken token)
        { await _gate.WaitAsync(token).ConfigureAwait(false);try{if(_disposed)throw new ObjectDisposedException(nameof(XVisionClient));return await action().ConfigureAwait(false);}finally{_gate.Release();} }
        private async Task Execute(Func<Task> action,CancellationToken token)
        { await _gate.WaitAsync(token).ConfigureAwait(false);try{if(_disposed)throw new ObjectDisposedException(nameof(XVisionClient));await action().ConfigureAwait(false);}finally{_gate.Release();} }
        public async Task ShutdownAsync()
        { await _gate.WaitAsync().ConfigureAwait(false);try{if(_disposed)return;await _engine.ShutdownAsync().ConfigureAwait(false);_disposed=true;}finally{_gate.Release();} }
        public void Dispose(){ShutdownAsync().GetAwaiter().GetResult();}
    }
}
