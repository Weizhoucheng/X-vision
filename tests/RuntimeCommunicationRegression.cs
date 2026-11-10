using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using VisionInspect.App.Bootstrap;
using VisionInspect.App.Runtime;
using VisionInspect.App.UI;
using VisionInspect.Communication;
using VisionInspect.Core;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Configuration;
using VisionInspect.Core.Models;
using VisionInspect.Core.Services;
using VisionInspect.Infrastructure.Configuration;
using VisionInspect.Infrastructure.Logging;
using VisionInspect.Infrastructure.Paths;

internal static class RuntimeCommunicationRegression
{
    static readonly CancellationToken None = CancellationToken.None;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static async Task Wait(Func<bool> condition) { for(int i=0;i<400;i++){if(condition())return;await Task.Delay(10);}throw new TimeoutException("Condition timed out"); }
    static async Task<T> Bounded<T>(Task<T> task) { if(await Task.WhenAny(task,Task.Delay(5000))!=task)throw new TimeoutException("Network test timed out");return await task; }
    [STAThread] static int Main(string[] args)
    {
        try { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); VisionMasterEnvironment.Prepare(new AppSettings { VisionMasterInstallDir=args[0] },null); Run(args[1]); return 0; }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void Run(string output)
    {
        Modbus().GetAwaiter().GetResult();
        Tcp().GetAwaiter().GetResult();
        RuntimeSettings(output).GetAwaiter().GetResult();
        Preview(output);
        Console.WriteLine("PASS runtime communication/settings integration suite");
    }
    static async Task Modbus()
    {
        using(var first=new Plc())using(var second=new Plc())using(var hub=new CommunicationHub(new AppSettings(),null)){
            var settings=new CommunicationSettings(); settings.ModbusTcp.Enabled=true;settings.ModbusTcp.Host="127.0.0.1";settings.ModbusTcp.Port=first.Port;settings.ModbusTcp.TimeoutMs=500;
            await hub.ReconfigureAsync(settings,None);await Wait(()=>hub.GetStatus(CommunicationChannel.ModbusTcp).State==DeviceConnectionState.Connected);
            Check(first.Accepted>0&&first.Requests==0,"Modbus must connect without a detection trigger or register write");
            var values=await hub.ReadRegistersAsync(1,100,2,None);Check(values.SequenceEqual(new ushort[]{107,108}),"FC03 response decoding");
            await hub.WriteRegistersAsync(1,100,new ushort[]{12,34},None);Check(first.LastWrite.SequenceEqual(new ushort[]{12,34}),"FC16 manual values");
            settings.ModbusTcp.Port=second.Port;await hub.ReconfigureAsync(settings,None);await Wait(()=>second.Accepted>0&&hub.GetStatus(CommunicationChannel.ModbusTcp).State==DeviceConnectionState.Connected);
            await hub.ReadRegistersAsync(1,200,1,None);Check(second.Requests==1,"Live port change must route IO to the new PLC");
            await hub.DisconnectAsync(CommunicationChannel.ModbusTcp);int count=second.Accepted;await Task.Delay(1100);Check(second.Accepted==count&&hub.GetStatus(CommunicationChannel.ModbusTcp).State==DeviceConnectionState.Disconnected,"Manual disconnect must cancel auto reconnect");
            await hub.ConnectAsync(CommunicationChannel.ModbusTcp,None);await Wait(()=>second.Accepted>count);await hub.StopAsync();
        }
        Console.WriteLine("PASS Modbus active connect without trigger / FC03 / FC16 / live endpoint change / manual disconnect and reconnect");
    }
    static async Task Tcp()
    {
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;
        try { using(var hub=new CommunicationHub(new AppSettings(),null)){
            int received=0,triggers=0;hub.DataTransferred+=(s,e)=>{if(e.IsReceive)Interlocked.Increment(ref received);};hub.Triggered+=(s,e)=>Interlocked.Increment(ref triggers);
            var settings=new CommunicationSettings();var tcp=settings.TcpTrigger;tcp.Enabled=true;tcp.Mode="Client";tcp.RemoteHost="127.0.0.1";tcp.RemotePort=port;tcp.EnableReceiveTrigger=false;tcp.SendResults=true;
            Task<TcpClient> accept=listener.AcceptTcpClientAsync();await hub.ReconfigureAsync(settings,None);
            using(var peer=await Bounded(accept)){
                await Wait(()=>hub.GetStatus(CommunicationChannel.TcpTrigger).State==DeviceConnectionState.Connected);
                var stream=peer.GetStream();var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HELLO\r\nTRIG\n"),0,12);await Wait(()=>received>0);Check(triggers==0,"Data receive must be independent of detection trigger");
                await hub.SendAsync(CommunicationChannel.TcpTrigger,Encoding.ASCII.GetBytes("PING\r\n"),None);Check(await Bounded(reader.ReadLineAsync())=="PING","TCP client manual TX");
                await hub.PublishResultAsync(Context(),None);Check((await Bounded(reader.ReadLineAsync())).StartsWith("OK,7,"),"TCP result template output");
                settings.Serial.Enabled=true;settings.Serial.PortName="COM99999";settings.Serial.AutoReconnect=false;
                await hub.ReconfigureAsync(settings,None);await Wait(()=>hub.GetStatus(CommunicationChannel.Serial).State==DeviceConnectionState.Fault);
                await hub.SendAsync(CommunicationChannel.TcpTrigger,Encoding.ASCII.GetBytes("STILL\n"),None);Check(await Bounded(reader.ReadLineAsync())=="STILL","Serial failure must not reconnect or interrupt healthy TCP");
                settings.Serial.Enabled=false;tcp.EnableReceiveTrigger=true;tcp.TriggerCommand="GO";tcp.AckCommand="RECEIVED";
                accept=listener.AcceptTcpClientAsync();await hub.ReconfigureAsync(settings,None);
                using(var replacement=await Bounded(accept)){
                    await Wait(()=>hub.GetStatus(CommunicationChannel.TcpTrigger).State==DeviceConnectionState.Connected);
                    var data=Encoding.ASCII.GetBytes("TRIG\nGO\n");await replacement.GetStream().WriteAsync(data,0,data.Length);
                    var incoming=new StreamReader(replacement.GetStream());Check(await Bounded(incoming.ReadLineAsync())=="RECEIVED","New trigger/ACK config must apply without restart");
                    await Wait(()=>triggers==1);Check(triggers==1,"Old trigger must no longer be accepted");
                }
            }
            tcp.Mode="Server";tcp.BindAddress="127.0.0.1";tcp.ListenPort=FreePort();await hub.ReconfigureAsync(settings,None);
            Check(hub.GetStatus(CommunicationChannel.TcpTrigger).State==DeviceConnectionState.Listening,"Listening is not the same as a connected peer");
            using(var peer=new TcpClient()){await peer.ConnectAsync("127.0.0.1",tcp.ListenPort);await Wait(()=>hub.GetStatus(CommunicationChannel.TcpTrigger).State==DeviceConnectionState.Connected);await hub.SendAsync(CommunicationChannel.TcpTrigger,Encoding.ASCII.GetBytes("SERVER\n"),None);Check(await Bounded(new StreamReader(peer.GetStream()).ReadLineAsync())=="SERVER","TCP server TX");}
            await Wait(()=>hub.GetStatus(CommunicationChannel.TcpTrigger).State==DeviceConnectionState.Listening);await hub.StopAsync();
            var probe=new TcpListener(IPAddress.Loopback,tcp.ListenPort);probe.Start();probe.Stop();
        }}finally{listener.Stop();}
        Console.WriteLine("PASS TCP active client / unsolicited RX / manual TX / results / per-channel isolation / live trigger change / server state and port release");
    }
    static int FreePort(){var l=new TcpListener(IPAddress.Loopback,0);l.Start();int p=((IPEndPoint)l.LocalEndpoint).Port;l.Stop();return p;}
    static DetectionContext Context(){return new DetectionContext{Sequence=7,Timestamp=DateTime.Now,Image=new RawImage(new byte[]{128},1,1,ImagePixelFormat.Mono8),Inspection=new VisionInspectionResult{IsOk=true,DurationMs=12.5},Record=new DetectionRecord{Sequence=7,Timestamp=DateTime.Now,IsOk=true}};}
    static async Task RuntimeSettings(string output)
    {
        var paths=AppPaths.Create(Path.Combine(output,"runtime-data"));var cfg=new AppSettings();cfg.General.AutoStart=false;
        var store=new SwitchableStore(new JsonSettingsStore(paths,null));store.Save(cfg);
        var log=new FileAppLogger(paths.SystemLogDirectory,paths.AuditLogDirectory,7,false);
        using(var app=new AppBootstrapper(paths,cfg,store,log,false)){
            Check(app.Auth.Login("admin","admin").Success,"Isolated admin login");
            var original=app.Settings;var detection=app.Settings.Detection;var judge=(ResultJudgeOptions)typeof(DetectionController).GetField("_judgeOptions",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(app.Detection);
            var next=JsonHelper.Deserialize<AppSettings>(JsonHelper.Serialize(cfg));next.LineName="LIVE_LINE";next.Report.CompanyName="LIVE_COMPANY";next.Storage.DataRoot=Path.Combine(output,"images-a");next.Storage.EnableImageArchive=true;next.Storage.ArchiveNgOnly=false;next.Storage.RetentionDays=7;next.ResultJudge.JudgeOutputName="status";next.ResultJudge.OkKeyword="PASS";next.ResultJudge.UseFailSafeScan=false;next.ResultJudge.UseProcedureStatusAsFallback=false;next.Detection.IntervalMs=37;
            await app.ApplySettingsAsync(next);Check(ReferenceEquals(original,app.Settings)&&ReferenceEquals(detection,app.Settings.Detection)&&detection.IntervalMs==37,"Existing services must retain updated settings references");
            Check(ResultJudge.Evaluate(new List<VisionOutputValue>{new VisionOutputValue{Name="status",DisplayValue="PASS"}},false,judge).IsOk,"Controller must use the new judgement rules immediately");
            var first=Context();await app.ResultPipeline.HandleAsync(first,None);await app.Archive.FlushAsync(None);Check(File.Exists(first.Record.ImagePath)&&Path.GetExtension(first.Record.ImagePath)==".png","New PNG archive config");
            next=JsonHelper.Deserialize<AppSettings>(JsonHelper.Serialize(app.Settings));next.Storage.DataRoot=Path.Combine(output,"images-b");next.Storage.ArchiveFormat="bmp";next.Report.CompanyName="LIVE_COMPANY_2";
            await app.ApplySettingsAsync(next);var second=Context();await app.ResultPipeline.HandleAsync(second,None);await app.Archive.FlushAsync(None);Check(File.Exists(second.Record.ImagePath)&&second.Record.ImagePath.StartsWith(next.Storage.DataRoot)&&Path.GetExtension(second.Record.ImagePath)==".bmp"&&File.Exists(first.Record.ImagePath),"Live archive path/format change must preserve old files");
            Check(app.DetectionLog.Query(DateTime.Today,DateTime.Today.AddDays(1)).Count==1,"Log repository must switch to the new root");
            string report=await app.Report.ExportAsync(DateTime.Today,DateTime.Today.AddDays(1),Path.Combine(app.ReportDirectory,"live.xlsx"),None);
            using(var zip=ZipFile.OpenRead(report)){string xml=string.Join("",zip.Entries.Where(e=>e.FullName.EndsWith(".xml")).Select(e=>{using(var reader=new StreamReader(e.Open()))return reader.ReadToEnd();}));Check(xml.Contains("LIVE_COMPANY_2")&&xml.Contains("LIVE_LINE"),"Report must use new company/line settings");}
            string previous=File.ReadAllText(paths.SettingsFile);var archive=app.Archive;
            next=JsonHelper.Deserialize<AppSettings>(JsonHelper.Serialize(app.Settings));string blocker=Path.Combine(output,"not-a-directory");File.WriteAllText(blocker,"x");next.Storage.DataRoot=blocker;bool rejected=false;try{await app.ApplySettingsAsync(next);}catch(IOException){rejected=true;}
            Check(rejected&&ReferenceEquals(archive,app.Archive)&&File.ReadAllText(paths.SettingsFile)==previous,"Invalid directory must leave the active config intact");
            DateTime witnessDate=DateTime.Today.AddDays(-3);string witness=Path.Combine(app.DataRoot,"images",witnessDate.ToString("yyyy"),witnessDate.ToString("MM"),witnessDate.ToString("dd"),"keep.txt");Directory.CreateDirectory(Path.GetDirectoryName(witness));File.WriteAllText(witness,"keep");
            next=JsonHelper.Deserialize<AppSettings>(JsonHelper.Serialize(app.Settings));next.Storage.RetentionDays=1;next.Communication.TcpTrigger.Enabled=true;next.Communication.TcpTrigger.BindAddress="127.0.0.1";next.Communication.TcpTrigger.ListenPort=FreePort();store.Fail=true;rejected=false;try{await app.ApplySettingsAsync(next);}catch(IOException){rejected=true;}store.Fail=false;
            Check(rejected&&File.Exists(witness)&&File.ReadAllText(paths.SettingsFile)==previous&&!app.Settings.Communication.TcpTrigger.Enabled,"Save failure must roll back live communications and avoid retention deletion");
            var probe=new TcpListener(IPAddress.Loopback,next.Communication.TcpTrigger.ListenPort);probe.Start();probe.Stop();
            next=JsonHelper.Deserialize<AppSettings>(JsonHelper.Serialize(app.Settings));next.Storage.EnableDetectionLog=false;await app.ApplySettingsAsync(next);await app.ResultPipeline.HandleAsync(Context(),None);Check(app.DetectionLog.Query(DateTime.Today,DateTime.Today.AddDays(1)).Count==1,"Logging switch must apply immediately");
            await app.ShutdownAsync();
        }
        Console.WriteLine("PASS hot settings references / judgement / real archives / directories / report metadata / logging / rollback and retention safety / shutdown without VM authorization");
    }
    static void Preview(string output)
    {
        var paths=AppPaths.Create(Path.Combine(output,"preview-data"));var cfg=new AppSettings();var log=new FileAppLogger(paths.SystemLogDirectory,paths.AuditLogDirectory,7,false);var store=new JsonSettingsStore(paths,null);
        using(var app=new AppBootstrapper(paths,cfg,store,log,false)){
            app.Auth.Login("admin","admin");
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();TcpClient peer=null;
            try {
                foreach(bool dark in new[]{false,true}){
                    Theme.SetDark(dark);
                    using(var form=new SettingsForm(cfg,app.Auth,app,true){Opacity=0,ShowInTaskbar=false}){
                        form.Show();Application.DoEvents();
                        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                        if(!dark){
                            ((CheckBox)typeof(SettingsForm).GetField("_chkTcpTriggerEnabled",flags).GetValue(form)).Checked=true;
                            ((ComboBox)typeof(SettingsForm).GetField("_cboTcpMode",flags).GetValue(form)).SelectedIndex=0;
                            ((TextBox)typeof(SettingsForm).GetField("_txtTcpRemote",flags).GetValue(form)).Text="127.0.0.1";
                            ((NumericUpDown)typeof(SettingsForm).GetField("_numTcpRemote",flags).GetValue(form)).Value=((IPEndPoint)listener.LocalEndpoint).Port;
                            var connecting=listener.AcceptTcpClientAsync();
                            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                            var applying=(Task<bool>)typeof(SettingsForm).GetMethod("ApplyEditorAsync",flags).Invoke(form,new object[]{false});Pump(applying);
                            Check(applying.Result&&form.Applied,"Real settings form must apply the edits without closing or restart");
                            Pump(connecting);peer=connecting.Result;
                            byte[] greeting=Encoding.ASCII.GetBytes("UI_CONNECTED\r\n");peer.GetStream().Write(greeting,0,greeting.Length);
                            Pump(Task.Delay(180));
                        }
                        var tabs=(TabControl)typeof(SettingsForm).GetField("_communicationTabs",flags).GetValue(form);
                        for(int tab=0;tab<3;tab++){
                            tabs.SelectedIndex=tab;Application.DoEvents();
                            using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(output,"communication-"+tab+(dark?"-night.png":"-day.png")));}
                        }
                        form.Close();
                    }
                }
            } finally {peer?.Close();listener.Stop();}
        }
        Console.WriteLine("PASS real dialog applies TCP configuration without restart; all three protocol pages render in both themes");
    }
    static void Pump(Task task){var timer=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted){Application.DoEvents();Thread.Sleep(2);if(timer.ElapsedMilliseconds>15000)throw new TimeoutException("UI apply timeout");}task.GetAwaiter().GetResult();Application.DoEvents();}
    sealed class SwitchableStore:ISettingsStore
    {
        readonly ISettingsStore inner;public bool Fail;public SwitchableStore(ISettingsStore value){inner=value;}public string SettingsFilePath{get{return inner.SettingsFilePath;}}public AppSettings Load(){return inner.Load();}public void Save(AppSettings value){if(Fail)throw new IOException("Test store failure");inner.Save(value);}
    }
    sealed class Plc:IDisposable
    {
        readonly TcpListener listener=new TcpListener(IPAddress.Loopback,0);readonly ConcurrentBag<TcpClient> clients=new ConcurrentBag<TcpClient>();readonly CancellationTokenSource stop=new CancellationTokenSource();readonly Task accept;public int Accepted,Requests;public ushort[] LastWrite;public int Port{get{return((IPEndPoint)listener.LocalEndpoint).Port;}}
        public Plc(){listener.Start();accept=Task.Run(async()=>{while(!stop.IsCancellationRequested){try{var c=await listener.AcceptTcpClientAsync();clients.Add(c);Interlocked.Increment(ref Accepted);_=Serve(c);}catch when(stop.IsCancellationRequested){break;}}});}
        async Task Serve(TcpClient client){try{using(client){var stream=client.GetStream();while(!stop.IsCancellationRequested){byte[] header=new byte[7];if(!await Read(stream,header))break;int len=(header[4]<<8)|header[5];byte[] pdu=new byte[len-1];if(!await Read(stream,pdu))break;Interlocked.Increment(ref Requests);int address=(pdu[1]<<8)|pdu[2],count=(pdu[3]<<8)|pdu[4];byte[] reply;if(pdu[0]==3){reply=new byte[9+count*2];Buffer.BlockCopy(header,0,reply,0,7);reply[4]=(byte)((3+count*2)>>8);reply[5]=(byte)(3+count*2);reply[7]=3;reply[8]=(byte)(count*2);for(int i=0;i<count;i++){int value=address+i+7;reply[9+i*2]=(byte)(value>>8);reply[10+i*2]=(byte)value;}}else{LastWrite=new ushort[count];for(int i=0;i<count;i++)LastWrite[i]=(ushort)((pdu[6+i*2]<<8)|pdu[7+i*2]);reply=new byte[12];Buffer.BlockCopy(header,0,reply,0,7);reply[4]=0;reply[5]=6;Buffer.BlockCopy(pdu,0,reply,7,5);}await stream.WriteAsync(reply,0,reply.Length);}}}catch(IOException){}catch(ObjectDisposedException){}}
        static async Task<bool> Read(Stream stream,byte[] buffer){int n=0;while(n<buffer.Length){int read=await stream.ReadAsync(buffer,n,buffer.Length-n);if(read==0)return false;n+=read;}return true;}
        public void Dispose(){stop.Cancel();listener.Stop();foreach(var c in clients)c.Close();try{accept.GetAwaiter().GetResult();}catch{}stop.Dispose();}
    }
}
