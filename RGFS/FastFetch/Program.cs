using CommandLine;

namespace FastFetch
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Parser.Default.ParseArguments<FastFetchVerb>(args)
                .WithParsed(fastFetch => fastFetch.Execute());
        }
    }
}
