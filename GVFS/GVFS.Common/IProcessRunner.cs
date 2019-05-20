namespace GVFS.Common
{
    /// <summary>
    /// Interface around process helper methods. This is to enable
    /// testing of components that interact with the ProcessHelper
    /// static class.
    /// </summary>
    public interface IProcessRunner
    {
        ProcessResult Run(string programName, string args, bool redirectOutput);
    }
}
