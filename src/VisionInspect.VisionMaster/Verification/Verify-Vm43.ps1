param(
    [string]$VisionMasterRoot = "D:\Program Files\VisionMaster4.3.0",
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug'
)

# 真正调用本机官方 SDK，绝不替代 VM 类型。需要有效许可；与主程序分开运行。
# 方案复制到临时目录执行，不修改官方示例。临时目录保留用于诊断。
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '..\..'))
$probeRoot = Join-Path ([IO.Path]::GetTempPath()) ('VisionInspectVmProbe_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeRoot | Out-Null
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vsRoot = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
$compiler = Join-Path $vsRoot 'MSBuild\Current\Bin\Roslyn\csc.exe'
$coreAssembly = Join-Path $workspaceRoot "src\VisionInspect.Core\bin\$Configuration\VisionInspect.Core.dll"
$vmAssembly = Join-Path $projectRoot "bin\$Configuration\VisionInspect.VisionMaster.dll"
Copy-Item -LiteralPath $coreAssembly,$vmAssembly -Destination $probeRoot
$source = @'
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VisionInspect.App.Runtime;
using VisionInspect.Core;
using VisionInspect.Core.Configuration;
using VisionInspect.VisionMaster;
using VisionInspect.VisionMaster.Parameters;

public static class Vm43Probe
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            var settings = new AppSettings { VisionMasterInstallDir = args[0] };
            VisionMasterEnvironment.Prepare(settings, null);
            Run(args[0], args[1]).GetAwaiter().GetResult();
            Console.WriteLine("PASS: official VM SDK load / inject / run / outputs / switch / unload / shutdown");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static async Task Run(string sdkRoot, string probeRoot)
    {
        var engine = new VisionMasterEngine(Path.Combine(probeRoot, "params"), new VisionSettings(), null);
        try
        {
            var samples = Path.Combine(sdkRoot, @"Development\V4.x\Samples");
            var image = RawImage.FromFile(Path.Combine(samples, @"VC\PlatformSDKSampleVC\GetResultControl\image.jpg"));
            var solutions = new[] {
                Path.Combine(samples, @"C#\PlatformSDKSampleCS\CircleFind\CircleFind.sol"),
                Path.Combine(samples, @"C#\PlatformSDKSampleCS\GetResultControl\SampleSolution\GetResultSample.sol")
            };
            foreach (string original in solutions)
            {
                string path = Path.Combine(probeRoot, Path.GetFileName(original));
                File.Copy(original, path);
                var solution = await engine.LoadSolutionAsync(path, "", CancellationToken.None);
                if (solution.Procedures.Count == 0 || engine.Modules.Count == 0)
                    throw new InvalidOperationException("SDK did not enumerate procedures/modules");
                Console.WriteLine("LOAD {0}: {1} procedures, {2} modules", Path.GetFileName(path), solution.Procedures.Count, engine.Modules.Count);
                for (int iteration = 0; iteration < 3; iteration++)
                {
                    var result = await engine.RunOnceAsync(image, solution.PrimaryProcedureName, new ResultJudgeOptions(), CancellationToken.None);
                    if (result.Modules.Count == 0) throw new InvalidOperationException("No module statuses");
                    Console.WriteLine("RUN {0}: {1:F1} ms, {2} output rows, judge={3}", iteration, result.DurationMs, result.Outputs.Count, result.JudgeText);
                }
                var module = engine.Modules.First(x => x.IsImageSource);
                var snapshot = await engine.CaptureParametersAsync(module.FullName, CancellationToken.None);
                string presetPath = Path.Combine(probeRoot, "preset.json");
                ParameterPresetStore.Save(presetPath, snapshot);
                ParameterPresetStore.Save(presetPath, snapshot); // 原子替换已有文件。
                var restored = ParameterPresetStore.Load(presetPath);
                if (restored.ModuleFullName != module.FullName || restored.Values.Count != snapshot.Values.Count)
                    throw new InvalidOperationException("Preset roundtrip failed");
                Console.WriteLine("PRESET {0}: {1} writable scalars", module.FullName, restored.Values.Count);
                restored.ModuleFullName = "OtherProcedure.OtherModule";
                try
                {
                    await engine.ApplyParametersAsync(module.FullName, restored, false, CancellationToken.None);
                    throw new InvalidOperationException("Mismatched preset was accepted");
                }
                catch (VisionInspectException) { Console.WriteLine("PASS: mismatched module preset rejected"); }
                var reset = await engine.ResetParametersToDefaultAsync(module.FullName, false, CancellationToken.None);
                if (!reset.Success || reset.Applied != 1) throw new InvalidOperationException("Official module ResetParam failed");
                Console.WriteLine("PASS: official VmModule.ResetParam");
            }
            await engine.UnloadSolutionAsync(CancellationToken.None);
            if (engine.IsLoaded) throw new InvalidOperationException("Unload did not clear state");
        }
        finally { await engine.ShutdownAsync(); }
    }
}
'@
$sourcePath = Join-Path $probeRoot 'Vm43Probe.cs'
[IO.File]::WriteAllText($sourcePath, $source, [Text.UTF8Encoding]::new($false))
$environmentSource = Join-Path $workspaceRoot 'src\VisionInspect.App\Runtime\VisionMasterEnvironment.cs'
$probeExe = Join-Path $probeRoot 'Vm43Probe.exe'
& $compiler /nologo /target:exe /platform:x64 "/out:$probeExe" "/reference:$coreAssembly" "/reference:$vmAssembly" /reference:System.Drawing.dll /reference:System.Windows.Forms.dll $sourcePath $environmentSource
if ($LASTEXITCODE -ne 0) { throw "VM probe compilation failed: $LASTEXITCODE" }
Write-Host "VM SDK probe: $probeRoot"
& $probeExe $VisionMasterRoot $probeRoot
if ($LASTEXITCODE -ne 0) { throw "VM SDK probe failed: $LASTEXITCODE. Evidence: $probeRoot" }
