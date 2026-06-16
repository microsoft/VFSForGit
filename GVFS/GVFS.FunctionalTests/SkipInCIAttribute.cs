using NUnit.Framework;

namespace GVFS.FunctionalTests
{
    /// <summary>
    /// Marks a test or fixture to be skipped in CI (when --ci is passed).
    /// Use the <see cref="Reason"/> property to document why the test is
    /// skipped so it can be triaged and fixed later.
    /// </summary>
    public class SkipInCIAttribute : CategoryAttribute
    {
        public SkipInCIAttribute(string reason)
            : base("SkipInCI")
        {
            this.Reason = reason;
        }

        public string Reason { get; }
    }
}
