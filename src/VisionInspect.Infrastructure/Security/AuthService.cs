using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using VisionInspect.Core;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Models;
using VisionInspect.Infrastructure.Configuration;

namespace VisionInspect.Infrastructure.Security
{
    /// <summary>
    /// 两级权限（操作员/管理员）的本地账号服务。
    /// 密码使用 PBKDF2-SHA256（100,000 次）+ 32 字节随机盐，仅保存派生哈希。
    /// 首次运行自动创建演示账号，现场必须在设置中修改默认密码。
    /// </summary>
    public sealed class AuthService : IAuthService
    {
        private const int DefaultIterations = 100000;
        private const int SaltSize = 32;
        private const int HashSize = 32;

        private readonly object _sync = new object();
        private readonly string _usersFile;
        private readonly IAppLogger _logger;
        private readonly AdminPasswordStore _adminPasswordStore;
        private List<UserAccount> _users;
        private UserSession _currentUser;

        public AuthService(string usersFile, IAppLogger logger)
            : this(usersFile, logger, new AdminPasswordStore())
        {
        }

        public AuthService(string usersFile, IAppLogger logger, AdminPasswordStore adminPasswordStore)
        {
            if (string.IsNullOrWhiteSpace(usersFile)) throw new ArgumentException("usersFile");
            _usersFile = usersFile;
            _logger = logger;
            _adminPasswordStore = adminPasswordStore ?? new AdminPasswordStore();
            LoadOrSeed();
        }

        public event EventHandler UserChanged;

        public UserSession CurrentUser
        {
            get { lock (_sync) return _currentUser; }
        }

        public bool IsAuthenticated
        {
            get { lock (_sync) return _currentUser != null; }
        }

        public bool IsAdministrator
        {
            get
            {
                lock (_sync)
                    return _currentUser != null && _currentUser.Role == UserRole.Administrator;
            }
        }

        public AuthResult Login(string userName, string password)
        {
            UserSession session = null;
            string failure = null;

            lock (_sync)
            {
                if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
                {
                    failure = "用户名或密码不能为空。";
                }
                else
                {
                    UserAccount account = _users.FirstOrDefault(x =>
                        string.Equals(x.UserName, userName.Trim(), StringComparison.OrdinalIgnoreCase));

                    if (account == null || !account.Enabled)
                    {
                        failure = "用户名不存在或账号已停用。";
                    }
                    else if (account.Role == UserRole.Administrator)
                    {
                        // 管理员密码按现场要求固定保存在 App.config/appSettings，
                        // 使用明文比较，不再走 PBKDF2，也不做复杂度/长度校验。
                        string expected = _adminPasswordStore.Read();
                        if (!string.Equals(password, expected, StringComparison.Ordinal))
                        {
                            failure = "管理员密码错误。";
                        }
                        else
                        {
                            account.LastLoginUtc = DateTime.UtcNow;
                            Save();
                            session = new UserSession(account.UserName, account.DisplayName, account.Role);
                            _currentUser = session;
                        }
                    }
                    else if (!VerifyPassword(password, account))
                    {
                        failure = "密码错误。";
                    }
                    else
                    {
                        account.LastLoginUtc = DateTime.UtcNow;
                        Save();
                        session = new UserSession(account.UserName, account.DisplayName, account.Role);
                        _currentUser = session;
                    }
                }
            }

            if (session == null)
            {
                if (_logger != null)
                    _logger.Warn("Auth", string.Format("登录失败：{0}，原因：{1}", userName, failure));
                if (_logger != null)
                    _logger.Audit(userName, "LoginFailed", failure);
                return new AuthResult { Success = false, Message = failure };
            }

            if (_logger != null)
            {
                _logger.Info("Auth", string.Format("登录成功：{0}（{1}）", session.UserName, session.RoleText));
                _logger.Audit(session.UserName, "Login", session.RoleText);
            }

            RaiseUserChanged();
            return new AuthResult { Success = true, Message = "登录成功。", Session = session };
        }

        public void Logout()
        {
            UserSession old;
            lock (_sync)
            {
                old = _currentUser;
                _currentUser = null;
            }

            if (old != null && _logger != null)
            {
                _logger.Info("Auth", "用户登出：" + old.UserName);
                _logger.Audit(old.UserName, "Logout", string.Empty);
            }

            RaiseUserChanged();
        }

        public bool ChangePassword(string oldPassword, string newPassword, out string message)
        {
            UserSession session;
            lock (_sync)
            {
                session = _currentUser;
                if (session == null)
                {
                    message = "请先登录。";
                    return false;
                }
                if (string.IsNullOrEmpty(newPassword))
                {
                    message = "新密码不能为空。";
                    return false;
                }

                UserAccount account = _users.FirstOrDefault(x =>
                    string.Equals(x.UserName, session.UserName, StringComparison.OrdinalIgnoreCase));
                if (account == null)
                {
                    message = "账号不存在。";
                    return false;
                }

                try
                {
                    if (account.Role == UserRole.Administrator)
                    {
                        // 管理员密码写入 App.config；不验证旧密码、不校验复杂度。
                        _adminPasswordStore.Write(newPassword);
                    }
                    else
                    {
                        // 操作员继续使用本地随机盐哈希，保持旧账号兼容；不限制长度/复杂度。
                        byte[] salt = RandomBytes(SaltSize);
                        byte[] hash = Derive(newPassword, salt, DefaultIterations);
                        account.Salt = Convert.ToBase64String(salt);
                        account.PasswordHash = Convert.ToBase64String(hash);
                        account.Iterations = DefaultIterations;
                        Save();
                    }
                }
                catch (Exception ex)
                {
                    if (_logger != null) _logger.Error("Auth", "修改密码失败。", ex);
                    message = "修改失败：" + ex.Message;
                    return false;
                }
            }

            if (_logger != null)
                _logger.Audit(session.UserName, "ChangePassword", "成功");
            message = "密码修改成功。";
            return true;
        }

        private void LoadOrSeed()
        {
            lock (_sync)
            {
                if (File.Exists(_usersFile))
                {
                    try
                    {
                        _users = JsonHelper.LoadFile<List<UserAccount>>(_usersFile);
                        if (_users == null || _users.Count == 0 || _users.Any(x => x == null ||
                            string.IsNullOrWhiteSpace(x.UserName) || string.IsNullOrWhiteSpace(x.Salt) ||
                            string.IsNullOrWhiteSpace(x.PasswordHash) || !Enum.IsDefined(typeof(UserRole), x.Role)))
                            throw new InvalidDataException("用户文件包含空记录或无效账号。");
                    }
                    catch (Exception ex)
                    {
                        string backup = _usersFile + ".corrupt." + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        try { File.Copy(_usersFile, backup, true); } catch { }
                        if (_logger != null) _logger.Error("Auth", "用户文件损坏，已拒绝重置账号。备份：" + backup, ex);
                        // 已部署用户库损坏时不能静默恢复公开默认管理员密码。
                        throw new VisionInspectException("用户文件无效，请从备份恢复用户文件：" + _usersFile, ex);
                    }
                }

                if (_users == null || _users.Count == 0)
                {
                    _users = new List<UserAccount>();
                    _users.Add(CreateAccount("admin", AdminPasswordStore.DefaultPassword, "系统管理员", UserRole.Administrator));
                    _users.Add(CreateAccount("operator", "123456", "产线操作员", UserRole.Operator));
                    Save();
                    if (_logger != null)
                    {
                        _logger.Warn("Auth", "已创建首次运行账号，请立即修改默认密码（见部署说明）。");
                        _logger.Audit("system", "SeedUsers", "创建默认账号");
                    }
                }
            }
        }

        private static UserAccount CreateAccount(string userName, string password, string displayName, UserRole role)
        {
            byte[] salt = RandomBytes(SaltSize);
            byte[] hash = Derive(password, salt, DefaultIterations);
            return new UserAccount
            {
                UserName = userName,
                DisplayName = displayName,
                Role = role,
                Enabled = true,
                Salt = Convert.ToBase64String(salt),
                PasswordHash = Convert.ToBase64String(hash),
                Iterations = DefaultIterations,
                CreatedAtUtc = DateTime.UtcNow
            };
        }

        private static bool VerifyPassword(string password, UserAccount account)
        {
            if (account == null || string.IsNullOrEmpty(account.Salt) || string.IsNullOrEmpty(account.PasswordHash))
                return false;

            try
            {
                byte[] salt = Convert.FromBase64String(account.Salt);
                byte[] expected = Convert.FromBase64String(account.PasswordHash);
                byte[] actual = Derive(password, salt, Math.Max(10000, account.Iterations));
                if (expected.Length != actual.Length) return false;

                int diff = 0;
                for (int i = 0; i < expected.Length; i++)
                    diff |= expected[i] ^ actual[i];
                return diff == 0;
            }
            catch
            {
                return false;
            }
        }

        private static byte[] Derive(string password, byte[] salt, int iterations)
        {
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
            {
                return pbkdf2.GetBytes(HashSize);
            }
        }

        private static byte[] RandomBytes(int length)
        {
            byte[] bytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return bytes;
        }

        private void Save()
        {
            JsonHelper.SaveFileAtomic(_usersFile, _users);
        }

        private void RaiseUserChanged()
        {
            EventHandler handler = UserChanged;
            if (handler == null) return;
            try { handler(this, EventArgs.Empty); }
            catch (Exception ex)
            {
                if (_logger != null) _logger.Error("Auth", "UserChanged 事件异常。", ex);
            }
        }
    }
}
