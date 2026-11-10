using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using VisionInspect.VisionMaster.Runtime;

namespace VisionInspect.App.UI
{
    public sealed class RuntimeEnvironmentForm : Form
    {
        private readonly string _configuredRoot;
        private readonly TextBox _details;
        private readonly Button _refresh, _activate, _export;
        private VmRuntimeReport _report;
        public RuntimeEnvironmentForm(string configuredRoot)
        {
            _configuredRoot=configuredRoot;Theme.StyleForm(this);
            Text="X Vision 运行环境与授权";ClientSize=new Size(830,560);MinimumSize=new Size(760,500);StartPosition=FormStartPosition.CenterParent;
            var title=new Label{Dock=DockStyle.Top,Height=52,Text="VM 运行环境与授权",Font=Theme.TitleFont,ForeColor=Theme.Text,Padding=new Padding(18,10,0,0)};
            var note=new Label{Dock=DockStyle.Top,Height=68,Text="不插 USB 加密狗运行，需要官方支持的软授权或网络授权。\r\n二次开发 SDK 与驱动负责接口和运行环境，授权有效性以实际加载及运行结果为准。",ForeColor=Theme.TextMuted,Padding=new Padding(18,8,12,0)};
            _details=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,Font=Theme.MonoFont,BackColor=Theme.Field,ForeColor=Theme.Text,BorderStyle=BorderStyle.FixedSingle};
            var bottom=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=56,Padding=new Padding(12,10,0,0),WrapContents=false};
            _refresh=Theme.MakeButton("重新检查",null,Theme.ButtonKind.Ghost,110);_activate=Theme.MakeButton("官方软授权工具",null,Theme.ButtonKind.Primary,180);_export=Theme.MakeButton("导出诊断",null,Theme.ButtonKind.Ghost,120);
            var close=Theme.MakeButton("关闭",null,Theme.ButtonKind.Ghost,90);close.Click+=(s,e)=>Close();
            _refresh.Click+=async(s,e)=>await RefreshAsync();_activate.Click+=(s,e)=>OpenActivationTool();_export.Click+=(s,e)=>Export();
            bottom.Controls.AddRange(new Control[]{_refresh,_activate,_export,close});
            Controls.Add(_details);Controls.Add(bottom);Controls.Add(note);Controls.Add(title);
            Shown+=async(s,e)=>await RefreshAsync();
        }
        private async Task RefreshAsync()
        {
            _refresh.Enabled=_activate.Enabled=_export.Enabled=false;
            try {
                var report=await Task.Run(()=>VmRuntimeDiagnostics.Inspect(_configuredRoot));if(IsDisposed)return;_report=report;
                var text=new StringBuilder();
                text.AppendLine("检查时间："+report.CheckedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                text.AppendLine("运行库文件："+(report.FilesReady?"已找到所需文件":"未就绪"));
                text.AppendLine("当前授权方式："+(report.AuthorizationModeName??"未确认"));
                text.AppendLine("授权运行服务："+report.AuthorizationService);
                text.AppendLine("授权状态："+report.AuthorizationStatus);
                text.AppendLine("VM 安装目录："+report.InstallRoot);text.AppendLine("SDK 目录："+report.SdkDirectory);
                foreach(var item in report.MissingFiles)text.AppendLine("缺少："+item);
                foreach(var item in report.Warnings)text.AppendLine("说明："+item);
                text.AppendLine();text.AppendLine("无 USB 加密狗的交付步骤：");
                text.AppendLine("1. 向海康确认现有许可的迁移方式，取得覆盖当前 SDK 和算法的软授权。");
                text.AppendLine("2. 使用官方工具在线激活，或按本机 C2D / D2C 流程离线激活。");
                text.AppendLine("3. 依官方安装配置选择对应授权方式，重新启动 VM 运行时与 X Vision。");
                text.AppendLine("4. 在不插 USB 加密狗条件下，实际加载 .sol 并运行算法完成验收。");
                text.AppendLine();text.AppendLine("本页面只读检查配置，不会修改 VM 全局授权方式或激活许可。");
                _details.Text=text.ToString();_activate.Enabled=!string.IsNullOrEmpty(report.ActivationTool);_export.Enabled=true;
            }catch(Exception ex){if(!IsDisposed)_details.Text="检查失败："+ex.Message;}
            finally{if(!IsDisposed)_refresh.Enabled=true;}
        }
        private void OpenActivationTool()
        {
            try { if(_report==null||string.IsNullOrEmpty(_report.ActivationTool)||!File.Exists(_report.ActivationTool))throw new FileNotFoundException("未找到官方激活工具。");Process.Start(new ProcessStartInfo(_report.ActivationTool){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(_report.ActivationTool)}); }
            catch(Exception ex){MessageBox.Show(this,ex.Message,"打开工具失败",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        }
        private void Export()
        {
            using(var dialog=new SaveFileDialog{Filter="诊断 JSON|*.json",FileName="XVision-runtime-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json"}){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                try{File.WriteAllText(dialog.FileName,new JavaScriptSerializer().Serialize(_report),Encoding.UTF8);}catch(Exception ex){MessageBox.Show(this,ex.Message,"导出失败");}
            }
        }
    }
}
