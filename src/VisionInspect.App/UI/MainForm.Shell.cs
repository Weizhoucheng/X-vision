using System;
using System.Drawing;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using VisionInspect.App.UI.Controls;

namespace VisionInspect.App.UI
{
    public sealed partial class MainForm
    {
        private Button _btnTheme;
        private Button _btnCommunication;
        private Button _btnRuntime;
        private ToolTip _shellTips;
        private bool _themeChanging;
        private Task _themeSwitchTask = Task.CompletedTask;
        private void BuildTopBar()
        {
            _shellTips=new ToolTip{ShowAlways=true,InitialDelay=400,ReshowDelay=200};
            Disposed+=(s,e)=>_shellTips.Dispose();
            _pnlTopBar=new Panel{Dock=DockStyle.Top,Height=172,BackColor=Theme.Surface,Padding=new Padding(12,0,12,6)};
            var title=new Panel{Dock=DockStyle.Top,Height=72};
            _pnlBrand=new Panel{Dock=DockStyle.Left,Width=290,BackColor=Theme.Surface};
            var brandLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Padding=new Padding(0,4,0,4),Margin=Padding.Empty};
            brandLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,54));
            brandLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            brandLayout.RowStyles.Add(new RowStyle(SizeType.Percent,60));
            brandLayout.RowStyles.Add(new RowStyle(SizeType.Percent,40));
            _picBrandLogo=new PictureBox{Dock=DockStyle.Fill,Margin=new Padding(0,3,6,3),SizeMode=PictureBoxSizeMode.Zoom,Image=BrandAssets.LogoForTheme(Theme.IsDark),BackColor=Color.Transparent};
            _lblAppTitle=new Label{Text="X VISION",Dock=DockStyle.Fill,Margin=Padding.Empty,TextAlign=ContentAlignment.BottomLeft,Font=new Font("Segoe UI",18,FontStyle.Bold),ForeColor=Theme.Text};
            _lblProductName=new Label{Text="工业视觉检测平台",Dock=DockStyle.Fill,Margin=new Padding(1,1,0,0),TextAlign=ContentAlignment.TopLeft,Font=Theme.SmallFont,ForeColor=Theme.TextMuted};
            brandLayout.Controls.Add(_picBrandLogo,0,0);brandLayout.SetRowSpan(_picBrandLogo,2);
            brandLayout.Controls.Add(_lblAppTitle,1,0);brandLayout.Controls.Add(_lblProductName,1,1);
            _pnlBrand.Controls.Add(brandLayout);title.Controls.Add(_pnlBrand);
            var system=new FlowLayoutPanel{Dock=DockStyle.Right,AutoSize=true,WrapContents=false,Padding=new Padding(0,19,0,0)};
            _roleBadge=new RoleBadge{Text="未登录",Margin=new Padding(0,5,10,0)};
            _btnTheme=Theme.MakeButton(string.Empty,null,Theme.ButtonKind.Ghost,44);
            UpdateThemeButton();
            _btnTheme.Click+=async(s,e)=>await SwitchThemeAsync();
            _btnSettings=Theme.MakeButton("设置",null,Theme.ButtonKind.Ghost,82);
            _btnCommunication=Theme.MakeButton("通讯",null,Theme.ButtonKind.Ghost,88);
            _btnCommunication.Click+=async(s,e)=>await ShowSettingsAsync(true);
            _btnRuntime=Theme.MakeButton("运行环境",null,Theme.ButtonKind.Ghost,116);
            _btnRuntime.Click+=(s,e)=>{using(var dialog=new RuntimeEnvironmentForm(_app.Settings.VisionMasterInstallDir))dialog.ShowDialog(this);};
            _btnExportReport=Theme.MakeButton("检测报表",null,Theme.ButtonKind.Ghost,114);
            _btnLoginLogout=Theme.MakeButton("登录",null,Theme.ButtonKind.Ghost,86);
            system.Controls.AddRange(new Control[]{_roleBadge,_btnTheme,_btnCommunication,_btnRuntime,_btnExportReport,_btnSettings,_btnLoginLogout});title.Controls.Add(system);
            var project=new FlowLayoutPanel{Dock=DockStyle.Top,Height=46,WrapContents=false,Padding=new Padding(2,4,0,2)};
            project.Controls.Add(new Label{Text="视觉方案",AutoSize=true,Margin=new Padding(2,8,12,0),ForeColor=Theme.TextMuted,Font=Theme.SmallFont});
            _cboSolution=new VisionInspect.App.UI.Controls.StudioComboBox{Width=250,DropDownStyle=ComboBoxStyle.DropDownList,Margin=new Padding(0,3,10,0)};Theme.StyleCombo(_cboSolution);
            _btnLoadSolution=Theme.MakeButton("加载方案",null,Theme.ButtonKind.Primary,128);
            _btnManageSolutions=Theme.MakeButton("方案管理",null,Theme.ButtonKind.Ghost,112);
            _btnImportFiles=Theme.MakeButton("导入单张图片",null,Theme.ButtonKind.Ghost,140);
            _btnImportFolder=Theme.MakeButton("导入整个文件夹",null,Theme.ButtonKind.Ghost,156);
            _shellTips.SetToolTip(_btnImportFiles,"选择并导入一张检测图片");
            _shellTips.SetToolTip(_btnImportFolder,"导入所选文件夹中的全部检测图片");
            _cameraSelector=new CameraSelector{Text="工业相机",Width=146,Margin=new Padding(8,0,0,0)};
            project.Controls.AddRange(new Control[]{_cboSolution,_btnLoadSolution,_btnManageSolutions,_btnImportFiles,_btnImportFolder,_cameraSelector});
            var run=new Panel{Dock=DockStyle.Top,Height=48,Padding=new Padding(2,5,0,2)};
            var commands=new FlowLayoutPanel{Dock=DockStyle.Left,AutoSize=true,WrapContents=false};
            _segDetectionMode=new SegmentedControl("单次检测","连续检测"){Width=194,Height=34,Margin=new Padding(0,0,12,0)};
            _btnStart=Theme.MakeButton("启动 F1",null,Theme.ButtonKind.Primary,112);
            _btnPause=Theme.MakeButton("暂停",null,Theme.ButtonKind.Ghost,82);
            _btnResume=Theme.MakeButton("继续",null,Theme.ButtonKind.Ghost,82);
            _btnStop=Theme.MakeButton("停止 F3",null,Theme.ButtonKind.Danger,112);
            _btnResetStats=Theme.MakeButton("统计清零",null,Theme.ButtonKind.Ghost,112);
            commands.Controls.AddRange(new Control[]{_segDetectionMode,_btnStart,_btnPause,_btnResume,_btnStop,_btnResetStats});
            _lblMessage=new Label{Dock=DockStyle.Fill,Text="就绪",AutoEllipsis=true,TextAlign=ContentAlignment.MiddleLeft,Font=Theme.SmallFont,ForeColor=Theme.TextMuted,Padding=new Padding(14,0,4,0)};
            run.Controls.Add(_lblMessage);run.Controls.Add(commands);_lblMessage.BringToFront();
            _pnlTopBar.Controls.Add(run);_pnlTopBar.Controls.Add(project);_pnlTopBar.Controls.Add(title);
            _pnlTopBar.Paint+=(s,e)=>{using(var pen=new Pen(Theme.Border))e.Graphics.DrawLine(pen,0,_pnlTopBar.Height-1,_pnlTopBar.Width,_pnlTopBar.Height-1);};
            Controls.Add(_pnlTopBar);
        }
        private Task SwitchThemeAsync()
        {
            if(_themeChanging||_shutdownStarted)return _themeSwitchTask;
            return _themeSwitchTask = ApplyThemeAsync();
        }
        private void UpdateThemeButton()
        {
            ((RoundedButton)_btnTheme).Icon=Theme.IsDark?UiIcon.Sun:UiIcon.Moon;
            _btnTheme.AccessibleName=Theme.IsDark?"切换到白天模式":"切换到夜间模式";
            _shellTips.SetToolTip(_btnTheme,_btnTheme.AccessibleName);
            _btnTheme.Invalidate();
        }
        private async Task ApplyThemeAsync()
        {
            _themeChanging=true;_btnTheme.Enabled=false;
            try {
                bool dark=!Theme.IsDark;
                await Theme.TransitionAsync(dark);
                // 颜色已经在过渡中更新；这里避免重复设置布局、原生主题和字体。
                UpdateCommunicationStatusLabels();
                _picBrandLogo.Image=BrandAssets.LogoForTheme(dark);
                UpdateThemeButton();
                Theme.ApplyContextMenu(_imageMenu);
                _app.Settings.UiTheme=dark?"Dark":"Light";
                await Task.Run(()=>_app.SaveSettings());
                _app.Logger.Debug("UI","主题过渡 "+Theme.LastTransitionMs.ToString("F1")+" ms，最大配色帧 "+Theme.LastFrameMaxMs.ToString("F1")+" ms");
            } catch(Exception error){_app.Logger.Error("UI","主题切换失败",error);}
            finally{_themeChanging=false;if(!_btnTheme.IsDisposed)_btnTheme.Enabled=true;}
        }
    }
}
