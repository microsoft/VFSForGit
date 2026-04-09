using System;

namespace GVFS.Common
{
    public static class EpochConverter
    {
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static long ToUnixEpochSeconds(DateTime datetime)
        {
            return Convert.ToInt64(Math.Truncate((datetime - UnixEpoch).TotalSeconds));
        }

        public static DateTime FromUnixEpochSeconds(long secondsSinceEpoch)
        {
            return UnixEpoch.AddSeconds(secondsSinceEpoch);
        }
    }
}