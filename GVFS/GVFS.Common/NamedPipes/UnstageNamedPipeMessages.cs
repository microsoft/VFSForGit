namespace GVFS.Common.NamedPipes
{
    public static partial class NamedPipeMessages
    {
        public static class PrepareForUnstage
        {
            public const string Request = "PreUnstage";
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
