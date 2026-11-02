using GVFS.Common.FileSystem;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class HydrationFailureDiagnosticsTests
    {
        private const int ErrorSharingViolationWin32Code = 32;
        private static readonly int ErrorSharingViolationHResult = unchecked((int)0x80070000) | ErrorSharingViolationWin32Code;

        [TestCase]
        public void BuildMetadataIncludesExceptionTypeAndHResult()
        {
            IOException failure = new IOException("sharing violation", ErrorSharingViolationHResult);

            var metadata = HydrationFailureDiagnostics.BuildMetadata("some\\path", failure);

            metadata["Path"].ShouldEqual("some\\path");
            metadata["ExceptionType"].ShouldEqual(nameof(IOException));
            metadata["HResult"].ShouldEqual("0x" + ErrorSharingViolationHResult.ToString("X8"));
        }

        [TestCase]
        public void BuildMetadataDerivesWin32ErrorFromWin32HResult()
        {
            IOException failure = new IOException("sharing violation", ErrorSharingViolationHResult);

            var metadata = HydrationFailureDiagnostics.BuildMetadata("some\\path", failure);

            metadata.ContainsKey("Win32Error").ShouldBeTrue();
            metadata["Win32Error"].ShouldEqual(ErrorSharingViolationWin32Code);
        }

        [TestCase]
        public void BuildMetadataOmitsWin32ErrorWhenHResultIsNotWin32Facility()
        {
            // The default HResult for a plain Exception (COR_E_EXCEPTION) is not encoded via
            // HRESULT_FROM_WIN32 (facility != FACILITY_WIN32), unlike most BCL I/O exceptions.
            Exception failure = new Exception("not a Win32-facility HResult");

            var metadata = HydrationFailureDiagnostics.BuildMetadata("some\\path", failure);

            metadata.ContainsKey("Win32Error").ShouldBeFalse();
        }

        [TestCase]
        public void GetFailureSignatureIncludesWin32ErrorWhenDerivable()
        {
            IOException failure = new IOException("sharing violation", ErrorSharingViolationHResult);

            string signature = HydrationFailureDiagnostics.GetFailureSignature(failure);

            signature.ShouldEqual($"{nameof(IOException)}(Win32={ErrorSharingViolationWin32Code})");
        }

        [TestCase]
        public void GetFailureSignatureFallsBackToHResultWhenWin32NotDerivable()
        {
            Exception failure = new Exception("not a Win32-facility HResult");

            string signature = HydrationFailureDiagnostics.GetFailureSignature(failure);

            signature.ShouldEqual($"{nameof(Exception)}(HResult=0x{failure.HResult:X8})");
        }

        [TestCase]
        public void BuildMetadataDerivesWin32ErrorForUnauthorizedAccessException()
        {
            // Access-denied hydration failures surface as UnauthorizedAccessException, not IOException —
            // confirm the Win32 derivation isn't accidentally IOException-specific.
            int accessDeniedHResult = unchecked((int)0x80070000) | 5;
            UnauthorizedAccessException failure = new UnauthorizedAccessException("access denied");
            SetHResult(failure, accessDeniedHResult);

            var metadata = HydrationFailureDiagnostics.BuildMetadata("some\\path", failure);

            metadata["ExceptionType"].ShouldEqual(nameof(UnauthorizedAccessException));
            metadata["Win32Error"].ShouldEqual(5);
        }

        [TestCase]
        public void GetFailureSignatureDerivesWin32ErrorAtZeroBoundary()
        {
            // Win32 code 0 (ERROR_SUCCESS) is a degenerate but legal boundary value for the mask —
            // confirm it is still reported as a derived Win32 error, not mistaken for "not derivable".
            IOException failure = new IOException("boundary", unchecked((int)0x80070000));

            string signature = HydrationFailureDiagnostics.GetFailureSignature(failure);

            signature.ShouldEqual($"{nameof(IOException)}(Win32=0)");
        }

        [TestCase]
        public void BuildMetadataThrowsOnNullFailure()
        {
            Assert.Throws<ArgumentNullException>(() => HydrationFailureDiagnostics.BuildMetadata("some\\path", null));
        }

        [TestCase]
        public void GetFailureSignatureThrowsOnNullFailure()
        {
            Assert.Throws<ArgumentNullException>(() => HydrationFailureDiagnostics.GetFailureSignature(null));
        }

        private static void SetHResult(Exception exception, int hResult)
        {
            typeof(Exception)
                .GetProperty(nameof(Exception.HResult))
                .SetValue(exception, hResult);
        }

        [TestCase]
        public void FormatSignatureCountsJoinsEntries()
        {
            Dictionary<string, int> counts = new Dictionary<string, int>
            {
                { "IOException(Win32=32)", 3 },
                { "UnauthorizedAccessException(HResult=0x80070005)", 1 },
            };

            string formatted = HydrationFailureDiagnostics.FormatSignatureCounts(counts);

            formatted.ShouldEqual("IOException(Win32=32)=3;UnauthorizedAccessException(HResult=0x80070005)=1");
        }
    }
}
