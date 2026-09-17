using GVFS.Common.Tracing;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace GVFS.Common
{
    /// <summary>
    /// Verifies that an installer executable is a genuine VFS for Git installer
    /// by checking its Authenticode signature and PE version info.
    /// </summary>
    public static class InstallerVerifier
    {
        public const string ExpectedProductName = "VFS for Git";
        public const string ExpectedSignerCommonName = "Microsoft Corporation";

        /// <summary>
        /// Verifies the installer at the given path. Returns true if the
        /// installer passes all checks, false otherwise.
        /// </summary>
        /// <param name="allowUnsigned">
        /// When true, skip Authenticode verification (for dev/test builds).
        /// Product identity is still checked.
        /// </param>
        public static bool TryVerifyInstaller(
            ITracer tracer,
            string installerPath,
            bool allowUnsigned,
            out string error)
        {
            ArgumentNullException.ThrowIfNull(tracer);
            ArgumentNullException.ThrowIfNull(installerPath);

            if (!File.Exists(installerPath))
            {
                error = $"Installer not found: {installerPath}";
                return false;
            }

            // Always verify product identity, even when unsigned is allowed.
            if (!TryVerifyProductIdentity(tracer, installerPath, out error))
            {
                return false;
            }

            if (allowUnsigned)
            {
                tracer.RelatedWarning(
                    $"{nameof(InstallerVerifier)}: Skipping Authenticode verification (--allow-unsigned)");
                error = null;
                return true;
            }

            if (!TryVerifyAuthenticodeSignature(tracer, installerPath, out error))
            {
                return false;
            }

            error = null;
            return true;
        }

        private static bool TryVerifyProductIdentity(
            ITracer tracer,
            string installerPath,
            out string error)
        {
            FileVersionInfo versionInfo;
            try
            {
                versionInfo = FileVersionInfo.GetVersionInfo(installerPath);
            }
            catch (Exception ex)
            {
                error = $"Failed to read version info from {installerPath}: {ex.Message}";
                tracer.RelatedError(error);
                return false;
            }

            string productName = versionInfo.ProductName?.Trim();
            if (!string.Equals(productName, ExpectedProductName, StringComparison.OrdinalIgnoreCase))
            {
                error = $"Installer ProductName '{productName}' does not match expected '{ExpectedProductName}'";
                tracer.RelatedError($"{nameof(InstallerVerifier)}: {error}");
                return false;
            }

            tracer.RelatedInfo(
                $"{nameof(InstallerVerifier)}: Product identity verified — " +
                $"ProductName='{productName}', FileVersion='{versionInfo.FileVersion?.Trim()}'");

            error = null;
            return true;
        }

        private static bool TryVerifyAuthenticodeSignature(
            ITracer tracer,
            string installerPath,
            out string error)
        {
            // Authenticode verification is Windows-only. Fail closed on
            // other platforms — callers can opt in with --allow-unsigned
            // when running on non-Windows for dev/test scenarios.
            if (!OperatingSystem.IsWindows())
            {
                error = "Authenticode verification is only supported on Windows";
                tracer.RelatedError($"{nameof(InstallerVerifier)}: {error}");
                return false;
            }

            // Step 1: Verify the file's Authenticode signature with
            // WinVerifyTrust and extract the leaf signer certificate from
            // its provider state. This is the ONLY API that actually checks
            // the signed digest against the file's contents — extracting
            // the signer certificate by parsing the PE alone does NOT
            // detect a tampered file with an intact signature blob.
            if (!TryWinVerifyTrustAndGetSignerCert(
                    installerPath,
                    out byte[] signerCertBytes,
                    out string trustError))
            {
                error = trustError;
                tracer.RelatedError($"{nameof(InstallerVerifier)}: {error}");
                return false;
            }

            // Step 2: After WinVerifyTrust has confirmed the file's
            // signature is intact, inspect the signer certificate to
            // verify it really is Microsoft (and not just any valid
            // code-signing cert). Use the modern X509CertificateLoader
            // to materialize the cert from the DER bytes we copied out of
            // the WinTrust provider state — X509Certificate.CreateFromSignedFile
            // is obsolete (SYSLIB0057) and its replacement does not parse
            // PE signature blocks.
            X509Certificate2 certificate;
            try
            {
                certificate = X509CertificateLoader.LoadCertificate(signerCertBytes);
            }
            catch (CryptographicException ex)
            {
                error = $"Failed to parse signer certificate: {ex.Message}";
                tracer.RelatedError($"{nameof(InstallerVerifier)}: {error}");
                return false;
            }

            using (certificate)
            {
                // Exact CN match — GetNameInfo(SimpleName) parses the
                // certificate's Subject DN and returns the raw CN value,
                // avoiding substring-collision attacks such as
                // CN="Microsoft Corporation Ltd" or DNs that put the
                // attacker's name in the CN field with "Microsoft
                // Corporation" appearing elsewhere.
                string signerCommonName = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
                if (!string.Equals(signerCommonName, ExpectedSignerCommonName, StringComparison.Ordinal))
                {
                    error = $"Installer signed by unexpected publisher: '{signerCommonName}' (Subject: {certificate.Subject})";
                    tracer.RelatedError($"{nameof(InstallerVerifier)}: {error}");
                    return false;
                }

                tracer.RelatedInfo(
                    $"{nameof(InstallerVerifier)}: Authenticode signature verified — " +
                    $"Signer CN='{signerCommonName}', Thumbprint={certificate.Thumbprint}");
            }

            error = null;
            return true;
        }

        // WinVerifyTrust interop — verifies Authenticode hash + signature
        // chain in one call. This is the same code path Windows uses for
        // SmartScreen / SRP / WDAC, and is the ONLY supported way to
        // verify Authenticode on Windows. See
        // https://learn.microsoft.com/en-us/windows/win32/api/wintrust/nf-wintrust-winverifytrust.

        private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 =
            new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        private const uint WTD_UI_NONE = 2;
        private const uint WTD_REVOKE_WHOLECHAIN = 1;
        private const uint WTD_CHOICE_FILE = 1;
        private const uint WTD_STATEACTION_VERIFY = 1;
        private const uint WTD_STATEACTION_CLOSE = 2;

        // S_OK and the few error codes we want to surface by name.
        private const int S_OK = 0;
        private const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
        private const int TRUST_E_BAD_DIGEST = unchecked((int)0x80096010);
        private const int TRUST_E_PROVIDER_UNKNOWN = unchecked((int)0x800B0001);
        private const int CRYPT_E_SECURITY_SETTINGS = unchecked((int)0x80092026);

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = false)]
        [SupportedOSPlatform("windows")]
        private static extern int WinVerifyTrust(IntPtr hWnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

        // WTHelper interop — used to extract the signer certificate from a
        // WinVerifyTrust state after a successful verification. Avoids the
        // obsolete X509Certificate.CreateFromSignedFile (SYSLIB0057), and
        // reuses the verification we already did rather than reparsing the
        // PE signature block from scratch.

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
        [SupportedOSPlatform("windows")]
        private static extern IntPtr WTHelperProvDataFromStateData(IntPtr hStateData);

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
        [SupportedOSPlatform("windows")]
        private static extern IntPtr WTHelperGetProvSignerFromChain(
            IntPtr pProvData,
            uint idxSigner,
            [MarshalAs(UnmanagedType.Bool)] bool fCounterSigner,
            uint idxCounterSigner);

        // Partial layouts — we only marshal the fields we need. Sequential
        // layout means the runtime computes offsets/padding correctly for
        // the leading fields; we never read past the declared end, so the
        // trailing fields can be omitted without risk.

        [StructLayout(LayoutKind.Sequential)]
        private struct CRYPT_PROVIDER_SGNR
        {
            public uint cbStruct;
            public System.Runtime.InteropServices.ComTypes.FILETIME sftVerifyAsOf;
            public uint csCertChain;
            public IntPtr pasCertChain;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CRYPT_PROVIDER_CERT
        {
            public uint cbStruct;
            public IntPtr pCert;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CERT_CONTEXT
        {
            public uint dwCertEncodingType;
            public IntPtr pbCertEncoded;
            public uint cbCertEncoded;
            public IntPtr pCertInfo;
            public IntPtr hCertStore;
        }

        [SupportedOSPlatform("windows")]
        private static bool TryWinVerifyTrustAndGetSignerCert(
            string filePath,
            out byte[] signerCertBytes,
            out string error)
        {
            signerCertBytes = null;

            WINTRUST_FILE_INFO fileInfo = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                pcwszFilePath = filePath,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero,
            };

            IntPtr fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
            try
            {
                Marshal.StructureToPtr(fileInfo, fileInfoPtr, fDeleteOld: false);

                WINTRUST_DATA data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                    pPolicyCallbackData = IntPtr.Zero,
                    pSIPClientData = IntPtr.Zero,
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_WHOLECHAIN,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = fileInfoPtr,
                    dwStateAction = WTD_STATEACTION_VERIFY,
                    hWVTStateData = IntPtr.Zero,
                    pwszURLReference = IntPtr.Zero,
                    dwProvFlags = 0,
                    dwUIContext = 0,
                    pSignatureSettings = IntPtr.Zero,
                };

                Guid action = WINTRUST_ACTION_GENERIC_VERIFY_V2;
                int verifyResult = WinVerifyTrust(IntPtr.Zero, ref action, ref data);

                string extractError = null;
                if (verifyResult == S_OK)
                {
                    // Extract the leaf signer cert from WinTrust's provider
                    // state BEFORE closing the state (which would free the
                    // underlying CERT_CONTEXT).
                    extractError = TryExtractSignerCert(data.hWVTStateData, out signerCertBytes);
                }

                // Always close the state to release wintrust resources.
                data.dwStateAction = WTD_STATEACTION_CLOSE;
                WinVerifyTrust(IntPtr.Zero, ref action, ref data);

                if (verifyResult == S_OK)
                {
                    if (signerCertBytes == null)
                    {
                        error = extractError ?? "Failed to extract signer certificate from verified file";
                        return false;
                    }
                    error = null;
                    return true;
                }

                error = verifyResult switch
                {
                    TRUST_E_NOSIGNATURE => "Installer is not signed",
                    TRUST_E_BAD_DIGEST => "Installer Authenticode hash does not match — file has been tampered with",
                    TRUST_E_PROVIDER_UNKNOWN => "Authenticode trust provider is not available",
                    CRYPT_E_SECURITY_SETTINGS => "Authenticode verification blocked by local security policy",
                    _ => $"Authenticode verification failed (HRESULT 0x{verifyResult:X8})",
                };
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(fileInfoPtr);
            }
        }

        [SupportedOSPlatform("windows")]
        private static string TryExtractSignerCert(IntPtr hWVTStateData, out byte[] certBytes)
        {
            certBytes = null;

            IntPtr provData = WTHelperProvDataFromStateData(hWVTStateData);
            if (provData == IntPtr.Zero)
            {
                return "WTHelperProvDataFromStateData returned null";
            }

            IntPtr signerPtr = WTHelperGetProvSignerFromChain(provData, idxSigner: 0, fCounterSigner: false, idxCounterSigner: 0);
            if (signerPtr == IntPtr.Zero)
            {
                return "WTHelperGetProvSignerFromChain returned null";
            }

            CRYPT_PROVIDER_SGNR signer = Marshal.PtrToStructure<CRYPT_PROVIDER_SGNR>(signerPtr);
            if (signer.csCertChain == 0 || signer.pasCertChain == IntPtr.Zero)
            {
                return "Signer has no certificate chain";
            }

            // pasCertChain[0] is the leaf signer cert by convention.
            CRYPT_PROVIDER_CERT providerCert = Marshal.PtrToStructure<CRYPT_PROVIDER_CERT>(signer.pasCertChain);
            if (providerCert.pCert == IntPtr.Zero)
            {
                return "Signer certificate context is null";
            }

            CERT_CONTEXT certContext = Marshal.PtrToStructure<CERT_CONTEXT>(providerCert.pCert);
            if (certContext.pbCertEncoded == IntPtr.Zero || certContext.cbCertEncoded == 0)
            {
                return "Signer certificate has no encoded data";
            }

            // Copy the DER bytes out — they live in the WinTrust state which
            // is about to be freed.
            byte[] buffer = new byte[certContext.cbCertEncoded];
            Marshal.Copy(certContext.pbCertEncoded, buffer, 0, buffer.Length);
            certBytes = buffer;
            return null;
        }
    }
}
