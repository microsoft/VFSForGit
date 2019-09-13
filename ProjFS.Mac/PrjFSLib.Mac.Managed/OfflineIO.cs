using System;
namespace PrjFSLib.Mac.Managed
{
    public class OfflineIO
    {
        public static bool RegisterForOfflineIO()
        {
            return Result.Success == Interop.PrjFSLib.RegisterForOfflineIO();
        }

        public static bool UnregisterForOfflineIO()
        {
            return Result.Success == Interop.PrjFSLib.UnregisterForOfflineIO();
        }
    }
}
