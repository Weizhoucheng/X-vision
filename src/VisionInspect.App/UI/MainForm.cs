using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using VisionInspect.App.Bootstrap;
using VisionInspect.App.UI.Controls;
using VisionInspect.Cameras;
using VisionInspect.Core;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Algorithms;
using VisionInspect.Core.Configuration;
using VisionInspect.Core.Models;
using VisionInspect.VisionMaster.Interop;

namespace VisionInspect.App.UI
{
    /// <summary>
    /// 主界面协调器。UI 只负责展示与命令转发，检测状态机、视觉执行、存储和通讯均下沉到服务层。
    /// 后台事件统一经 RunOnUi 切回 UI 线程。
    /// </summary>
    public sealed partial class MainForm : Form
    {
        private sealed class SolutionOption
        {
            public string Path;
            public string Display;
            public override string ToString() { return Display; }
        }

        private sealed class ModuleOption
        {
            public VisionModuleInfo Module;
            public override string ToString()
            {
                return Module == null ? string.Empty : Module.ProcedureName + " / " + Module.DisplayName;
            }
        }

        private sealed class RenderOption
        {
            public string FullName;
            public string Display;
            public bool IsProcedure;
            public override string ToString() { return Display; }
        }

        private readonly AppBootstrapper _app;
        private readonly Dictionary<CommunicationChannel, DeviceConnectionState> _commStates =
            new Dictionary<CommunicationChannel, DeviceConnectionState>();

        private VMControls.Winform.Release.VmRenderControl _vmRender;
        private VMControls.Winform.Release.VmParamsConfigControl _vmParams;
        private ContextMenuStrip _imageMenu;
        private VisionSolutionInfo _solution;
        private InputMode _inputMode;
        private CameraDeviceInfo _selectedCamera;
        private bool _isAdmin;
        private bool _busy;
        private bool _solutionLoading;
        private bool _shutdownStarted;
        private bool _shutdownComplete;
        private bool _initialShown;
        private bool _triggerStarting;
        private bool _updatingSourceTab;
        private bool _updatingRenderSource;
        private int _previewUpdating;
        private int _lastRightTabIndex;
        private long _lastPreviewTick;

        public MainForm(AppBootstrapper app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            InitializeLayout();
            InitializeVisionControls();
            ApplySettingsToUi();
            WireEvents();
            if (Theme.IsDark)
            {
                ApplyShellSurfaceColors();
            }
            Theme.AssignControlNames(this);
            Shown += (s, e) => ApplyShellSurfaceColors();
            _lastRightTabIndex = _tabRight.SelectedIndex;

            _commStates[CommunicationChannel.ModbusTcp] = _app.Communication.GetStatus(CommunicationChannel.ModbusTcp).State;
            _commStates[CommunicationChannel.Serial] = _app.Communication.GetStatus(CommunicationChannel.Serial).State;
            _commStates[CommunicationChannel.TcpTrigger] = _app.Communication.GetStatus(CommunicationChannel.TcpTrigger).State;

            RefreshUserUi();
            SetMessage("就绪。请加载视觉方案后开始检测。", Theme.TextMuted);
            UpdateStatusBar();
        }

        private void InitializeVisionControls()
        {
            try
            {
                _vmRender = new VMControls.Winform.Release.VmRenderControl
                {
                    Dock = DockStyle.Fill,
                    CoordinateInfoVisible = true,
                    BackColor = Color.Black
                };
                try { _vmRender.SetRenderInterval(80); } catch { }
                // 图像显示区采用黑底，与 VM 原生工具条和坐标栏统一。
                try { _vmRender.SetBackground("#000000"); } catch { }
                try { _vmRender.ChangeTopBarVisibility(false); } catch { }

                _crosshairOverlay = new CrosshairOverlayWindow
                {
                    ShowCrosshair = _chkCrosshair.Checked
                };
                _vmRender.MouseMove += (s, e) =>
                {
                    if (_crosshairOverlay != null)
                        _crosshairOverlay.UpdatePointer(_vmRender.PointToScreen(e.Location));
                };
                _vmRender.MouseLeave += (s, e) =>
                {
                    if (_crosshairOverlay != null) _crosshairOverlay.UpdatePointer(null);
                };
                _pnlRenderHost.Controls.Add(_vmRender);
                AttachImageContextMenu();
            }
            catch (Exception ex)
            {
                _app.Logger.Error("UI", "创建 VmRenderControl 失败。", ex);
                _vmRender = null;
                _pnlRenderHost.Controls.Add(new Label
                {
                    Dock = DockStyle.Fill,
                    Text = "VM 原生渲染控件初始化失败。\r\n请检查 VisionMaster 安装、许可与 x64 平台。\r\n" + ex.Message,
                    ForeColor = Theme.Warning,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = Theme.BoldFont
                });
            }
        }

        private void AttachImageContextMenu()
        {
            var menu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                Font = Theme.BaseFont,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text
            };
            var fitItem = new ToolStripMenuItem("自适应")
            {
                ForeColor = Theme.Text,
                Tag = UiIcon.Photo,
                Image = UiIcons.ToBitmap(UiIcon.Photo, 16, Theme.Text)
            };
            fitItem.Click += (s, e) =>
            {
                try { if (_vmRender != null) _vmRender.InitView(); } catch { }
            };
            var saveOriginal = new ToolStripMenuItem("保存原图")
            {
                ForeColor = Theme.Text,
                Tag = UiIcon.Save,
                Image = UiIcons.ToBitmap(UiIcon.Save, 16, Theme.Text)
            };
            saveOriginal.Click += (s, e) => SaveRenderImage(false);
            var saveRendered = new ToolStripMenuItem("保存渲染图")
            {
                ForeColor = Theme.Text,
                Tag = UiIcon.Save,
                Image = UiIcons.ToBitmap(UiIcon.Save, 16, Theme.Text)
            };
            saveRendered.Click += (s, e) => SaveRenderImage(true);
            menu.Items.Add(fitItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(saveOriginal);
            menu.Items.Add(saveRendered);

            // 图像区右键：保留保存原图/渲染图，并新增自适应；顶部只保留 1:1。
            _imageMenu = menu;
            _vmRender.ContextMenuStrip = menu;
            _pnlRenderHost.ContextMenuStrip = menu;
        }

        private void SaveRenderImage(bool rendered)
        {
            if (_vmRender == null) return;
            string folder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string sourcePath = _app.LocalImages.CurrentPath;
            if (!string.IsNullOrWhiteSpace(sourcePath) && Directory.Exists(Path.GetDirectoryName(sourcePath)))
                folder = Path.GetDirectoryName(sourcePath);
            string baseName = string.IsNullOrWhiteSpace(sourcePath)
                ? "X Vision_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")
                : Path.GetFileNameWithoutExtension(sourcePath) + (rendered ? "_渲染图" : "_原图");
            using (var dialog = new SaveFileDialog
            {
                Title = rendered ? "保存渲染图" : "保存原图",
                Filter = "PNG 图片|*.png|JPEG 图片|*.jpg;*.jpeg|BMP 图片|*.bmp",
                InitialDirectory = folder,
                FileName = baseName + ".png",
                OverwritePrompt = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (rendered) _vmRender.SaveRenderedImage(dialog.FileName);
                    else _vmRender.SaveOriginalImage(dialog.FileName);
                    SetMessage("已保存：" + Path.GetFileName(dialog.FileName), Theme.Ok);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "保存失败：\r\n" + ex.Message, "保存图片",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ApplyShellSurfaceColors()
        {
            // SplitContainer 的 Panel1/Panel2 不总在普通 Controls 递归中，显式收口。
            if (_splitMain != null)
            {
                _splitMain.Panel1.BackColor = Theme.Surface;
                _splitMain.Panel2.BackColor = Theme.Surface;
            }

            // 顶部所有布局容器统一为卡片色，避免切回浅色时残留深色容器。
            ForceContainerColor(_pnlTopBar, Theme.Surface);
            ForceContainerColor(_pnlBrand, Theme.Surface);
            ForceContainerColor(_splitMain, Theme.Surface);
            if (_roleBadge != null)
            {
                _roleBadge.BackColor = Theme.Surface;
                _roleBadge.Invalidate();
            }
            if (_cameraSelector != null)
            {
                _cameraSelector.BackColor = Theme.Surface;
                _cameraSelector.Invalidate();
            }
            if (_chkCrosshair != null) _chkCrosshair.BackColor = Theme.Surface;
            if (_lblMessage != null) _lblMessage.BackColor = Color.Transparent;
            foreach (ComboBox combo in new[] { _cboSolution, _cboRenderSource, _cboParamModule, _cboCameras, _cboTriggerMode })
                if (combo != null) Theme.StyleCombo(combo);
            if (_lblJudgeResult != null)
            {
                if (_lblJudgeResult.Parent != null) _lblJudgeResult.Parent.BackColor = Theme.SurfaceAlt;
                _lblJudgeResult.BackColor = Theme.SurfaceAlt;
            }
            if (_lblJudgeSource != null)
            {
                _lblJudgeSource.BackColor = Theme.SurfaceAlt;
            }

            if (_pnlImageHeader != null)
            {
                _pnlImageHeader.BackColor = Theme.Surface;
                _pnlImageHeader.Invalidate();
            }
            if (_tabSource != null)
            {
                _tabSource.BackColor = Theme.Surface;
                _tabSource.Appearance = TabAppearance.FlatButtons;
                if (_tabLocal != null)
                {
                    _tabLocal.BackColor = Theme.Surface;
                    ForceContainerColor(_tabLocal, Theme.Surface);
                }
                if (_tabCamera != null)
                {
                    _tabCamera.BackColor = Theme.Surface;
                    ForceContainerColor(_tabCamera, Theme.Surface);
                }
                Theme.ApplyNativeTabTheme(_tabSource);
                _tabSource.Invalidate();
            }
            if (_tabRight != null)
            {
                _tabRight.BackColor = Theme.Surface;
                _tabRight.Appearance = TabAppearance.FlatButtons;
                if (_tabResult != null)
                {
                    _tabResult.BackColor = Theme.Surface;
                    ForceContainerColor(_tabResult, Theme.Surface);
                }
                if (_tabParams != null)
                {
                    _tabParams.BackColor = Theme.Surface;
                    ForceContainerColor(_tabParams, Theme.Surface);
                }
                if (_tabLog != null)
                {
                    _tabLog.BackColor = Theme.Surface;
                    ForceContainerColor(_tabLog, Theme.Surface);
                }
                Theme.ApplyNativeTabTheme(_tabRight);
                _tabRight.Invalidate();
            }
            if (_pnlStatusBar != null) _pnlStatusBar.BackColor = Theme.SurfaceAlt;
            if (_lvLocalImages != null)
            {
                _lvLocalImages.BackColor = Theme.Surface;
                _lvLocalImages.ForeColor = Theme.Text;
            }
        }

        private static void ForceContainerColor(Control root, Color color)
        {
            if (root == null) return;
            if (root is Panel || root is FlowLayoutPanel || root is TableLayoutPanel)
            {
                root.BackColor = color;
                root.Invalidate();
            }
            foreach (Control child in root.Controls)
            {
                if (child is Button || child is Label || child is CheckBox ||
                    child is RadioButton || child is PictureBox ||
                    child is ComboBox || child is TextBox || child is NumericUpDown ||
                    child is DataGridView || child is TabControl || child is ListView ||
                    child.GetType().Namespace == "VMControls.Winform.Release") continue;
                ForceContainerColor(child, color);
            }
        }

        private void WireEvents()
        {
            _btnLoadSolution.Click += async (s, e) => await LoadSelectedSolutionAsync(false);
            _btnManageSolutions.Click += (s, e) => ManageSolutions();
            _cameraSelector.Click += async (s, e) => await ShowCameraMenuAsync();
            _btnLoginLogout.Click += (s, e) => ToggleLogin();
            _btnSettings.Click += async (s, e) => await ShowSettingsAsync();
            _btnExportReport.Click += (s, e) => ShowReport();
            _btnStart.Click += async (s, e) => await StartDetectionAsync(SelectedDetectionMode());
            _btnPause.Click += async (s, e) => await PauseDetectionAsync();
            _btnResume.Click += async (s, e) => await ResumeDetectionAsync();
            _btnStop.Click += async (s, e) => await StopDetectionAsync();
            _btnResetStats.Click += (s, e) => ResetStatistics();
            _btnZoomIn.Click += (s, e) => { try { if (_vmRender != null) _vmRender.EnlargeView(); } catch { } };
            _btnZoomOut.Click += (s, e) => { try { if (_vmRender != null) _vmRender.ShrinkView(); } catch { } };
            _btnActualSize.Click += (s, e) => { try { if (_vmRender != null) _vmRender.InitView(); } catch { } };
            _chkCrosshair.CheckedChanged += (s, e) =>
            {
                if (_crosshairOverlay != null) _crosshairOverlay.ShowCrosshair = _chkCrosshair.Checked;
            };
            _cboRenderSource.SelectedIndexChanged += async (s, e) => await OnRenderSourceChangedAsync();

            _tabSource.SelectedIndexChanged += SourceTabChanged;
            _tabSource.MouseDown += (s, e) =>
            {
                // 底部只剩“本地图像”页。相机模式下单击该页签即可切回本地图像，
                // 不需要重新导入或重启。
                if (e.Button == MouseButtons.Left && _inputMode == InputMode.Camera && !_busy)
                    ApplySourceMode(InputMode.LocalImages, true);
            };
            _btnImportFiles.Click += (s, e) => ImportLocalFiles();
            _btnImportFolder.Click += (s, e) => ImportLocalFolder();
            _btnClearLocal.Click += (s, e) => ClearLocalImages();
            _cboPlaybackMode.SelectedIndexChanged += (s, e) => ApplyPlaybackMode();
            _lvLocalImages.SelectedIndexChanged += (s, e) =>
            {
                if (_lvLocalImages.SelectedIndices.Count > 0)
                    _app.LocalImages.SetCurrentIndex(_lvLocalImages.SelectedIndices[0]);
            };

            _btnRefreshCameras.Click += async (s, e) => await RefreshCamerasAsync(false);
            _btnConnectCamera.Click += async (s, e) => await ToggleCameraConnectionAsync();
            _btnApplyCamera.Click += async (s, e) => await ApplyCameraSettingsAsync();
            _btnSoftTrigger.Click += async (s, e) => await SoftTriggerPreviewAsync();

            _tabRight.SelectedIndexChanged += async (s, e) => await OnRightTabChangedAsync();
            _btnRefreshModules.Click += async (s, e) => await RefreshParameterModulesAsync();
            _cboParamModule.SelectedIndexChanged += async (s, e) => await ShowSelectedParameterControlAsync();
            _btnPresetSave.Click += async (s, e) => await SaveParameterPresetAsync();
            _btnPresetLoad.Click += async (s, e) => await LoadParameterPresetAsync();
            _btnParamReset.Click += async (s, e) => await ResetParameterDefaultsAsync();
            _btnSaveSolution.Click += async (s, e) => await SaveSolutionParametersAsync();
            _btnClearLog.Click += (s, e) => _txtSystemLog.Clear();

            _app.Detection.StateChanged += OnDetectionStateChanged;
            _app.Detection.DetectionCompleted += OnDetectionCompleted;
            _app.Detection.ErrorOccurred += OnDetectionError;
            _app.Detection.Notice += OnDetectionNotice;
            _app.Camera.FrameReceived += OnCameraFrame;
            _app.Camera.StatusChanged += OnCameraStatusChanged;
            _app.Communication.StatusChanged += OnCommunicationStatusChanged;
            _app.ExternalTriggers.Triggered += OnExternalTriggered;
            _app.Logger.EntryWritten += OnLogEntry;

            Shown += async (s, e) => await OnShownAsync();
            FormClosing += OnFormClosing;
            FormClosed += OnFormClosed;

            Move += (s, e) => UpdateCrosshairOverlay();
            Resize += (s, e) => UpdateCrosshairOverlay();
            VisibleChanged += (s, e) => UpdateCrosshairOverlay();
            if (_vmRender != null)
            {
                _vmRender.SizeChanged += (s, e) => UpdateCrosshairOverlay();
                _vmRender.LocationChanged += (s, e) => UpdateCrosshairOverlay();
            }
            _splitMain.SplitterMoved += (s, e) => UpdateCrosshairOverlay();
        }

        private void UpdateCrosshairOverlay()
        {
            if (_crosshairOverlay == null || _vmRender == null || _shutdownStarted) return;

            if (!Visible || WindowState == FormWindowState.Minimized)
            {
                if (_crosshairOverlay.Visible) _crosshairOverlay.Hide();
                return;
            }

            Rectangle screenRect;
            try
            {
                screenRect = _vmRender.RectangleToScreen(_vmRender.ClientRectangle);
            }
            catch { return; }
            if (screenRect.Width <= 0 || screenRect.Height <= 0) return;

            try
            {
                // VM 原生工具条为深色，且其 WPF 控件不能可靠改成白色。
                _crosshairOverlay.PaintTopCover = false;
                _crosshairOverlay.TopCoverHeight = 0;
                _crosshairOverlay.SetScreenBounds(screenRect);
                if (!_crosshairOverlay.Visible)
                    _crosshairOverlay.Show(this);
                else
                    _crosshairOverlay.Invalidate();
            }
            catch (Exception ex)
            {
                _app.Logger.Debug("UI", "更新十字线叠加窗口失败：" + ex.Message);
            }
        }

        private async Task OnShownAsync()
        {
            if (_initialShown) return;
            _initialShown = true;

            try
            {
                _splitMain.Panel1MinSize = 640;
                _splitMain.Panel2MinSize = 340;
                int desired = Math.Max(760, (int)(ClientSize.Width * 0.72));
                int maximum = ClientSize.Width - _splitMain.Panel2MinSize - _splitMain.SplitterWidth;
                if (desired > maximum) desired = maximum;
                if (desired > _splitMain.Panel1MinSize) _splitMain.SplitterDistance = desired;
            }
            catch (Exception ex)
            {
                _app.Logger.Warn("UI", "设置主分栏失败：" + ex.Message);
            }

            RefreshSolutionCombo();
            ApplySourceMode(_app.Settings.Input.Mode, false);
            ApplySettingsToUi();

            if (_inputMode == InputMode.LocalImages &&
                Directory.Exists(_app.Settings.Input.LastLocalFolder))
            {
                string[] extensions = { ".bmp", ".jpg", ".jpeg", ".png", ".tif", ".tiff" };
                try
                {
                    string[] files = Directory.EnumerateFiles(_app.Settings.Input.LastLocalFolder,
                            "*.*", SearchOption.TopDirectoryOnly)
                        .Where(x => extensions.Contains(Path.GetExtension(x), StringComparer.OrdinalIgnoreCase))
                        .ToArray();
                    if (files.Length > 0)
                    {
                        _app.LocalImages.SetImages(files);
                        _app.LocalImages.PlaybackMode = _app.Settings.Input.LocalPlaybackMode;
                        RefreshLocalImageList();
                    }
                }
                catch (Exception ex)
                {
                    _app.Logger.Warn("UI", "自动加载上次本地图片目录失败：" + ex.Message);
                }
            }

            UpdateControlStates();

            UpdateCrosshairOverlay();

            try
            {
                await _app.Communication.StartAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _app.Logger.Error("UI", "启动通讯失败。", ex);
                SetMessage("通讯启动失败：" + ex.Message, Theme.Warning);
            }

            string autoLoad = _app.Settings.General.RememberLastSolution
                ? _app.Settings.LastSolutionPath
                : null;
            if (!string.IsNullOrWhiteSpace(autoLoad) && File.Exists(autoLoad))
            {
                SelectSolutionPath(autoLoad);
                await LoadSelectedSolutionAsync(true);
            }
            else
            {
                SetMessage(_isAdmin
                    ? "请点击“加载视觉方案”选择 VisionMaster 方案。"
                    : "尚未配置方案，请联系管理员加载 .sol 方案。", Theme.Warning);
            }

            if (_inputMode == InputMode.Camera)
                await RefreshCamerasAsync(true);
        }

        private void ApplySettingsToUi()
        {
            if (_app.Settings == null) return;

            _cboPlaybackMode.SelectedIndex = PlaybackIndex(_app.Settings.Input.LocalPlaybackMode);
            _cboTriggerMode.SelectedIndex = TriggerIndex(_app.Settings.Camera.TriggerMode);
            _numExposure.Value = ClampNumeric(_numExposure, (decimal)_app.Settings.Camera.ExposureTimeUs);
            _numGain.Value = ClampNumeric(_numGain, (decimal)_app.Settings.Camera.GainDb);
            _segDetectionMode.SelectedIndex = _app.Settings.Detection.DefaultMode == DetectionMode.Continuous ? 1 : 0;
            UpdateClock();
        }

        private void RefreshUserUi()
        {
            UserSession user = _app.Auth.CurrentUser;
            _isAdmin = user != null && user.IsAdministrator;
            _roleBadge.Text = user == null ? "未登录" : user.RoleText;
            _roleBadge.IsAdmin = user != null && user.Role == UserRole.Administrator;
            _btnLoginLogout.Text = user == null ? "登录" : "注销";
            ApplyPermissions();
            UpdateControlStates();
        }

        private void ApplyPermissions()
        {
            bool admin = _isAdmin;
            _btnSettings.Enabled = admin && !_busy;
            _btnCommunication.Enabled = admin && !_busy;
            _btnImportFiles.Enabled = admin && !_busy;
            _btnImportFolder.Enabled = admin && !_busy;
            _btnClearLocal.Enabled = admin && !_busy;
            _cboPlaybackMode.Enabled = admin && !_busy;
            _btnApplyCamera.Enabled = admin && !_busy;
            _cboTriggerMode.Enabled = admin && !_busy;
            _numExposure.Enabled = admin && !_busy;
            _numGain.Enabled = admin && !_busy;
            _btnSaveSolution.Enabled = admin && !_busy;
            _btnPresetSave.Enabled = admin && !_busy;
            _btnPresetLoad.Enabled = admin && !_busy;
            _btnParamReset.Enabled = admin && !_busy;

            if (!admin)
            {
                _lblParamLock.Visible = true;
                _lblParamLock.BringToFront();
                DisposeParameterControl();
                _lblParamHint.Text = "操作员仅可执行检测；参数修改需要管理员权限。";
            }
            else
            {
                _lblParamHint.Text = "原生参数控件修改即时生效；保存方案后写入 .sol 文件。";
            }
        }

        private void UpdateControlStates()
        {
            if (IsDisposed || !IsHandleCreated) return;

            DetectionRunState state = _app.Detection.State;
            bool active = state == DetectionRunState.Running || state == DetectionRunState.Paused ||
                          state == DetectionRunState.Stopping;
            bool canStart = !_busy && !_solutionLoading && !_app.IsShutdownPending && _app.Algorithm.IsLoaded &&
                            (state == DetectionRunState.Idle || state == DetectionRunState.Fault);

            _btnStart.Enabled = canStart && _app.Auth.IsAuthenticated;
            _btnPause.Enabled = state == DetectionRunState.Running && !_busy;
            _btnResume.Enabled = state == DetectionRunState.Paused && !_busy;
            _btnStop.Enabled = !_busy && (state == DetectionRunState.Running ||
                                          state == DetectionRunState.Paused ||
                                          state == DetectionRunState.Fault);
            _btnResetStats.Enabled = !_busy;

            bool canConfigure = !_busy && !_solutionLoading && !active && !_app.IsShutdownPending;
            _btnLoginLogout.Enabled = !_busy && !_app.IsShutdownPending;
            bool admin = _isAdmin;
            _btnManageSolutions.Enabled = canConfigure && admin;
            // 即使目录为空也允许管理员打开 .sol；首次选择文件后会自动加入方案列表。
            _btnLoadSolution.Enabled = canConfigure && admin;
            _cboSolution.Enabled = canConfigure && admin;
            _btnSettings.Enabled = admin && !_busy && !_solutionLoading && !_app.IsShutdownPending;
            _btnCommunication.Enabled = _btnSettings.Enabled;
            _btnImportFiles.Enabled = canConfigure && admin;
            _btnImportFolder.Enabled = canConfigure && admin;
            _cameraSelector.Enabled = canConfigure;
            _cameraSelector.Connected = _app.Camera.IsConnected;
            _btnClearLocal.Enabled = canConfigure && admin;
            _cboPlaybackMode.Enabled = canConfigure && admin;
            _btnRefreshCameras.Enabled = canConfigure;
            _btnConnectCamera.Enabled = canConfigure;
            _btnApplyCamera.Enabled = canConfigure && admin;
            _cboTriggerMode.Enabled = canConfigure && admin;
            _numExposure.Enabled = canConfigure && admin;
            _numGain.Enabled = canConfigure && admin;
            _btnSoftTrigger.Enabled = canConfigure && _app.Camera.IsConnected;
            _cboRenderSource.Enabled = canConfigure && _app.Vision.IsLoaded;
            _btnRefreshModules.Enabled = canConfigure && admin && _app.Vision.IsLoaded;
            _cboParamModule.Enabled = canConfigure && admin && _cboParamModule.Items.Count > 0;
            _btnSaveSolution.Enabled = canConfigure && admin &&
                _app.Vision.IsLoaded && _app.Algorithm.Kind == AlgorithmEngineKind.VisionMaster;
            _btnPresetSave.Enabled = canConfigure && admin && _cboParamModule.SelectedItem != null;
            _btnPresetLoad.Enabled = canConfigure && admin && _cboParamModule.SelectedItem != null;
            _btnParamReset.Enabled = canConfigure && admin && _cboParamModule.SelectedItem != null;
            if (_vmParams != null) _vmParams.Enabled = canConfigure && admin;
        }

        private async Task OnRightTabChangedAsync()
        {
            int previous = _lastRightTabIndex;
            _lastRightTabIndex = _tabRight.SelectedIndex;
            int parameterIndex = _tabRight.TabPages.IndexOf(_tabParams);
            if (_tabRight.SelectedIndex == parameterIndex && previous != parameterIndex)
            {
                _resultSplitterDistance = _splitMain.SplitterDistance;
                _splitMain.SplitterDistance = Math.Max(_splitMain.Panel1MinSize,
                    Math.Min((int)(_splitMain.Width * 0.48), _splitMain.Width - _splitMain.Panel2MinSize - _splitMain.SplitterWidth));
            }
            else if (previous == parameterIndex && _resultSplitterDistance > 0)
                _splitMain.SplitterDistance = Math.Max(_splitMain.Panel1MinSize,
                    Math.Min(_resultSplitterDistance, _splitMain.Width - _splitMain.Panel2MinSize - _splitMain.SplitterWidth));
            if (previous != _tabRight.TabPages.IndexOf(_tabParams) || !_isAdmin) return;
            if (_busy || !_app.Vision.IsLoaded) return;
            if (!_app.Settings.Vision.AutoSaveParameterChanges) return;
            if (_app.Detection.State != DetectionRunState.Idle) return;

            try
            {
                await _app.Vision.SaveSolutionAsync(CancellationToken.None);
                _app.Logger.Info("UI", "离开参数页，已按配置自动保存方案。");
            }
            catch (Exception ex)
            {
                _app.Logger.Warn("UI", "自动保存方案失败：" + ex.Message);
            }
        }

        private async Task LoadSelectedSolutionAsync(bool initial)
        {
            if (_solutionLoading || _busy) return;
            if (!_isAdmin && !initial)
            {
                ShowError("操作员不能切换视觉方案。");
                RefreshSolutionCombo();
                return;
            }

            SolutionOption option = _cboSolution.SelectedItem as SolutionOption;
            if ((option == null || !File.Exists(option.Path)) && initial &&
                !string.IsNullOrWhiteSpace(_app.Settings.LastSolutionPath) &&
                File.Exists(_app.Settings.LastSolutionPath))
            {
                // 自动恢复时下拉框可能尚未完成选择；以配置中的最后方案兜底。
                RegisterSolution(_app.Settings.LastSolutionPath);
                SelectSolutionPath(_app.Settings.LastSolutionPath);
                option = _cboSolution.SelectedItem as SolutionOption;
            }
            if (option == null || !File.Exists(option.Path))
            {
                // 首次使用或列表为空时，点击“加载 .sol”直接打开文件选择框。
                if (initial) return;

                string picked = PickSolutionFile();
                if (string.IsNullOrWhiteSpace(picked)) return;
                RegisterSolution(picked);
                SelectSolutionPath(picked);
                option = _cboSolution.SelectedItem as SolutionOption;
                if (option == null || !File.Exists(option.Path))
                {
                    ShowError("无法读取所选的视觉方案文件。");
                    return;
                }
            }

            if (!initial && (_app.Detection.State == DetectionRunState.Running ||
                             _app.Detection.State == DetectionRunState.Paused))
            {
                if (MessageBox.Show("切换方案前必须停止当前检测，是否继续？", "切换方案",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
            }

            _solutionLoading = true;
            _busy = true;
            UpdateControlStates();
            SetMessage("正在加载方案：" + Path.GetFileName(option.Path) + " ...", Theme.Accent);
            _app.Logger.Info("UI", "开始加载视觉方案：" + option.Path);

            // 必须先解除渲染/参数控件对旧流程的绑定，再让 VM CloseSolution。
            if (_vmRender != null)
            {
                try { _vmRender.ModuleSource = null; } catch { }
            }
            DisposeParameterControl();

            try
            {
                VisionSolutionInfo solution = await _app.LoadSolutionAsync(option.Path, null);
                _solution = solution;
                _app.Settings.Vision.PrimaryProcedureName = solution.PrimaryProcedureName;
                _app.Settings.LastSolutionPath = option.Path;
                _app.SaveSettings();

                RefreshRenderSourceCombo(solution);
                await BindSelectedRenderSourceAsync();
                ClearResultUi();
                await RefreshParameterModulesAsync();
                UpdateSolutionStatus();
                SetMessage(string.Format("方案已加载：{0}，主流程 {1}，模块 {2} 个。",
                    Path.GetFileName(option.Path), solution.PrimaryProcedureName, _app.Vision.Modules.Count),
                    Theme.Ok);
                _app.Logger.Audit(_app.Auth.CurrentUser == null ? "-" : _app.Auth.CurrentUser.UserName,
                    "UILoadSolution", option.Path);
            }
            catch (Exception ex)
            {
                // 加载前旧方案已关闭。失败时必须清除旧元数据，避免界面保留已释放模块。
                _solution = _app.Vision.CurrentSolution;
                _cboRenderSource.Items.Clear();
                _cboParamModule.Items.Clear();
                ClearResultUi();
                UpdateSolutionStatus();
                // 先落日志再弹窗，避免 MessageBox 阻塞导致现场无法定位加载失败原因。
                _app.Logger.Error("UI", "加载视觉方案失败。", ex);
                ShowError("加载方案失败：" + ex.Message);
            }
            finally
            {
                _solutionLoading = false;
                _busy = false;
                UpdateControlStates();
            }
            // 加载期间 SelectedIndexChanged 被 _busy 拦截，完成后再初始化原生参数页。
            if (_isAdmin && _app.Vision.IsLoaded) await ShowSelectedParameterControlAsync();
        }

        private void RefreshRenderSourceCombo(VisionSolutionInfo solution)
        {
            _cboRenderSource.Items.Clear();
            if (solution == null) return;

            _cboRenderSource.Items.Add(new RenderOption
            {
                FullName = solution.PrimaryProcedureName,
                Display = "流程整体 / " + solution.PrimaryProcedureName,
                IsProcedure = true
            });

            var modules = _app.Vision.Modules
                .Where(x => x.IsAvailable &&
                            string.Equals(x.ProcedureName, solution.PrimaryProcedureName,
                                StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (VisionModuleInfo module in modules)
            {
                _cboRenderSource.Items.Add(new RenderOption
                {
                    FullName = module.FullName,
                    Display = module.ProcedureName + " / " + module.DisplayName,
                    IsProcedure = false
                });
            }

            // 优先绑定检测类模块（最后一个非图像源/格式化模块），它通常能显示输入图 + ROI/测量结果。
            int selected = 0;
            for (int i = modules.Count - 1; i >= 0; i--)
            {
                if (!modules[i].IsImageSource && !modules[i].IsFormatModule)
                {
                    selected = i + 1; // 0 是“流程整体”
                    break;
                }
            }

            _updatingRenderSource = true;
            try
            {
                _cboRenderSource.SelectedIndex = _cboRenderSource.Items.Count > selected ? selected : 0;
            }
            finally
            {
                _updatingRenderSource = false;
            }
        }

        private async Task OnRenderSourceChangedAsync()
        {
            if (_updatingRenderSource) return;
            await BindSelectedRenderSourceAsync();
        }

        private async Task BindSelectedRenderSourceAsync()
        {
            if (_vmRender == null) return;
            RenderOption option = _cboRenderSource.SelectedItem as RenderOption;
            if (option == null) return;

            try
            {
                _vmRender.ModuleSource = null;
                object source = option.IsProcedure
                    ? await _app.Vision.GetRenderSourceAsync(option.FullName, CancellationToken.None)
                    : await _app.Vision.GetModuleSourceAsync(option.FullName, CancellationToken.None);

                VMControls.Interface.IVmModule module = source as VMControls.Interface.IVmModule;
                if (module == null)
                    throw new VisionInspectException("对象不实现 IVmModule，无法绑定渲染控件。");

                _vmRender.ModuleSource = module;
                _lblImageTitle.Text = "图像显示";
            }
            catch (Exception ex)
            {
                _app.Logger.Error("UI", "绑定 VM 渲染控件失败。", ex);
                SetMessage("渲染控件绑定失败：" + ex.Message, Theme.Warning);
            }
        }

        private void UpdateSolutionStatus()
        {
            string name = _solution == null ? "未加载" : Path.GetFileName(_solution.SolutionPath ?? string.Empty);
            _lblStatusSolution.Text = "方案：" + name;
            _lblStatusSolution.ForeColor = _solution == null ? Theme.TextMuted : Theme.Text;
            UpdateControlStates();
        }

        private async Task StartDetectionAsync(DetectionMode mode)
        {
            if (!_app.Auth.IsAuthenticated)
            {
                ShowError("请先登录。");
                return;
            }
            if (_busy || _solutionLoading) return;
            if (!_app.Algorithm.IsLoaded)
            {
                ShowError("请先加载视觉方案。");
                return;
            }

            try
            {
                _busy = true;
                UpdateControlStates();
                if (_inputMode == InputMode.Camera)
                {
                    if (!_app.Camera.IsConnected)
                        throw new VisionInspectException("工业相机未连接。");
                    ReadCameraUiToSettings();
                    await _app.Camera.ApplySettingsAsync(_app.Settings.Camera, CancellationToken.None);
                }

                IInputProvider provider = CreateInputProvider();
                _segDetectionMode.SelectedIndex = mode == DetectionMode.Continuous ? 1 : 0;
                await _app.Detection.StartAsync(mode, provider);
                SetMessage(mode == DetectionMode.Single ? "单次检测已启动。" : "连续检测已启动。", Theme.Ok);
            }
            catch (Exception ex)
            {
                ShowError("启动检测失败：" + ex.Message);
                _app.Logger.Error("UI", "启动检测失败。", ex);
            }
            finally
            {
                _busy = false;
                UpdateControlStates();
            }
        }

        private IInputProvider CreateInputProvider()
        {
            if (_inputMode == InputMode.Camera)
            {
                if (!_app.Camera.IsConnected)
                    throw new VisionInspectException("工业相机未连接。");
                return new CameraInputProvider(_app.Camera, _app.Settings.Camera.FrameTimeoutMs);
            }

            if (_app.LocalImages.Count == 0)
                throw new VisionInspectException("本地图片列表为空。");
            _app.LocalImages.PlaybackMode = SelectedPlaybackMode();
            return _app.LocalImages;
        }

        private async Task PauseDetectionAsync()
        {
            try { await _app.Detection.PauseAsync(); }
            catch (Exception ex) { ShowError("暂停失败：" + ex.Message); }
        }

        private async Task ResumeDetectionAsync()
        {
            try { await _app.Detection.ResumeAsync(); }
            catch (Exception ex) { ShowError("继续失败：" + ex.Message); }
        }

        private async Task StopDetectionAsync()
        {
            try
            {
                _busy = true;
                UpdateControlStates();
                await _app.Detection.StopAsync();
                SetMessage("检测已停止。", Theme.TextMuted);
            }
            catch (Exception ex)
            {
                ShowError("停止失败：" + ex.Message);
            }
            finally
            {
                _busy = false;
                UpdateControlStates();
            }
        }

        private void ResetStatistics()
        {
            if (MessageBox.Show("确定将本次运行统计清零？", "统计清零",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            _app.Statistics.Reset();
            UpdateStatisticsUi();
            _app.Logger.Audit(_app.Auth.CurrentUser == null ? "-" : _app.Auth.CurrentUser.UserName,
                "ResetStatistics", "检测统计清零");
        }

        private DetectionMode SelectedDetectionMode()
        {
            return _segDetectionMode.SelectedIndex == 1 ? DetectionMode.Continuous : DetectionMode.Single;
        }

        private PlaybackMode SelectedPlaybackMode()
        {
            switch (_cboPlaybackMode.SelectedIndex)
            {
                case 0: return PlaybackMode.Manual;
                case 1: return PlaybackMode.Sequential;
                default: return PlaybackMode.Loop;
            }
        }

        private void ApplyPlaybackMode()
        {
            if (_isAdmin && !_busy)
            {
                _app.LocalImages.PlaybackMode = SelectedPlaybackMode();
                _app.Settings.Input.LocalPlaybackMode = SelectedPlaybackMode();
                _app.SaveSettings();
            }
        }

        private void ApplySourceMode(InputMode mode, bool persist)
        {
            _inputMode = mode;
            _updatingSourceTab = true;
            // 相机配置已移到顶部“工业相机”对话框；底部来源页只保留本地图像。
            try { _tabSource.SelectedIndex = 0; }
            finally { _updatingSourceTab = false; }

            if (persist)
            {
                _app.Settings.Input.Mode = mode;
                _app.SaveSettings();
            }
            UpdateStatusBar();
        }

        /// <summary>顶栏相机下拉：按 GigE/USB 分组枚举设备，点击设备后打开相机配置。</summary>
        private async Task ShowCameraMenuAsync()
        {
            if (_busy || _app.IsShutdownPending) return;
            var menu = new ContextMenuStrip { ShowImageMargin = false, Font = Theme.BaseFont };
            menu.Items.Add(new ToolStripMenuItem("正在枚举 GigE/USB 相机...") { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("打开相机配置...", null, (s, e) => OpenCameraPage());
            menu.Show(_cameraSelector, new Point(0, _cameraSelector.Height));
            try
            {
                IReadOnlyList<CameraDeviceInfo> devices = await Task.Run(() => _app.Camera.EnumerateDevices());
                menu.Items.Clear();
                int count = 0;
                foreach (var group in devices.GroupBy(x =>
                    string.IsNullOrWhiteSpace(x.Transport) ? "GigE" : x.Transport,
                    StringComparer.OrdinalIgnoreCase))
                {
                    menu.Items.Add(new ToolStripMenuItem(group.Key + " 相机") { Enabled = false });
                    foreach (CameraDeviceInfo device in group)
                    {
                        var item = new ToolStripMenuItem(device.ToString());
                        item.Click += (s, e) => { menu.Close(); OpenCameraPage(); };
                        menu.Items.Add(item);
                        count++;
                    }
                }
                if (count == 0)
                    menu.Items.Add(new ToolStripMenuItem("未发现相机（打开配置刷新）") { Enabled = false });
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("刷新设备", null, async (s, e) => { menu.Close(); await ShowCameraMenuAsync(); });
                menu.Items.Add("打开相机配置...", null, (s, e) => OpenCameraPage());
            }
            catch (Exception ex)
            {
                menu.Items.Clear();
                menu.Items.Add(new ToolStripMenuItem("枚举失败：" + ex.Message) { Enabled = false });
                menu.Items.Add("打开相机配置...", null, (s, e) => OpenCameraPage());
            }
        }

        private void OpenCameraPage()
        {
            if (!_app.Auth.IsAuthenticated)
            {
                SetMessage("请先登录后再配置工业相机。", Theme.Warning);
                return;
            }
            if (_busy || _app.Detection.State == DetectionRunState.Running ||
                _app.Detection.State == DetectionRunState.Paused ||
                _app.Detection.State == DetectionRunState.Stopping)
            {
                SetMessage("运行中不能打开相机配置。", Theme.Warning);
                return;
            }

            if (_app.Camera.IsConnected)
                ApplySourceMode(InputMode.Camera, true);

            // 相机配置使用独立对话框，不在底部来源页配置。
            // TabPage 只能放在 TabControl 中，因此把相机页的实际内容控件移到对话框。
            Control cameraContent = _tabCamera.Controls.Count > 0 ? _tabCamera.Controls[0] : null;
            if (cameraContent == null)
            {
                ShowError("相机配置页尚未初始化。");
                return;
            }

            using (var form = new Form
            {
                Text = "工业相机 · X Vision",
                Icon = Icon,
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(900, 620),
                MinimumSize = new Size(760, 520),
                BackColor = Theme.Background,
                Font = Theme.BaseFont,
                AutoScaleMode = AutoScaleMode.Dpi,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false
            })
            {
                _tabCamera.Controls.Remove(cameraContent);
                cameraContent.Dock = DockStyle.Fill;
                form.Controls.Add(cameraContent);
                try
                {
                    form.ShowDialog(this);
                }
                finally
                {
                    form.Controls.Remove(cameraContent);
                    _tabCamera.Controls.Add(cameraContent);
                }
            }

            UpdateControlStates();
            UpdateStatusBar();
        }

        private void SourceTabChanged(object sender, EventArgs e)
        {
            if (_updatingSourceTab) return;
            // 底部只剩本地图像页；相机切换由顶部按钮控制。
            InputMode requested = InputMode.LocalImages;
            bool active = _app.Detection.State == DetectionRunState.Running ||
                          _app.Detection.State == DetectionRunState.Paused ||
                          _app.Detection.State == DetectionRunState.Stopping;
            if (_busy || active)
            {
                ApplySourceMode(_inputMode, false);
                SetMessage("运行中不能切换图像源。", Theme.Warning);
                return;
            }

            _inputMode = requested;
            _app.Settings.Input.Mode = requested;
            _app.SaveSettings();
            UpdateStatusBar();
        }

        private void ImportLocalFiles()
        {
            if (!_isAdmin) return;
            using (var dialog = new OpenFileDialog
            {
                Multiselect = false,
                Filter = "图像文件|*.bmp;*.jpg;*.jpeg;*.png;*.tif;*.tiff|所有文件|*.*",
                Title = "导入单张检测图片",
                InitialDirectory = Directory.Exists(_app.Settings.Input.LastLocalFolder)
                    ? _app.Settings.Input.LastLocalFolder
                    : string.Empty
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _app.LocalImages.SetImages(dialog.FileNames);
                if (dialog.FileNames.Length > 0)
                    _app.Settings.Input.LastLocalFolder = Path.GetDirectoryName(dialog.FileNames[0]);
                ApplySourceMode(InputMode.LocalImages, true);
                RefreshLocalImageList();
                SetMessage(string.Format("已导入 {0} 张图片。", _app.LocalImages.Count), Theme.Ok);
            }
        }

        private void ImportLocalFolder()
        {
            if (!_isAdmin) return;
            using (var dialog = new FolderBrowserDialog
            {
                Description = "选择图片文件夹：导入其中的全部图片，不包含子文件夹。",
                SelectedPath = Directory.Exists(_app.Settings.Input.LastLocalFolder)
                    ? _app.Settings.Input.LastLocalFolder
                    : string.Empty
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                string[] extensions = { ".bmp", ".jpg", ".jpeg", ".png", ".tif", ".tiff" };
                string[] files = Directory.EnumerateFiles(dialog.SelectedPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(x => extensions.Contains(Path.GetExtension(x), StringComparer.OrdinalIgnoreCase))
                    .ToArray();
                _app.LocalImages.SetImages(files);
                _app.Settings.Input.LastLocalFolder = dialog.SelectedPath;
                ApplySourceMode(InputMode.LocalImages, true);
                RefreshLocalImageList();
                SetMessage(string.Format("文件夹导入完成，共 {0} 张图片。", _app.LocalImages.Count), Theme.Ok);
            }
        }

        private void ClearLocalImages()
        {
            if (!_isAdmin) return;
            _app.LocalImages.Clear();
            RefreshLocalImageList();
            SetMessage("本地图片列表已清空。", Theme.TextMuted);
        }

        private void RefreshLocalImageList()
        {
            _lvLocalImages.BeginUpdate();
            try
            {
                _lvLocalImages.Items.Clear();
                int index = 0;
                foreach (string path in _app.LocalImages.ImagePaths)
                {
                    var item = new ListViewItem((index + 1).ToString());
                    item.SubItems.Add(Path.GetFileName(path));
                    item.SubItems.Add(path);
                    _lvLocalImages.Items.Add(item);
                    index++;
                }

                if (_lvLocalImages.Items.Count > 0)
                {
                    int current = Math.Min(_app.LocalImages.CurrentIndex, _lvLocalImages.Items.Count - 1);
                    _lvLocalImages.Items[current].Selected = true;
                    _lvLocalImages.EnsureVisible(current);
                }
                _lblLocalCount.Text = string.Format("共 {0} 张", _app.LocalImages.Count);
            }
            finally
            {
                _lvLocalImages.EndUpdate();
            }
        }

        private async Task RefreshCamerasAsync(bool autoConnect)
        {
            try
            {
                _btnRefreshCameras.Enabled = false;
                _lblCameraStatus.Text = "正在枚举相机...";
                var devices = await Task.Run(() => _app.Camera.EnumerateDevices());
                _cboCameras.Items.Clear();
                foreach (CameraDeviceInfo device in devices)
                    _cboCameras.Items.Add(device);

                if (devices.Count == 0)
                {
                    _lblCameraStatus.Text = "未发现海康 GigE/USB 相机。";
                    SetMessage("未发现相机，请检查 MVS 驱动与网络。", Theme.Warning);
                    return;
                }

                string preferred = _app.Settings.Input.LastCameraId;
                int selected = 0;
                for (int i = 0; i < _cboCameras.Items.Count; i++)
                {
                    CameraDeviceInfo device = (CameraDeviceInfo)_cboCameras.Items[i];
                    if (string.Equals(device.Id, preferred, StringComparison.OrdinalIgnoreCase))
                    {
                        selected = i;
                        break;
                    }
                }
                _cboCameras.SelectedIndex = selected;
                _selectedCamera = (CameraDeviceInfo)_cboCameras.SelectedItem;
                _lblCameraStatus.Text = string.Format("发现 {0} 台相机。", devices.Count);

                if (autoConnect && !_app.Camera.IsConnected && _selectedCamera != null)
                    await ConnectCameraAsync(true);
            }
            catch (Exception ex)
            {
                _lblCameraStatus.Text = "枚举相机失败：" + ex.Message;
                _app.Logger.Error("UI", "枚举相机失败。", ex);
                if (!autoConnect) ShowError("枚举相机失败：" + ex.Message);
            }
            finally
            {
                UpdateControlStates();
            }
        }

        private async Task ToggleCameraConnectionAsync()
        {
            if (_app.Camera.IsConnected)
            {
                try
                {
                    await _app.Camera.DisconnectAsync();
                    ApplySourceMode(InputMode.LocalImages, true);
                    SetMessage("相机已断开，已切回本地图像。", Theme.TextMuted);
                }
                catch (Exception ex) { ShowError("断开相机失败：" + ex.Message); }
            }
            else
            {
                await ConnectCameraAsync(false);
            }
            UpdateControlStates();
        }

        private async Task ConnectCameraAsync(bool silent)
        {
            _selectedCamera = _cboCameras.SelectedItem as CameraDeviceInfo;
            if (_selectedCamera == null)
            {
                if (!silent) ShowError("请先选择相机。");
                return;
            }

            _btnConnectCamera.Enabled = false;
            _lblCameraStatus.Text = "正在连接相机 " + _selectedCamera.DisplayName + " ...";
            try
            {
                ReadCameraUiToSettings();
                await _app.Camera.ConnectAsync(_selectedCamera, _app.Settings.Camera, CancellationToken.None);
                _app.Settings.Input.LastCameraId = _selectedCamera.Id;
                _app.SaveSettings();
                _selectedCamera = _app.Camera.ConnectedDevice;
                _lblCameraStatus.Text = "相机已连接：" +
                    (_selectedCamera == null ? string.Empty : _selectedCamera.DisplayName);
                // 连接成功后自动切换为相机输入；相机配置仍在独立对话框中完成。
                ApplySourceMode(InputMode.Camera, true);
                SetMessage("相机连接成功，当前输入源：工业相机。", Theme.Ok);
            }
            catch (Exception ex)
            {
                _lblCameraStatus.Text = "相机连接失败：" + ex.Message;
                _app.Logger.Error("UI", "连接相机失败。", ex);
                if (!silent) ShowError("连接相机失败：" + ex.Message);
            }
            finally
            {
                _btnConnectCamera.Enabled = true;
                UpdateCameraButtons();
                UpdateStatusBar();
                UpdateControlStates();
            }
        }

        private void ReadCameraUiToSettings()
        {
            CameraSettings settings = _app.Settings.Camera;
            settings.TriggerMode = _cboTriggerMode.SelectedIndex == 1 ? CameraTriggerMode.Hardware :
                _cboTriggerMode.SelectedIndex == 2 ? CameraTriggerMode.Continuous : CameraTriggerMode.Software;
            settings.ExposureTimeUs = (float)_numExposure.Value;
            settings.GainDb = (float)_numGain.Value;
        }

        private async Task ApplyCameraSettingsAsync()
        {
            if (!_isAdmin)
            {
                ShowError("仅管理员可以修改相机参数。");
                return;
            }

            try
            {
                ReadCameraUiToSettings();
                _app.SaveSettings();
                if (_app.Camera.IsConnected)
                    await _app.Camera.ApplySettingsAsync(_app.Settings.Camera, CancellationToken.None);
                SetMessage("相机参数已应用。", Theme.Ok);
            }
            catch (Exception ex)
            {
                ShowError("应用相机参数失败：" + ex.Message);
            }
        }

        private async Task SoftTriggerPreviewAsync()
        {
            if (!_app.Camera.IsConnected)
            {
                ShowError("相机未连接。");
                return;
            }
            if (_app.Detection.State == DetectionRunState.Running ||
                _app.Detection.State == DetectionRunState.Paused)
            {
                ShowError("检测运行中不能手动软触发。");
                return;
            }
            if (_cboTriggerMode.SelectedIndex != 0)
            {
                ShowError("软触发预览要求相机触发方式为“软触发”。");
                return;
            }

            try
            {
                ReadCameraUiToSettings();
                await _app.Camera.ApplySettingsAsync(_app.Settings.Camera, CancellationToken.None);
                _btnSoftTrigger.Enabled = false;
                RawImage frame = await _app.Camera.CaptureAsync(TimeSpan.FromSeconds(4), CancellationToken.None);
                UpdateCameraPreview(frame);
                SetMessage("软触发采图成功。", Theme.Ok);
            }
            catch (Exception ex)
            {
                ShowError("软触发采图失败：" + ex.Message);
            }
            finally
            {
                _btnSoftTrigger.Enabled = true;
            }
        }

        private void UpdateCameraButtons()
        {
            _btnConnectCamera.Text = _app.Camera.IsConnected ? "断开" : "连接";
            _btnConnectCamera.BackColor = _app.Camera.IsConnected ? Theme.Warning : Theme.Accent;
            _btnConnectCamera.ForeColor = Color.White;
        }

        private async Task RefreshParameterModulesAsync()
        {
            _cboParamModule.Items.Clear();
            DisposeParameterControl();

            if (!_isAdmin)
            {
                _lblParamLock.Visible = true;
                _lblParamLock.BringToFront();
                return;
            }

            if (!_app.Vision.IsLoaded || _solution == null)
            {
                _lblParamLock.Text = "请先加载视觉方案。";
                _lblParamLock.Visible = true;
                _lblParamLock.BringToFront();
                return;
            }

            var modules = _app.Vision.Modules
                .Where(x => x.IsAvailable)
                .OrderBy(x => x.DisplayName, StringComparer.CurrentCulture)
                .ToList();

            foreach (VisionModuleInfo module in modules)
                _cboParamModule.Items.Add(new ModuleOption { Module = module });

            if (_cboParamModule.Items.Count > 0)
                _cboParamModule.SelectedIndex = 0;
            else
            {
                _lblParamLock.Text = "当前方案没有可配置模块。";
                _lblParamLock.Visible = true;
                _lblParamLock.BringToFront();
            }

            UpdateControlStates();
            await Task.CompletedTask;
        }

        private async Task ShowSelectedParameterControlAsync()
        {
            if (!_isAdmin || _busy || _shutdownStarted || !_app.Vision.IsLoaded) return;
            ModuleOption option = _cboParamModule.SelectedItem as ModuleOption;
            if (option == null) return;

            _lblParamLock.Visible = false;
            try
            {
                object module = await _app.Vision.GetModuleSourceAsync(option.Module.FullName, CancellationToken.None);
                if (module == null) throw new VisionInspectException("模块对象为空。");
                // 等待期间可能注销、启动检测、切换模块或关闭窗口，不能绑定过期对象。
                if (!_isAdmin || _busy || _shutdownStarted || IsDisposed ||
                    !ReferenceEquals(option, _cboParamModule.SelectedItem) ||
                    _app.Detection.State != DetectionRunState.Idle) return;

                DisposeParameterControl();
                // 使用不带预览窗口的参数控件：右侧只显示参数，不再显示图像预览。
                _vmParams = new VMControls.Winform.Release.VmParamsConfigControl
                {
                    Dock = DockStyle.Fill,
                    Name = "vmParams"
                };
                _vmParams.ModuleSource = module as VMControls.Interface.IVmModule;
                _pnlParamHost.Controls.Add(_vmParams);
                _vmParams.BringToFront();
            }
            catch (Exception ex)
            {
                _lblParamLock.Text = "参数控件加载失败：" + ex.Message;
                _lblParamLock.Visible = true;
                _lblParamLock.BringToFront();
                _app.Logger.Error("UI", "创建 VM 参数控件失败。", ex);
            }
        }

        private void DisposeParameterControl()
        {
            if (_vmParams == null) return;
            try
            {
                _vmParams.ModuleSource = null;
                _pnlParamHost.Controls.Remove(_vmParams);
                _vmParams.Dispose();
            }
            catch { }
            _vmParams = null;
        }

        private ModuleOption SelectedParameterModule()
        {
            return _cboParamModule.SelectedItem as ModuleOption;
        }

        private async Task SaveParameterPresetAsync()
        {
            ModuleOption option = SelectedParameterModule();
            if (option == null) return;
            if (!_isAdmin) { ShowError("需要管理员权限。"); return; }

            string directory = Path.Combine(_app.ParameterRoot, "presets",
                ParameterSafeName(Path.GetFileNameWithoutExtension(_solution == null ? "solution" : _solution.SolutionPath)));
            Directory.CreateDirectory(directory);

            using (var dialog = new SaveFileDialog
            {
                Filter = "参数预设|*.json",
                InitialDirectory = directory,
                FileName = ParameterSafeName(option.Module.FullName) + ".json",
                OverwritePrompt = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    await _app.Vision.SaveParameterPresetAsync(option.Module.FullName, dialog.FileName,
                        CancellationToken.None);
                    SetMessage("参数预设已保存：" + Path.GetFileName(dialog.FileName), Theme.Ok);
                    _app.Logger.Audit(_app.Auth.CurrentUser.UserName, "SaveParameterPreset",
                        option.Module.FullName + " -> " + dialog.FileName);
                }
                catch (Exception ex)
                {
                    ShowError("保存参数预设失败：" + ex.Message);
                }
            }
        }

        private async Task LoadParameterPresetAsync()
        {
            ModuleOption option = SelectedParameterModule();
            if (option == null) return;
            if (!_isAdmin) { ShowError("需要管理员权限。"); return; }

            using (var dialog = new OpenFileDialog
            {
                Filter = "参数预设|*.json",
                Title = "加载参数预设"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    ParameterApplyResult result = await _app.Vision.LoadParameterPresetAsync(
                        option.Module.FullName, dialog.FileName, false, CancellationToken.None);
                    ReportParameterResult(result, "加载参数预设");
                    await ShowSelectedParameterControlAsync();
                }
                catch (Exception ex)
                {
                    ShowError("加载参数预设失败：" + ex.Message);
                }
            }
        }

        private async Task ResetParameterDefaultsAsync()
        {
            ModuleOption option = SelectedParameterModule();
            if (option == null) return;
            if (!_isAdmin) { ShowError("需要管理员权限。"); return; }
            if (MessageBox.Show("确定使用官方 ResetParam 将模块“" + option.Module.DisplayName + "”恢复为算子出厂默认参数？",
                    "恢复默认参数", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            try
            {
                ParameterApplyResult result = await _app.Vision.ResetParametersToDefaultAsync(
                    option.Module.FullName, false, CancellationToken.None);
                ReportParameterResult(result, "恢复默认参数");
                await ShowSelectedParameterControlAsync();
            }
            catch (Exception ex)
            {
                ShowError("恢复默认参数失败：" + ex.Message);
            }
        }

        private async Task SaveSolutionParametersAsync()
        {
            if (!_isAdmin) { ShowError("需要管理员权限。"); return; }
            try
            {
                await _app.Vision.SaveSolutionAsync(CancellationToken.None);
                SetMessage("方案参数已写入 .sol 文件。", Theme.Ok);
            }
            catch (Exception ex)
            {
                ShowError("保存方案失败：" + ex.Message);
            }
        }

        private void ReportParameterResult(ParameterApplyResult result, string operation)
        {
            if (result == null) return;
            if (result.Success)
            {
                SetMessage(string.Format("{0}成功，已应用 {1} 项。", operation, result.Applied), Theme.Ok);
                _app.Logger.Audit(_app.Auth.CurrentUser == null ? "-" : _app.Auth.CurrentUser.UserName,
                    operation, "Applied=" + result.Applied);
            }
            else
            {
                ShowError(string.Format("{0}完成，但有 {1} 项失败：\r\n{2}", operation,
                    result.Failures.Count, string.Join("\r\n", result.Failures.Take(12))));
            }
        }

        private void ManageSolutions()
        {
            if (!_isAdmin) return;
            using (var form = new SolutionManagerForm(_app.Settings.SolutionCatalog))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                _app.Settings.SolutionCatalog = form.Solutions;
                _app.SaveSettings();
                RefreshSolutionCombo();
                SetMessage("方案列表已更新。", Theme.Ok);
            }
        }

        private async Task ShowSettingsAsync(bool communication = false)
        {
            if (!_isAdmin)
            {
                ShowError("仅管理员可以修改系统设置。");
                return;
            }

            using (var form = new SettingsForm(_app.Settings, _app.Auth, _app, communication))
            {
                var result = form.ShowDialog(this);
                if (result != DialogResult.OK && !form.Applied) return;
                try
                {
                    if (!form.Applied) await _app.ApplySettingsAsync(form.Settings);
                    ApplySettingsToUi();
                    ApplySourceMode(_app.Settings.Input.Mode, false);
                    UpdateStatusBar();
                    SetMessage("配置已立即应用，检测已停止。无需重启软件。", Theme.Ok);
                }
                catch (Exception ex)
                {
                    ShowError("保存设置失败：" + ex.Message);
                }
            }
        }

        private void ShowReport()
        {
            using (var form = new ReportForm(_app.Report, _app.ReportDirectory))
                form.ShowDialog(this);
        }

        private async void ToggleLogin()
        {
            if (!_app.Auth.IsAuthenticated)
            {
                using (var login = new LoginForm(_app.Auth, _app.Logger, _app.Settings.LastUserName))
                {
                    if (login.ShowDialog(this) != DialogResult.OK) return;
                }
                _app.Settings.LastUserName = _app.Auth.CurrentUser == null ? string.Empty : _app.Auth.CurrentUser.UserName;
                _app.SaveSettings();
                RefreshUserUi();
                SetMessage("登录成功。", Theme.Ok);
                return;
            }

            if (_app.Detection.State == DetectionRunState.Running ||
                _app.Detection.State == DetectionRunState.Paused)
            {
                if (MessageBox.Show("注销前必须停止检测，是否停止并继续注销？", "注销",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                try { await _app.Detection.StopAsync(); }
                catch (Exception ex) { ShowError("停止检测失败，无法注销：" + ex.Message); return; }
            }

            _app.Auth.Logout();
            RefreshUserUi();
            Hide();
            using (var login = new LoginForm(_app.Auth, _app.Logger, _app.Settings.LastUserName))
            {
                if (login.ShowDialog() != DialogResult.OK)
                {
                    Show();
                    Close();
                    return;
                }
            }
            _app.Settings.LastUserName = _app.Auth.CurrentUser == null ? string.Empty : _app.Auth.CurrentUser.UserName;
            _app.SaveSettings();
            Show();
            RefreshUserUi();
            SetMessage("已切换用户：" + _app.Auth.CurrentUser.RoleText, Theme.Ok);
            if (_app.Auth.IsAdministrator && _solution != null)
                _ = RefreshParameterModulesAsync();
        }

        private void OnDetectionStateChanged(object sender, DetectionStateChangedEventArgs e)
        {
            RunOnUi(() =>
            {
                _lblStatusState.Text = "● " + Theme.StateText(e.NewState, _app.Algorithm.IsLoaded) +
                (_app.Algorithm.IsLoaded ? " · VM" : string.Empty);
                _lblStatusState.ForeColor = Theme.StateColor(e.NewState);
                SetMessage(e.Message, e.NewState == DetectionRunState.Fault ? Theme.Ng :
                    e.NewState == DetectionRunState.Running ? Theme.Ok : Theme.TextMuted);
                UpdateControlStates();

                if (e.NewState == DetectionRunState.Fault)
                    MessageBox.Show(this, e.Message, "检测异常", MessageBoxButtons.OK, MessageBoxIcon.Error);
            });
        }

        private void OnDetectionCompleted(object sender, DetectionCompletedEventArgs e)
        {
            RunOnUi(() => UpdateResultUi(e.Context));
        }

        private void OnDetectionError(object sender, DetectionErrorEventArgs e)
        {
            RunOnUi(() =>
            {
                _lblStatusState.Text = "● 异常";
                _lblStatusState.ForeColor = Theme.Ng;
                SetMessage(e.Message, Theme.Ng);
                AppendLog("ERROR", "Detection", e.Message);
            });
        }

        private void OnDetectionNotice(object sender, DetectionNoticeEventArgs e)
        {
            RunOnUi(() =>
            {
                SetMessage(e.Message, Theme.TextMuted);
                AppendLog("INFO", "Detection", e.Message);
            });
        }

        private void UpdateResultUi(DetectionContext context)
        {
            if (context == null || context.Inspection == null) return;

            bool ok = context.IsOk;
            _lblJudgeResult.Text = ok ? "OK" : "NG";
            _lblJudgeResult.ForeColor = ok ? Theme.Ok : Theme.Ng;
            _lblJudgeSource.Text = string.Format("{0} · {1:F1} ms · {2:HH:mm:ss.fff} · 判定来源：{3}",
                context.Record.Procedure, context.Inspection.DurationMs, context.Timestamp,
                context.Inspection.JudgeSource);
            if (_crosshairOverlay != null) _crosshairOverlay.SetResult(ok ? "OK" : "NG", ok);
            UpdateCrosshairOverlay();
            try
            {
                if (_vmRender != null) _vmRender.UpdateVMResultShow();
            }
            catch (Exception ex)
            {
                _app.Logger.Warn("UI", "VM 结果图像显示失败：" + ex.Message);
            }

            _dgvOutputs.SuspendLayout();
            try
            {
                _dgvOutputs.Rows.Clear();
                foreach (VisionOutputValue output in context.Inspection.Outputs)
                {
                    int index = _dgvOutputs.Rows.Add(output.Name, output.JoinedValue,
                        DetectionMapping.KindText(output.Kind), output.Source);
                    if (output.Kind == VisionValueKind.Boolean)
                    {
                        bool value;
                        bool parsed = bool.TryParse(output.JoinedValue, out value);
                        _dgvOutputs.Rows[index].Cells[1].Style.ForeColor =
                            parsed && value ? Theme.Ok : Theme.Ng;
                    }
                }
            }
            finally
            {
                _dgvOutputs.ResumeLayout();
            }

            _dgvHistory.Rows.Insert(0,
                context.Timestamp.ToString("HH:mm:ss.fff"),
                ok ? "OK" : "NG",
                context.Inspection.DurationMs.ToString("F1"),
                context.InputSource);
            if (_dgvHistory.Rows.Count > 200)
                _dgvHistory.Rows.RemoveAt(_dgvHistory.Rows.Count - 1);
            _dgvHistory.Rows[0].Cells[1].Style.ForeColor = ok ? Theme.Ok : Theme.Ng;
            _dgvHistory.Rows[0].Cells[1].Style.Font = Theme.BoldFont;

            UpdateStatisticsUi();
            SetMessage(string.Format("第 {0} 次检测：{1}，耗时 {2:F1} ms。",
                context.Sequence, ok ? "OK" : "NG", context.Inspection.DurationMs),
                ok ? Theme.Ok : Theme.Ng);
        }

        private void UpdateStatisticsUi()
        {
            DetectionStatisticsSnapshot snapshot = _app.Statistics.Snapshot;
            _lblStatTotal.Text = "检测总数\r\n" + snapshot.Total;
            _lblStatOk.Text = "OK\r\n" + snapshot.Ok;
            _lblStatNg.Text = "NG\r\n" + snapshot.Ng;
            _lblStatYield.Text = "合格率\r\n" + snapshot.YieldRate.ToString("F2") + "%";
        }

        private void ClearResultUi()
        {
            _lblJudgeResult.Text = "等待检测";
            _lblJudgeResult.ForeColor = Theme.TextMuted;
            _lblJudgeSource.Text = "—";
            _dgvOutputs.Rows.Clear();
            _dgvHistory.Rows.Clear();
            if (_crosshairOverlay != null) _crosshairOverlay.ClearResult();
            UpdateStatisticsUi();
            try { if (_vmRender != null) _vmRender.UpdateVMResultShow(); } catch { }
        }

        private void OnCameraFrame(object sender, CameraFrameEventArgs e)
        {
            // Stopwatch 使用单调 64 位计数，避免 TickCount 在约 24.9 天翻转后预览长期冻结。
            if (_shutdownStarted || IsDisposed) return;
            long now = Stopwatch.GetTimestamp();
            long throttleTicks = Stopwatch.Frequency * (long)_app.Settings.General.PreviewThrottleMs / 1000;
            if (now - Interlocked.Read(ref _lastPreviewTick) < throttleTicks)
                return;
            Interlocked.Exchange(ref _lastPreviewTick, now);
            if (Interlocked.Exchange(ref _previewUpdating, 1) == 1) return;

            RawImage frame = e.Frame;
            RunOnUi(() =>
            {
                try { UpdateCameraPreview(frame); }
                finally { Interlocked.Exchange(ref _previewUpdating, 0); }
            });
        }

        private void UpdateCameraPreview(RawImage frame)
        {
            if (frame == null || _picCameraPreview == null || IsDisposed) return;
            try
            {
                Bitmap bitmap = frame.ToBitmap();
                Image previous = _picCameraPreview.Image;
                _picCameraPreview.Image = bitmap;
                if (previous != null) previous.Dispose();
            }
            catch (Exception ex)
            {
                _app.Logger.Warn("UI", "相机预览更新失败：" + ex.Message);
            }
        }

        private void OnCameraStatusChanged(object sender, CameraStatusEventArgs e)
        {
            RunOnUi(() =>
            {
                _lblCameraStatus.Text = e.Message;
                _lblStatusCamera.Text = "相机：" + StateShortText(e.State);
                _lblStatusCamera.ForeColor = e.State == DeviceConnectionState.Connected ? Theme.Ok :
                    e.State == DeviceConnectionState.Fault ? Theme.Ng : Theme.TextMuted;
                UpdateCameraButtons();
                UpdateControlStates();
                if (e.State == DeviceConnectionState.Connected)
                {
                    if (e.Device != null) _app.Settings.Input.LastCameraId = e.Device.Id;
                    _app.SaveSettings();
                }
            });
        }

        private void OnCommunicationStatusChanged(object sender, CommunicationStatusEventArgs e)
        {
            RunOnUi(() =>
            {
                _commStates[e.Channel] = e.State;
                UpdateCommunicationStatusLabels();
            });
        }

        private void UpdateCommunicationStatusLabels()
        {
            DeviceConnectionState modbus;
            DeviceConnectionState serial;
            DeviceConnectionState tcp;
            _commStates.TryGetValue(CommunicationChannel.ModbusTcp, out modbus);
            _commStates.TryGetValue(CommunicationChannel.Serial, out serial);
            _commStates.TryGetValue(CommunicationChannel.TcpTrigger, out tcp);

            _lblStatusModbus.Text = "● Modbus：" + StateShortText(modbus);
            _lblStatusModbus.ForeColor = ConnectionColor(modbus);
            _lblStatusSerial.Text = "● 串口：" + StateShortText(serial);
            _lblStatusSerial.ForeColor = ConnectionColor(serial);
            _lblStatusTcp.Text = "● TCP：" + StateShortText(tcp);
            _lblStatusTcp.ForeColor = ConnectionColor(tcp);
        }

        private static Color ConnectionColor(DeviceConnectionState state)
        {
            return state == DeviceConnectionState.Connected ? Theme.Ok : state == DeviceConnectionState.Listening ? Theme.Accent :
                state == DeviceConnectionState.Fault ? Theme.Ng : Theme.TextMuted;
        }

        private async void OnExternalTriggered(object sender, TriggerEventArgs e)
        {
            try
            {
                if (IsDisposed || _shutdownStarted || _app.IsApplyingSettings || !Visible || !Enabled || !_app.Auth.IsAuthenticated) return;
                await RunOnUiAsync(async () =>
                {
                    // 模态配置/报表/登录窗口打开时不接受触发；忙碌或退出时也不得旁路启动。
                    if (_triggerStarting || _busy || _solutionLoading || _shutdownStarted ||
                        _app.IsShutdownPending || _app.IsApplyingSettings || !Visible || !Enabled || !_app.Auth.IsAuthenticated) return;
                    if (_app.Detection.State != DetectionRunState.Idle || !_app.Algorithm.IsLoaded) return;

                    _triggerStarting = true;
                    try
                    {
                        IInputProvider provider = CreateInputProvider();
                        _segDetectionMode.SelectedIndex = 0;
                        SetMessage("外部触发：" + e.Source + "，启动单次检测。", Theme.Accent);
                        await _app.Detection.StartAsync(DetectionMode.Single, provider);
                    }
                    catch (Exception ex)
                    {
                        SetMessage("外部触发启动失败：" + ex.Message, Theme.Ng);
                        _app.Logger.Error("UI", "外部触发启动失败。", ex);
                    }
                    finally
                    {
                        _triggerStarting = false;
                    }
                });
            }
            catch (Exception ex)
            {
                _app.Logger.Error("UI", "外部触发处理异常。", ex);
            }
        }

        private void OnLogEntry(object sender, LogEntryEventArgs e)
        {
            if (e.Level == LogLevel.Debug) return;
            RunOnUi(() => AppendLog(e.Level.ToString().ToUpperInvariant(), e.Area,
                string.Format("{0:HH:mm:ss} {1}", e.Timestamp, e.Message)));
        }

        private void AppendLog(string level, string area, string message)
        {
            if (_txtSystemLog == null || _txtSystemLog.IsDisposed) return;
            string line = string.Format("[{0}] [{1}] [{2}] {3}",
                DateTime.Now.ToString("HH:mm:ss"), level, area, message);
            _txtSystemLog.AppendText(line + Environment.NewLine);
            if (_txtSystemLog.Lines.Length > 800)
            {
                var lines = _txtSystemLog.Lines;
                _txtSystemLog.Text = string.Join(Environment.NewLine, lines.Skip(lines.Length - 500));
                _txtSystemLog.SelectionStart = _txtSystemLog.TextLength;
                _txtSystemLog.ScrollToCaret();
            }
        }

        private void UpdateStatusBar()
        {
            if (_lblStatusState == null) return;
            _lblStatusState.Text = "● " + Theme.StateText(_app.Detection.State, _app.Algorithm.IsLoaded) +
                (_app.Algorithm.IsLoaded ? " · VM" : string.Empty);
            _lblStatusState.ForeColor = Theme.StateColor(_app.Detection.State);
            _lblStatusSolution.Text = "方案：" + (_solution == null
                ? "未加载" : Path.GetFileName(_solution.SolutionPath ?? string.Empty));
            _lblStatusCamera.Text = "相机：" + StateShortText(_app.Camera.State);
            _lblStatusCamera.ForeColor = _app.Camera.State == DeviceConnectionState.Connected ? Theme.Ok :
                _app.Camera.State == DeviceConnectionState.Fault ? Theme.Ng : Theme.TextMuted;
            UpdateCommunicationStatusLabels();
            UpdateCameraButtons();
        }

        private void UpdateClock()
        {
            if (_lblClock != null)
                _lblClock.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private static string StateShortText(DeviceConnectionState state)
        {
            switch (state)
            {
                case DeviceConnectionState.Connected: return "已连接";
                case DeviceConnectionState.Listening: return "监听中";
                case DeviceConnectionState.Connecting: return "连接中";
                case DeviceConnectionState.Reconnecting: return "重连中";
                case DeviceConnectionState.Fault: return "故障";
                case DeviceConnectionState.Disabled: return "未启用";
                default: return "未连接";
            }
        }

        private void RefreshSolutionCombo()
        {
            string selectedPath = _cboSolution.SelectedItem is SolutionOption
                ? ((SolutionOption)_cboSolution.SelectedItem).Path
                : _app.Settings.LastSolutionPath;

            var paths = new List<string>();
            if (_app.Settings.SolutionCatalog != null) paths.AddRange(_app.Settings.SolutionCatalog);
            if (!string.IsNullOrWhiteSpace(_app.Settings.LastSolutionPath)) paths.Add(_app.Settings.LastSolutionPath);

            var options = paths
                .Where(x => !string.IsNullOrWhiteSpace(x) && File.Exists(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => Path.GetFileName(x), StringComparer.CurrentCulture)
                .Select(x => new SolutionOption
                {
                    Path = x,
                    Display = Path.GetFileName(x) + "   [" + Path.GetDirectoryName(x) + "]"
                })
                .ToList();

            _cboSolution.Items.Clear();
            foreach (SolutionOption option in options) _cboSolution.Items.Add(option);

            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                for (int i = 0; i < _cboSolution.Items.Count; i++)
                {
                    if (string.Equals(((SolutionOption)_cboSolution.Items[i]).Path, selectedPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        _cboSolution.SelectedIndex = i;
                        break;
                    }
                }
            }

            if (_cboSolution.SelectedIndex < 0 && _cboSolution.Items.Count > 0)
                _cboSolution.SelectedIndex = 0;

            _btnLoadSolution.Enabled = _isAdmin && !_busy;
            UpdateControlStates();
        }

        private string PickSolutionFile()
        {
            string initialDirectory = string.Empty;
            SolutionOption selected = _cboSolution.SelectedItem as SolutionOption;
            string current = selected != null ? selected.Path : _app.Settings.LastSolutionPath;
            if (!string.IsNullOrWhiteSpace(current) && File.Exists(current))
                initialDirectory = Path.GetDirectoryName(current);

            using (var dialog = new OpenFileDialog
            {
                Filter = "VisionMaster 方案 (*.sol)|*.sol|所有文件 (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false,
                Title = "打开 VisionMaster 方案 (.sol)",
                InitialDirectory = string.IsNullOrWhiteSpace(initialDirectory) ? string.Empty : initialDirectory
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return null;
                string extension = Path.GetExtension(dialog.FileName);
                if (!string.Equals(extension, ".sol", StringComparison.OrdinalIgnoreCase))
                {
                    ShowError("请选择 VisionMaster 方案文件（扩展名 .sol）。");
                    return null;
                }
                return dialog.FileName;
            }
        }

        private void RegisterSolution(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (_app.Settings.SolutionCatalog == null)
                _app.Settings.SolutionCatalog = new List<string>();
            if (!_app.Settings.SolutionCatalog.Any(x =>
                    string.Equals(x, path, StringComparison.OrdinalIgnoreCase)))
                _app.Settings.SolutionCatalog.Add(path);
            _app.SaveSettings();
            RefreshSolutionCombo();
        }

        private void SelectSolutionPath(string path)
        {
            for (int i = 0; i < _cboSolution.Items.Count; i++)
            {
                if (string.Equals(((SolutionOption)_cboSolution.Items[i]).Path, path,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _cboSolution.SelectedIndex = i;
                    return;
                }
            }
        }

        private void SetMessage(string message, Color color)
        {
            if (_lblMessage == null || _lblMessage.IsDisposed) return;
            _lblMessage.Text = message ?? string.Empty;
            _lblMessage.ForeColor = color;
        }

        private void ShowError(string message)
        {
            SetMessage(message, Theme.Ng);
            MessageBox.Show(this, message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void RunOnUi(Action action)
        {
            if (action == null || IsDisposed) return;
            try
            {
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private Task RunOnUiAsync(Func<Task> action)
        {
            if (action == null) return Task.CompletedTask;
            if (!InvokeRequired) return action();

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        await action();
                        tcs.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                }));
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
            return tcs.Task;
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_shutdownComplete) return;
            e.Cancel = true;
            if (_shutdownStarted) return;

            if (_app.Detection.State == DetectionRunState.Running ||
                _app.Detection.State == DetectionRunState.Paused)
            {
                if (MessageBox.Show("检测正在运行，确定停止并退出？", "退出",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
            }
            else if (_app.Settings.General.ConfirmBeforeExit &&
                     MessageBox.Show("确定退出 X Vision？", "退出",
                         MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            _shutdownStarted = true;
            _clockTimer.Stop();
            _busy = true;
            UpdateControlStates();
            SetMessage("正在释放资源，请稍候...", Theme.Warning);

            ShutdownAndCloseAsync();
        }

        private async void ShutdownAndCloseAsync()
        {
            try
            {
                // 主题过渡和设置落盘结束后再释放控件，避免关闭与重绘并发。
                await _themeSwitchTask;
                // 先停检测，再保存参数，避免与 SyncRun 并发访问 VM。
                // 退出路径不允许无限等待：单步 3 秒超时后继续走统一异步释放。
                bool detectionStopped = false;
                try
                {
                    Task stopTask = _app.Detection.StopAsync();
                    Task completed = await Task.WhenAny(stopTask, Task.Delay(3000));
                    if (completed == stopTask)
                    {
                        await stopTask;
                        detectionStopped = true;
                    }
                    else
                    {
                        _app.Logger.Error("Shutdown", "退出时停止检测超时（3000 ms），强制进入释放流程。");
                    }
                }
                catch (Exception ex)
                {
                    _app.Logger.Error("Shutdown", "退出时停止检测异常，继续释放。", ex);
                }

                if (detectionStopped && _isAdmin && _app.Settings.Vision.AutoSaveParameterChanges &&
                    _app.Vision.IsLoaded)
                {
                    try { await _app.Vision.SaveSolutionAsync(CancellationToken.None); }
                    catch (Exception ex) { _app.Logger.Warn("UI", "退出前保存方案失败：" + ex.Message); }
                }

                // 流程停止后才解除 UI 对方案的引用，再按顺序关闭 VM、相机和通讯。
                if (_crosshairOverlay != null) _crosshairOverlay.Hide();
                if (_vmRender != null) _vmRender.ModuleSource = null;
                DisposeParameterControl();
                await _app.ShutdownAsync();
                _shutdownComplete = true;
                Close();
            }
            catch (Exception ex)
            {
                _app.Logger.Error("Shutdown", "退出清理失败。", ex);
                // 原生流程未结束时，保持窗口和设备资源存活；不强制释放正在使用的 VM。
                _shutdownStarted = false;
                _busy = false;
                _clockTimer.Start();
                UpdateControlStates();
                SetMessage("退出未完成：请等待当前流程结束后再次退出。" + ex.Message, Theme.Ng);
                MessageBox.Show(this, "退出未完成：" + ex.Message +
                    "\r\n资源仍由程序持有，请等待流程结束后再次退出。", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>产线快捷键：F1 单次、F2 连续、F3 停止。仅在主窗体处于活动状态时生效。</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F1)
            {
                _ = StartDetectionAsync(DetectionMode.Single);
                return true;
            }
            if (keyData == Keys.F2)
            {
                _ = StartDetectionAsync(DetectionMode.Continuous);
                return true;
            }
            if (keyData == Keys.F3)
            {
                _ = StopDetectionAsync();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void OnFormClosed(object sender, FormClosedEventArgs e)
        {
            // 服务的生命周期可能长于窗体，显式解绑避免保留窗口、预览帧及排队回调。
            _app.Detection.StateChanged -= OnDetectionStateChanged;
            _app.Detection.DetectionCompleted -= OnDetectionCompleted;
            _app.Detection.ErrorOccurred -= OnDetectionError;
            _app.Detection.Notice -= OnDetectionNotice;
            _app.Camera.FrameReceived -= OnCameraFrame;
            _app.Camera.StatusChanged -= OnCameraStatusChanged;
            _app.Communication.StatusChanged -= OnCommunicationStatusChanged;
            _app.ExternalTriggers.Triggered -= OnExternalTriggered;
            _app.Logger.EntryWritten -= OnLogEntry;
            _clockTimer.Stop();
            _clockTimer.Dispose();

            try { if (_vmRender != null) _vmRender.ModuleSource = null; } catch { }
            try { if (_crosshairOverlay != null) _crosshairOverlay.Dispose(); } catch { }
            try { if (_vmRender != null) _vmRender.Dispose(); } catch { }
            try { DisposeParameterControl(); } catch { }
            try
            {
                if (_picCameraPreview != null && _picCameraPreview.Image != null)
                    _picCameraPreview.Image.Dispose();
            }
            catch { }
        }

        private static int PlaybackIndex(PlaybackMode mode)
        {
            if (mode == PlaybackMode.Manual) return 0;
            if (mode == PlaybackMode.Sequential) return 1;
            return 2;
        }

        private static int TriggerIndex(CameraTriggerMode mode)
        {
            if (mode == CameraTriggerMode.Hardware) return 1;
            if (mode == CameraTriggerMode.Continuous) return 2;
            return 0;
        }

        private static decimal ClampNumeric(NumericUpDown numeric, decimal value)
        {
            if (value < numeric.Minimum) return numeric.Minimum;
            if (value > numeric.Maximum) return numeric.Maximum;
            return value;
        }

        private static string ParameterSafeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unnamed";
            char[] invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
        }
    }
}
