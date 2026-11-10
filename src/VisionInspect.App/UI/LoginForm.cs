using System;
using System.Drawing;
using System.Windows.Forms;
using VisionInspect.Core;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Models;

namespace VisionInspect.App.UI
{
    /// <summary>登录窗体。登录成功后才创建主界面，避免操作员误操作配置。</summary>
    public sealed class LoginForm : Form
    {
        private readonly IAuthService _auth;
        private readonly IAppLogger _logger;
        private readonly TextBox _txtUser;
        private readonly TextBox _txtPassword;
        private readonly ComboBox _cboRole;
        private readonly Label _lblError;
        private readonly Button _btnLogin;

        public LoginForm(IAuthService auth, IAppLogger logger, string lastUserName = null)
        {
            _auth = auth ?? throw new ArgumentNullException(nameof(auth));
            _logger = logger;

            Theme.StyleForm(this);
            Text = "X Vision 登录";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(560, 470);
            BackColor = Theme.Background;

            var pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 116,
                BackColor = Theme.Surface,
                Padding = new Padding(28, 18, 20, 10)
            };
            pnlHeader.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, pnlHeader.Height - 1, pnlHeader.Width, pnlHeader.Height - 1);
            };
            var picLogo = new PictureBox
            {
                Location = new Point(26, 32),
                Size = new Size(52, 52),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Image = BrandAssets.LogoForTheme(Theme.IsDark)
            };
            var lblTitle = new Label
            {
                Text = "X Vision",
                Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold),
                ForeColor = Theme.Text,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(94, 35)
            };
            var lblProduct = new Label
            {
                Text = "视觉检测系统  ·  V3.0.1",
                Font = Theme.SmallFont,
                ForeColor = Theme.TextMuted,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(96, 75)
            };
            pnlHeader.Controls.Add(picLogo);
            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(lblProduct);

            var lblRole = Theme.MakeLabel("登录身份", false, true);
            lblRole.Location = new Point(48, 160);
            _cboRole = new VisionInspect.App.UI.Controls.StudioComboBox
            {
                Location = new Point(150, 155),
                Width = 220,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            Theme.StyleCombo(_cboRole);
            _cboRole.Items.AddRange(new object[] { "操作员", "管理员" });
            bool lastWasAdmin = string.Equals(lastUserName, "admin", StringComparison.OrdinalIgnoreCase);
            _cboRole.SelectedIndex = lastWasAdmin ? 1 : 0;
            _cboRole.SelectedIndexChanged += RoleChanged;

            var lblUser = Theme.MakeLabel("账号", false, true);
            lblUser.Location = new Point(48, 208);
            _txtUser = new TextBox { Location = new Point(150, 203), Width = 350, Height = 30 };
            Theme.StyleTextBox(_txtUser);
            _txtUser.Text = string.IsNullOrWhiteSpace(lastUserName)
                ? (lastWasAdmin ? "admin" : "operator")
                : lastUserName;

            var lblPassword = Theme.MakeLabel("密码", false, true);
            lblPassword.Location = new Point(48, 256);
            _txtPassword = new TextBox
            {
                Location = new Point(150, 251),
                Width = 350,
                Height = 30,
                UseSystemPasswordChar = true
            };
            Theme.StyleTextBox(_txtPassword);

            _lblError = new Label
            {
                Location = new Point(150, 290),
                Width = 350,
                Height = 30,
                ForeColor = Theme.Ng,
                Font = Theme.SmallFont,
                Text = string.Empty
            };

            _btnLogin = new Controls.RoundedButton
            {
                Text = "登 录",
                Location = new Point(150, 334),
                Width = 220,
                Height = 40
            };
            Theme.StyleButton(_btnLogin, Theme.ButtonKind.Primary);
            _btnLogin.Click += Login_Click;

            var btnCancel = new Controls.RoundedButton
            {
                Text = "退 出",
                Location = new Point(390, 334),
                Width = 100,
                Height = 40,
                DialogResult = DialogResult.Cancel
            };
            Theme.StyleButton(btnCancel, Theme.ButtonKind.Ghost);

            var separator = new Panel
            {
                Location = new Point(30, 420),
                Width = 500,
                Height = 1,
                BackColor = Theme.Border
            };
            var lblFooter = new Label
            {
                Text = "V3.0.1    作者：wzhoucheng",
                Location = new Point(30, 432),
                Size = new Size(500, 20),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.TextMuted,
                Font = Theme.SmallFont
            };

            Controls.Add(pnlHeader);
            Controls.Add(lblRole);
            Controls.Add(_cboRole);
            Controls.Add(lblUser);
            Controls.Add(_txtUser);
            Controls.Add(lblPassword);
            Controls.Add(_txtPassword);
            Controls.Add(_lblError);
            Controls.Add(_btnLogin);
            Controls.Add(btnCancel);
            Controls.Add(separator);
            Controls.Add(lblFooter);

            AcceptButton = _btnLogin;
            CancelButton = btnCancel;
            Shown += (s, e) =>
            {
                _txtUser.SelectAll();
                _txtUser.Focus();
            };
        }

        private void RoleChanged(object sender, EventArgs e)
        {
            if (_txtUser == null) return;
            string current = _txtUser.Text;
            if (string.IsNullOrWhiteSpace(current) ||
                string.Equals(current, "admin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "operator", StringComparison.OrdinalIgnoreCase))
            {
                _txtUser.Text = SelectedRole == UserRole.Administrator ? "admin" : "operator";
            }
        }

        private UserRole SelectedRole
        {
            get { return _cboRole.SelectedIndex == 1 ? UserRole.Administrator : UserRole.Operator; }
        }

        private void Login_Click(object sender, EventArgs e)
        {
            _lblError.Text = string.Empty;
            _btnLogin.Enabled = false;
            try
            {
                AuthResult result = _auth.Login(_txtUser.Text, _txtPassword.Text);
                if (!result.Success)
                {
                    _lblError.Text = result.Message;
                    _txtPassword.Clear();
                    _txtPassword.Focus();
                    return;
                }

                // 登录身份是现场操作边界，必须与账号实际角色一致；
                // 防止操作员误选“管理员”后继续操作管理员功能。
                if (result.Session == null || result.Session.Role != SelectedRole)
                {
                    _auth.Logout();
                    _lblError.Text = "所选登录身份与账号权限不一致，请重新选择。";
                    _txtPassword.Clear();
                    _txtPassword.Focus();
                    return;
                }

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                if (_logger != null) _logger.Error("UI", "登录过程异常。", ex);
                _lblError.Text = "登录异常：" + ex.Message;
            }
            finally
            {
                _btnLogin.Enabled = true;
            }
        }
    }
}
