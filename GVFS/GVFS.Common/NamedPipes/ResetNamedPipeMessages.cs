namespace GVFS.Common.NamedPipes
{
    public static partial class NamedPipeMessages
    {
        public static class PrepareForReset
        {
            public const string Request = "PreReset";
            public const string SuccessResult = "S";
            public const string FailureResult = "F";

            public class Response
            {
                public Response(string result)
                {
                    this.Result = result;
                }

                public string Result { get; }

                public Message CreateMessage()
                {
                    return new Message(this.Result, null);
                }
            }
        }
    }
}
