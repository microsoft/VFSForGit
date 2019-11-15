using System;

namespace GVFS.Service.UI
{
    public interface IToastNotifier
    {
        Action<string> UserResponseCallback { get; set; }
        void Notify(string title, string message, string actionButtonTitle, string callbackArgs);
    }
}
