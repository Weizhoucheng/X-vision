using System;
using System.Configuration;

namespace VisionInspect.Infrastructure.Security
{
    /// <summary>
    /// 管理员密码存储。按现场要求使用 App.config/appSettings 的明文
    /// AdminPassword 键，默认值 admin；不启用复杂度/长度/哈希校验。
    /// </summary>
    public sealed class AdminPasswordStore
    {
        public const string Key = "AdminPassword";
        public const string DefaultPassword = "admin";

        public string Read()
        {
            try
            {
                string value = ConfigurationManager.AppSettings[Key];
                return string.IsNullOrEmpty(value) ? DefaultPassword : value;
            }
            catch
            {
                return DefaultPassword;
            }
        }

        /// <summary>修改管理员密码，不需要旧密码；写回当前可执行程序的 App.config。</summary>
        public void Write(string newPassword)
        {
            if (string.IsNullOrEmpty(newPassword))
                throw new ArgumentException("管理员密码不能为空。", "newPassword");

            System.Configuration.Configuration config =
                ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            KeyValueConfigurationCollection settings = config.AppSettings.Settings;
            if (settings[Key] == null)
                settings.Add(Key, newPassword);
            else
                settings[Key].Value = newPassword;

            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }
    }
}
