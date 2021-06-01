namespace GVFS.Common
{
    /// <summary>
    /// Default product implementation of IProcessRunner
    /// interface. Delegates calls to static ProcessHelper class. This
    /// class can be used to enable testing of components that call
    /// into the ProcessHelper functionality.
    /// </summary>
    public class ProcessRunnerImpl : IProcessRunner
    {
        public ProcessResult Run(string programName, string args, bool redirectOutput)
        {
            return ProcessHelper.Run(programName, args, redirectOutput);
        }
    }
}
