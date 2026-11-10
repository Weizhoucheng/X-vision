using System;

namespace VisionInspect.Core.Models
{
    /// <summary>
    /// 本地用户账号。密码只保存 PBKDF2 派生值和随机盐，不落明文。
    /// </summary>
    public sealed class UserAccount
    {
        public string UserName { get; set; }
        public string DisplayName { get; set; }
        public UserRole Role { get; set; }
        public bool Enabled { get; set; }
        public string Salt { get; set; }
        public string PasswordHash { get; set; }
        public int Iterations { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? LastLoginUtc { get; set; }

        public UserAccount()
        {
            UserName = string.Empty;
            DisplayName = string.Empty;
            Enabled = true;
            Salt = string.Empty;
            PasswordHash = string.Empty;
            Iterations = 100000;
            CreatedAtUtc = DateTime.UtcNow;
        }
    }

    public sealed class UserSession
    {
        public UserSession(string userName, string displayName, UserRole role)
        {
            UserName = userName;
            DisplayName = displayName;
            Role = role;
            LoginTime = DateTime.Now;
        }

        public string UserName { get; private set; }
        public string DisplayName { get; private set; }
        public UserRole Role { get; private set; }
        public DateTime LoginTime { get; private set; }

        public bool IsAdministrator
        {
            get { return Role == UserRole.Administrator; }
        }

        public string RoleText
        {
            get { return IsAdministrator ? "管理员" : "操作员"; }
        }

        public string DisplayText
        {
            get { return string.Format("{0}（{1}）", DisplayName, RoleText); }
        }
    }

    public sealed class AuthResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public UserSession Session { get; set; }

        public AuthResult()
        {
            Message = string.Empty;
        }
    }
}
