#if NETCOREAPP2_1
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.X509Certificates;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using GVFS.UnitTests.Mock.Git;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace GVFS.UnitTests.Common.Git
{
    [TestFixture]
    public class GitSslTests
    {
        public static object[] BoolGitSettings = new[]
        {
            new object[] { GitConfigSetting.HttpSslCertPasswordProtected },
            new object[] { GitConfigSetting.HttpSslVerify }
        };

        private const string CertificateName = "TestCert";
        private const string CertificatePassword = "SecurePassword";

        private MockTracer tracer;
        private MockGitProcess gitProcess;

        private Mock<CertificateVerifier> certificateVerifierMock;
        private Mock<SystemCertificateStore> certificateStoreMock;

        private MockDirectory mockDirectory;
        private MockFileSystem fileSystem;

        [SetUp]
        public void Setup()
        {
            this.tracer = new MockTracer();
            this.gitProcess = new MockGitProcess();
            this.mockDirectory = new MockDirectory("mock://root", null, null);
            this.fileSystem = new MockFileSystem(this.mockDirectory);
            this.certificateVerifierMock = new Mock<CertificateVerifier>();
            this.certificateStoreMock = new Mock<SystemCertificateStore>();
        }

        [Category(CategoryConstants.ExceptionExpected)]
        [TestCaseSource(typeof(GitSslTests), nameof(GitSslTests.BoolGitSettings))]
        public void ConstructorShouldThrowWhenLastBoolSettingNotABool(string setting)
        {
            IDictionary<string, GitConfigSetting> gitConfig = new Dictionary<string, GitConfigSetting>();
            gitConfig.Add(setting, new GitConfigSetting(setting, "true", "this is true"));

            Assert.Throws<InvalidRepoException>(() => new GitSsl(gitConfig));
        }

        [TestCaseSource(typeof(GitSslTests), nameof(GitSslTests.BoolGitSettings))]
        public void ConstructorShouldNotThrowWhenLastBoolSettingIsABool(string setting)
        {
            IDictionary<string, GitConfigSetting> gitConfig = new Dictionary<string, GitConfigSetting>();
            gitConfig.Add(setting, new GitConfigSetting(setting, "this is true", "true"));

            Assert.DoesNotThrow(() => new GitSsl(gitConfig));
        }

        [TestCase]
        public void GetCertificateShouldReturnNullWhenCertificateCommonNameSettingIsEmpty()
        {
            GitSsl sut = new GitSsl(new Dictionary<string, GitConfigSetting>());
            X509Certificate2 result = sut.GetCertificate(this.tracer, this.gitProcess);
            result.ShouldBeNull();
        }

        [TestCase]
        public void GetCertificateShouldReturnCertificateFromFileWhenFileExistsAndIsPasswordProtectedAndIsValid()
        {
            X509Certificate2 certificate = GenerateTestCertificate();
            this.SetupCertificateFile(certificate, CertificatePassword);
            this.SetupGitCertificatePassword();
            this.MakeCertificateValid(certificate);
            GitSsl gitSsl = new GitSsl(GetGitConfig(), () => this.certificateStoreMock.Object, this.certificateVerifierMock.Object, this.fileSystem);

            X509Certificate2 result = gitSsl.GetCertificate(this.tracer, this.gitProcess);

            result.ShouldNotBeNull();
            result.ShouldEqual(certificate);
        }

        [TestCase]
        public void GetCertificateShouldReturnCertificateFromFileWhenFileExistsAndIsNotPasswordProtectedAndIsValid()
        {
            X509Certificate2 certificate = GenerateTestCertificate();
            this.SetupCertificateFile(certificate);
            this.MakeCertificateValid(certificate);
            GitSsl gitSsl = new GitSsl(
                GetGitConfig(
                    new GitConfigSetting(GitConfigSetting.HttpSslCertPasswordProtected, "false")),
                 () => this.certificateStoreMock.Object,
                 this.certificateVerifierMock.Object,
                 this.fileSystem);

            X509Certificate2 result = gitSsl.GetCertificate(this.tracer, this.gitProcess);

            result.ShouldNotBeNull();
            result.ShouldEqual(certificate);
        }

        [TestCase]
        public void GetCertificateShouldReturnNullWhenFileExistsAndIsNotPasswordProtectedAndIsInvalid()
        {
            X509Certificate2 certificate = GenerateTestCertificate();
            this.SetupCertificateFile(certificate);
            this.MakeCertificateValid(certificate, false);
            GitSsl gitSsl = new GitSsl(
                GetGitConfig(
                    new GitConfigSetting(GitConfigSetting.HttpSslCertPasswordProtected, "false")),
                 () => this.certificateStoreMock.Object,
                 this.certificateVerifierMock.Object,
                 this.fileSystem);

            X509Certificate2 result = gitSsl.GetCertificate(this.tracer, this.gitProcess);

            result.ShouldBeNull();
        }

        [TestCase]
        public void GetCertificateShouldReturnCertificateFromFileWhenFileExistsAndIsNotPasswordProtectedAndIsInvalidAndShouldVerifyIsFalse()
        {
            X509Certificate2 certificate = GenerateTestCertificate();
            this.SetupCertificateFile(certificate);
            this.MakeCertificateValid(certificate, false);
            GitSsl gitSsl = new GitSsl(
                GetGitConfig(
                    new GitConfigSetting(GitConfigSetting.HttpSslCertPasswordProtected, "false"),
                    new GitConfigSetting(GitConfigSetting.HttpSslVerify, "false")),
                () => this.certificateStoreMock.Object,
                this.certificateVerifierMock.Object,
                this.fileSystem);

            X509Certificate2 result = gitSsl.GetCertificate(this.tracer, this.gitProcess);

            result.ShouldNotBeNull();
            result.ShouldEqual(certificate);
        }

        [TestCase]
        public void GetCertificateShouldReturnCertificateFromStoreAccordingToRulesWhenFileDoesNotExist()
        {
            X509Certificate2 certificate = this.MakeCertificateValid(GenerateTestCertificate());
            this.SetupGitCertificatePassword();
            GitSsl gitSsl = new GitSsl(
                GetGitConfig(),
                () => this.certificateStoreMock.Object,
                this.certificateVerifierMock.Object,
                this.fileSystem);

            this.SetupCertificateStore(
                true,
                this.MakeCertificateValid(GenerateTestCertificate(CertificateName + "suphix")),
                this.MakeCertificateValid(GenerateTestCertificate("prefix" + CertificateName)),
                this.MakeCertificateValid(GenerateTestCertificate("not this certificate")),
                this.MakeCertificateValid(GenerateTestCertificate(), false),
                this.MakeCertificateValid(GenerateTestCertificate(validFrom: DateTimeOffset.Now.AddDays(-4))),
                this.MakeCertificateValid(GenerateTestCertificate(validTo: DateTimeOffset.Now.AddDays(4))),
                certificate);

            X509Certificate2 result = gitSsl.GetCertificate(this.tracer, this.gitProcess);

            result.ShouldNotBeNull();
            result.ShouldEqual(certificate);
        }

        [TestCase]
        public void GetCertificateShouldReturnNullWhenNoMatchingCertificatesExist()
        {
            this.SetupGitCertificatePassword();
            GitSsl gitSsl = new GitSsl(
                GetGitConfig(),
                () => this.certificateStoreMock.Object,
                this.certificateVerifierMock.Object,
                this.fileSystem);

            this.SetupCertificateStore(
                true,
                this.MakeCertificateValid(GenerateTestCertificate(CertificateName + "suphix")),
                this.MakeCertificateValid(GenerateTestCertificate("prefix" + CertificateName)),
                this.MakeCertificateValid(GenerateTestCertificate("not this certificate")));

            X509Certificate2 result = gitSsl.GetCertificate(this.tracer, this.gitProcess);

            result.ShouldBeNull();
        }

        private static IDictionary<string, GitConfigSetting> GetGitConfig(params GitConfigSetting[] overrides)
        {
            IDictionary<string, GitConfigSetting> gitConfig = new Dictionary<string, GitConfigSetting>();
            gitConfig.Add(GitConfigSetting.HttpSslCert, new GitConfigSetting(GitConfigSetting.HttpSslCert, CertificateName));
            gitConfig.Add(GitConfigSetting.HttpSslCertPasswordProtected, new GitConfigSetting(GitConfigSetting.HttpSslCertPasswordProtected, "true"));
            gitConfig.Add(GitConfigSetting.HttpSslVerify, new GitConfigSetting(GitConfigSetting.HttpSslVerify, "true"));

            foreach (GitConfigSetting settingOverride in overrides)
            {
                gitConfig[settingOverride.Name] = settingOverride;
            }

            return gitConfig;
        }

        private static X509Certificate2 GenerateTestCertificate(
            string subjectName = null,
            DateTimeOffset? validFrom = null,
            DateTimeOffset? validTo = null)
        {
            ECDsa ecdsa = ECDsa.Create();
            CertificateRequest req = new CertificateRequest($"cn={subjectName ?? CertificateName}", ecdsa, HashAlgorithmName.SHA256);
            X509Certificate2 cert = req.CreateSelfSigned(validFrom ?? DateTimeOffset.Now.AddDays(-5), validTo ?? DateTimeOffset.Now.AddDays(5));

            return cert;
        }

        private X509Certificate2 MakeCertificateValid(X509Certificate2 certificate, bool isValid = true)
        {
            this.certificateVerifierMock.Setup(x => x.Verify(certificate)).Returns(isValid);
            return certificate;
        }

        private void SetupGitCertificatePassword()
        {
            this.gitProcess.SetExpectedCommandResult("credential fill", () => new GitProcess.Result($"password={CertificatePassword}\n", null, 0));
        }

        private void SetupCertificateStore(bool onlyValid, params X509Certificate2[] results)
        {
            this.SetupCertificateStore(X509FindType.FindBySubjectName, CertificateName, onlyValid, results);
        }

        private void SetupCertificateStore(
            X509FindType findType,
            string certificateName,
            bool onlyValid,
            params X509Certificate2[] results)
        {
            X509Certificate2Collection result = new X509Certificate2Collection();
            result.AddRange(results);
            this.certificateStoreMock.Setup(x => x.Find(findType, certificateName, onlyValid)).Returns(result);
        }

        private void SetupCertificateFile(X509Certificate2 certificate, string password = null)
        {
            byte[] certificateContents;
            if (password == null)
            {
                certificateContents = certificate.Export(X509ContentType.Pkcs12);
            }
            else
            {
                certificateContents = certificate.Export(X509ContentType.Pkcs12, password);
            }

            this.mockDirectory.AddFile(
                new MockFile(CertificateName, certificateContents),
                Path.Combine(this.mockDirectory.FullName, CertificateName));
        }
    }
}
#endif