using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Packaging;
using NuGet.Packaging.Signing;
using HashAlgorithmName = NuGet.Common.HashAlgorithmName;

namespace Squirrel.Update
{
    internal class NugetPackageVerify
    {
        public static string GetSHA256Fingerprint(X509Certificate2 certificate)
        {
            return CertificateUtility.GetHashString(certificate, HashAlgorithmName.SHA256);
        }

        public static async Task<string[]> CheckSignature(string packagePath, string sha256thumbprint)
        {
            var token = CancellationToken.None;
            using var p = new PackageArchiveReader(packagePath);

            var _defaultFingerprintAlgorithm = HashAlgorithmName.SHA256;

            var allowListEntries = new[] {
                new CertificateHashAllowListEntry(
                    VerificationTarget.Author, // | VerificationTarget.Repository,
                    SignaturePlacement.PrimarySignature,
                    sha256thumbprint,
                    _defaultFingerprintAlgorithm)
            };

            //var untrustedRoots = new KeyValuePair<string, HashAlgorithmName>[] { new KeyValuePair<string, HashAlgorithmName>(sha256thumbprint, _defaultFingerprintAlgorithm) };

            var verifierSettings = SignedPackageVerifierSettings.GetVerifyCommandDefaultPolicy();
            var verificationProviders = new List<ISignatureVerificationProvider>()
            {
                new IntegrityVerificationProvider(),
                //new SignatureTrustAndValidityVerificationProvider(untrustedRoots),
                new AllowListVerificationProvider(
                    allowListEntries,
                    requireNonEmptyAllowList: false,
                    noMatchErrorMessage: "Error_NoMatchingCertificate")
            };

            var verifier = new PackageSignatureVerifier(verificationProviders);

            var verificationResult = await verifier.VerifySignaturesAsync(p, verifierSettings, token);
            if (!verificationResult.IsSigned)
                throw new Exception($"The package '{Path.GetFileName(packagePath)}' is not signed.");

            var messages = verificationResult.Results
                //.Where(r => r.Trust != SignatureVerificationStatus.Valid)
                .SelectMany(r => r.Issues)
                .Select(i => $"[{i.Level}] " + i.Message)
                .ToArray();

            if (!verificationResult.IsValid)
                throw new Exception("The package validation has failed. Messages: " + Environment.NewLine + String.Join(Environment.NewLine, messages));

            return messages;
        }
    }
}
