using System;
using VisionInspect.Core.Models;

namespace VisionInspect.Core.Abstractions
{
    public interface IAuthService
    {
        event EventHandler UserChanged;
        UserSession CurrentUser { get; }
        bool IsAuthenticated { get; }
        bool IsAdministrator { get; }

        AuthResult Login(string userName, string password);
        void Logout();
        bool ChangePassword(string oldPassword, string newPassword, out string message);
    }
}
