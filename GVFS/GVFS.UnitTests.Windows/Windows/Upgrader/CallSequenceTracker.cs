using GVFS.Tests.Should;
using System;
using System.Collections.Generic;

namespace GVFS.UnitTests.Windows.Upgrader
{
    public class CallSequenceTracker
    {
        public List<string> CallTrail { get; private set; } = new List<string>();

        public void RecordMethod(string methodName)
        {
            this.CallTrail.Add(methodName);
        }

        public void Clear()
        {
            this.CallTrail.Clear();
        }

        public void VerifyMethodsCalledInSequence(List<string> expectedSequence)
        {
            this.CallTrail.ShouldMatchInOrder(
                expectedSequence, 
                (str1, str2) => 
                {
                    return str1.Equals(str2, StringComparison.Ordinal);
                });
        }

        public void VerifyMethodsNotCalled(List<string> unexpectedMethods)
        {
            this.CallTrail.ShouldNotContain(
                unexpectedMethods,
                (str1, str2) =>
                {
                    return str1.Equals(str2, StringComparison.Ordinal);
                });
        }
    }
}
