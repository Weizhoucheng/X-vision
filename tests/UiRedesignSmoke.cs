using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using VisionInspect.App.Bootstrap;
using VisionInspect.App.Runtime;
using VisionInspect.App.UI;
using VisionInspect.Core;
using VisionInspect.Core.Configuration;
using VisionInspect.Infrastructure.Configuration;
using VisionInspect.Infrastructure.Logging;
using VisionInspect.Infrastructure.Paths;

internal static class UiRedesignSmoke
{
    private static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    [STAThread]private static int Main(string[] args)
    {
        try{
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            VisionMasterEnvironment.Prepare(new AppSettings{VisionMasterInstallDir=args[0]},null);
            Run(args[0],args[1]);return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void Run(string sdk,string output)
    {
        Directory.CreateDirectory(output);var paths=AppPaths.Create(Path.Combine(output,"Data"));
        var settings=new AppSettings();settings.General.ConfirmBeforeExit=false;settings.General.RememberLastSolution=false;
        settings.Communication.ModbusTcp.Enabled=false;settings.Communication.Serial.Enabled=false;settings.Communication.TcpTrigger.Enabled=false;
        var store=new JsonSettingsStore(paths,null);
        var logger=new FileAppLogger(paths.SystemLogDirectory,paths.AuditLogDirectory,7,false);
        var app=new AppBootstrapper(paths,settings,store,logger,false);
        if(!app.Auth.Login("admin","admin").Success)throw new Exception("Isolated auth failed.");
        settings.UiTheme="Dark";store.Save(settings);
        Theme.SetDark(string.Equals(store.Load().UiTheme,"Dark",StringComparison.Ordinal));BrandAssets.WarmUp();
        var form=new MainForm(app){Opacity=0,ShowInTaskbar=false,WindowState=FormWindowState.Normal};form.ClientSize=new Size(1560,980);form.Show();
        Pump(Task.Delay(300));
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        if(((Control)Field(form,"_lblAppTitle")).ForeColor!=Theme.Text)throw new Exception("Dark startup text contrast failed.");
        var render=(Control)Field(form,"_vmRender");if(render==null)throw new Exception("VM native renderer missing.");
        IntPtr rendererHandle=render.Handle;
        var intervals=new System.Collections.Generic.List<double>();var frames=new System.Collections.Generic.List<double>();
        var measurements=new System.Collections.Generic.List<string>{"switch,transition_ms,palette_frame_max_ms,ui_gap_max_ms"};
        var heartbeat=new System.Windows.Forms.Timer{Interval=10};var clock=Stopwatch.StartNew();double previous=clock.Elapsed.TotalMilliseconds,maxGap=0;
        heartbeat.Tick+=(s,e)=>{double now=clock.Elapsed.TotalMilliseconds;maxGap=Math.Max(maxGap,now-previous);previous=now;};heartbeat.Start();
        for(int i=0;i<20;i++){
            maxGap=0;previous=clock.Elapsed.TotalMilliseconds;
            Pump((Task)Invoke(form,"SwitchThemeAsync"));
            if(Theme.IsDark!=(i%2!=0))throw new Exception("Theme direction mismatch.");
            if(render.Handle!=rendererHandle)throw new Exception("Theme recreated VM native renderer.");
            AssertOverlayTransparent(form);
            intervals.Add(Theme.LastTransitionMs);frames.Add(Theme.LastFrameMaxMs);
            measurements.Add((i+1)+","+Theme.LastTransitionMs.ToString("F1")+","+Theme.LastFrameMaxMs.ToString("F1")+","+maxGap.ToString("F1"));
        }
        heartbeat.Stop();heartbeat.Dispose();
        File.WriteAllLines(Path.Combine(output,"theme-samples.csv"),measurements);
        if(store.Load().UiTheme!="Dark")throw new Exception("Theme preference not persisted.");
        foreach(var size in new[]{new Size(1366,768),new Size(1560,980)}){
            form.ClientSize=size;Application.DoEvents();
            foreach(string name in new[]{"_btnTheme","_btnPause","_btnResume","_btnImportFolder","_btnLoadSolution","_btnStop","_btnRuntime"}){
                var control=(Control)Field(form,name);if(!control.Visible||control.Parent==null)throw new Exception("Missing toolbar function "+name);
                if(control.Right>control.Parent.ClientSize.Width||control.Bottom>control.Parent.ClientSize.Height)throw new Exception("Clipped toolbar function "+name);
            }
        }
        using(var bitmap=BrandAssets.Render(256,false))bitmap.Save(Path.Combine(output,"logo-day.png"));
        using(var bitmap=BrandAssets.Render(256,true))bitmap.Save(Path.Combine(output,"logo-night.png"));
        string solution=Path.Combine(sdk,@"Development\V4.x\Samples\C#\PlatformSDKSampleCS\CircleFind\CircleFind.sol");
        string copy=Path.Combine(output,"CircleFind.sol");File.Copy(solution,copy);
        bool vmVerified=false;
        try {
        Invoke(form,"RegisterSolution",copy);Invoke(form,"SelectSolutionPath",copy);
        Pump((Task)Invoke(form,"LoadSelectedSolutionAsync",false));
        if(!app.Vision.IsLoaded||render.GetType().GetProperty("ModuleSource").GetValue(render)==null)throw new Exception("Main form did not bind the real VM solution.");
        string imagePath=Path.Combine(sdk,@"Development\V4.x\Samples\VC\PlatformSDKSampleVC\GetResultControl\image.jpg");
        var raw=RawImage.FromFile(imagePath);
        for(int i=0;i<4;i++){
            var run=app.Vision.RunOnceAsync(raw,app.Settings.Vision.PrimaryProcedureName,app.Settings.ResultJudge,CancellationToken.None);
            var switchTask=(Task)Invoke(form,"SwitchThemeAsync");
            Pump(Task.WhenAll(run,switchTask));
            if(run.Result==null||run.Result.Modules.Count==0)throw new Exception("VM result lost during theme transition.");
            AssertOverlayTransparent(form);
        }
        if(render.Handle!=rendererHandle)throw new Exception("Renderer recreated after VM run.");
        app.LocalImages.SetImages(new[]{imagePath});Invoke(form,"RefreshLocalImageList");
        Pump((Task)Invoke(form,"StartDetectionAsync",DetectionMode.Single));
        PumpUntil(()=>app.Statistics.Snapshot.Total>=1&&app.Detection.State==DetectionRunState.Idle,15000);
        Pump((Task)Invoke(form,"StartDetectionAsync",DetectionMode.Continuous));
        PumpUntil(()=>app.Statistics.Snapshot.Total>=3,15000);
        Pump((Task)Invoke(form,"SwitchThemeAsync"));
        Pump((Task)Invoke(form,"PauseDetectionAsync"));
        if(app.Detection.State!=DetectionRunState.Paused)throw new Exception("Pause command failed.");
        Pump((Task)Invoke(form,"SwitchThemeAsync"));
        Pump((Task)Invoke(form,"ResumeDetectionAsync"));
        if(app.Detection.State!=DetectionRunState.Running)throw new Exception("Resume command failed.");
        Pump((Task)Invoke(form,"StopDetectionAsync"));
        if(app.Detection.State!=DetectionRunState.Idle)throw new Exception("Stop command failed.");
        for(int mode=0;mode<2;mode++){
            if(Theme.IsDark!=(mode==0))Pump((Task)Invoke(form,"SwitchThemeAsync"));
            Snapshot(form,Path.Combine(output,mode==0?"night.png":"day.png"));
            AssertOverlayTransparent(form);
            var tabs=(TabControl)Field(form,"_tabRight");tabs.SelectedIndex=1;Pump(Task.Delay(120));
            AssertVisible(form,"_btnRefreshModules");AssertVisible(form,"_btnSaveSolution");AssertVisible(form,"_cboParamModule");
            if(Field(form,"_vmParams")==null)throw new Exception("Native VM parameter control missing.");
            Snapshot(form,Path.Combine(output,mode==0?"parameters-night.png":"parameters-day.png"));
            tabs.SelectedIndex=0;Pump(Task.Delay(120));
            using(var settingsForm=new SettingsForm(app.Settings,app.Auth))VerifyDialog(settingsForm,output,"settings");
            using(var reportForm=new ReportForm(app.Report,Path.Combine(output,"Reports")))VerifyDialog(reportForm,output,"report");
            using(var manager=new SolutionManagerForm(new[]{copy}))VerifyDialog(manager,output,"solutions");
            using(var login=new LoginForm(app.Auth,logger))VerifyDialog(login,output,"login");
        }
        vmVerified=true;
        } catch(Exception error) {
            if(!error.ToString().Contains("0xE0000700"))throw;
            File.WriteAllText(Path.Combine(output,"VM_BLOCKED.txt"),error.ToString());
            Console.WriteLine("BLOCKED VM execution: SDK 0xE0000700 (no dongle detected or detection exception).");
        }
        // 测试走真实关闭路径；无未保存配方、无生产设备连接。
        var closingTheme=(Task)Invoke(form,"SwitchThemeAsync");
        if(!ReferenceEquals(closingTheme,Invoke(form,"SwitchThemeAsync")))throw new Exception("Repeated theme click created a second transition.");
        form.Close();Pump(closingTheme);PumpUntil(()=>form.IsDisposed,20000);
        Console.WriteLine("PASS day/night 20 switches, preference persistence, stable native renderer handle");
        Console.WriteLine("PASS visible toolbar controls at 1366 and 1560 widths");
        Console.WriteLine("PASS dark startup contrast, repeated-click guard, close during transition");
        Console.WriteLine(vmVerified?"PASS real VM/render/parameters, single/continuous/pause/resume/stop, four dialogs in both themes, orderly shutdown":"PASS orderly shutdown; VM execution remains BLOCKED by authorization.");
        Console.WriteLine("TIMING transition avg="+intervals.Average().ToString("F1")+"ms max="+intervals.Max().ToString("F1")+"ms; palette-frame max="+frames.Max().ToString("F1")+"ms; heartbeat max-gap="+maxGap.ToString("F1")+"ms");
        File.WriteAllText(Path.Combine(output,"timing.txt"),"transition avg="+intervals.Average().ToString("F1")+"ms; max="+intervals.Max().ToString("F1")+"ms; palette-frame max="+frames.Max().ToString("F1")+"ms; final-switch heartbeat max-gap="+maxGap.ToString("F1")+"ms; see theme-samples.csv for all UI gaps");
        app.Dispose();Console.WriteLine("Artifacts: "+output);
    }
    private static object Field(object target,string name){return target.GetType().GetField(name,Flags).GetValue(target);}
    private static void AssertOverlayTransparent(Form form){var overlay=(Form)Field(form,"_crosshairOverlay");if(overlay==null||overlay.TransparencyKey.IsEmpty||overlay.BackColor.ToArgb()!=overlay.TransparencyKey.ToArgb())throw new Exception("VM image is hidden by an opaque overlay.");using(var b=new Bitmap(overlay.Width,overlay.Height)){overlay.DrawToBitmap(b,new Rectangle(Point.Empty,b.Size));if(b.GetPixel(5,5).ToArgb()!=overlay.TransparencyKey.ToArgb())throw new Exception("Overlay pixels no longer remain transparent.");}}
    private static object Invoke(object target,string name,params object[] args){SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());return target.GetType().GetMethod(name,Flags).Invoke(target,args);}
    private static void AssertVisible(Form form,string name){var c=(Control)Field(form,name);if(!c.Visible||c.Right>c.Parent.ClientSize.Width||c.Bottom>c.Parent.ClientSize.Height)throw new Exception("Hidden or clipped control: "+name);}
    private static void VerifyDialog(Form form,string output,string name){form.Opacity=0;form.ShowInTaskbar=false;form.Show();Application.DoEvents();Snapshot(form,Path.Combine(output,name+(Theme.IsDark?"-night.png":"-day.png")));form.Close();}
    private static void Snapshot(Form form,string path){using(var b=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(b,new Rectangle(Point.Empty,b.Size));b.Save(path);}}
    private static void Pump(Task task){PumpUntil(()=>task.IsCompleted,60000);task.GetAwaiter().GetResult();}
    private static void PumpUntil(Func<bool> condition,int timeout){var watch=Stopwatch.StartNew();while(!condition()){Application.DoEvents();Thread.Sleep(2);if(watch.ElapsedMilliseconds>timeout)throw new TimeoutException("UI operation timeout.");}Application.DoEvents();}
}
