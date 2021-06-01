namespace GVFS.Common.Tracing
{
    public static class TracingConstants
    {
        public static class MessageKey
        {
            public const string LogAlwaysMessage = ErrorMessage;
            public const string CriticalMessage = ErrorMessage;
            public const string ErrorMessage = "ErrorMessage";
            public const string WarningMessage = "WarningMessage";
            public const string InfoMessage = "Message";
            public const string VerboseMessage = InfoMessage;
        }
    }
}
