using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Squirrel;
using Squirrel.Lib;
using Squirrel.SimpleSplat;

namespace SquirrelCli
{
    internal abstract class BaseOptions : ValidatedOptionSet
    {
        public string releaseDir { get; private set; } = ".\\Releases";
        public BaseOptions()
        {
            Add("r=|releaseDir=", "Release directory containing releasified packages", v => releaseDir = v);
        }
    }

    internal class SignArgumentAttribute : Attribute
    {
        public string ParamName { get; }
        public string BoolDefaultValue { get; set; }
        public bool IsSensitive { get; set; }

        public SignArgumentAttribute(string paramName)
        {
            ParamName = paramName;
        }
    }

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    internal class NugetSignAttribute : SignArgumentAttribute
    {
        public NugetSignAttribute(string paramName) : base(paramName)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    internal class SignToolAttribute : SignArgumentAttribute
    {
        public SignToolAttribute(string paramName) : base(paramName)
        {
        }
    }

    internal abstract class SigningOptions : BaseOptions, IEnableLogger
    {
        [SignTool("/f")]
        [NugetSign("-CertificatePath")]
        public string signPath { get; private set; }

        [SignTool("/p", IsSensitive = true)]
        [NugetSign("-CertificatePassword", IsSensitive = true)]
        public string signPassword { get; private set; }

        [SignTool("/n")]
        [NugetSign("-CertificateSubjectName")]
        public string signSubjectName { get; private set; }

        [SignTool("/s")]
        [NugetSign("-CertificateStoreName")]
        public string signStoreName { get; private set; }

        [SignTool("/sm")]
        [NugetSign("-CertificateStoreLocation", BoolDefaultValue = "LocalMachine")]
        public bool signMachineStore { get; private set; }

        [SignTool("/sha1")]
        [NugetSign("-CertificateFingerprint")]
        public string signSha1 { get; private set; }

        [SignTool("/fd")]
        [NugetSign("-HashAlgorithm")]
        public string signHash { get; private set; } = "SHA256";

        [SignTool("/tr")]
        [NugetSign("-Timestamper")]
        public string signTimestamp { get; private set; }

        [SignTool("/td")]
        [NugetSign("-TimestampHashAlgorithm")]
        public string signTimestampHash { get; private set; } = "SHA256";

        public int signParallel { get; private set; } = 10;

        [Obsolete]
        public string signParams { get; private set; }

        private bool shouldSign = false;

        public SigningOptions()
        {
            Add("signPath=", "File path to the certificate used for signing", v => signPath = v);
            Add("signPassword=", "Password for the signing certificate", v => signPassword = v);
            Add("signSubjectName=", "Subject name of the certificate, used to search the local certificate store", v => signSubjectName = v);
            Add("signSha1=", "SHA1 fingerprint of the certificate, used to search the local certificate store", v => signSha1 = v);
            Add("signStoreName=", "Name of the store to search for certificate. Default is \"MY\" store.", v => signStoreName = v);
            Add("signMachineStore", "Search the machine store instead of the current user", v => signMachineStore = true);
            Add("signHash=", "The hash algorithm to be used for the file signature (default SHA256)", v => signHash = v);
            Add("signTimestamp=", "URL to an RFC 3161 timestamping server", v => signTimestamp = v);
            Add("signTimestampHash=", "The hash algorithm to be used by the RFC 3161 timestamping server (default SHA256)", v => signTimestampHash = v);

            // legacy / hidden
            Add("signParallel=", "Number of PE files to pass to SignTool in parallel (default 10)", v => signParallel = ToInt(nameof(signParallel), v, 1, 100), true);
            Add("n=|signParams=", "Sign the installer via SignTool.exe with the parameters given", v => signParams = v, true);
        }

        public override void Validate()
        {
            if (!String.IsNullOrEmpty(null))
                this.Log().Warn("The --signParams (-n) argument is deprecated. It will be removed in a future version");

            IsValidFile(nameof(signPath));
            IsValidUrl(nameof(signTimestamp));

            shouldSign = !String.IsNullOrEmpty(signPath) || !String.IsNullOrEmpty(signSubjectName) || !String.IsNullOrEmpty(signSha1);
            if (shouldSign && String.IsNullOrEmpty(signTimestamp))
                this.Log().Warn("Signing without a timestamping service could result in binaries failing to start after the certificate expires");
        }

        private List<string> GetSignArgs<T>(bool hideSensitive) where T : SignArgumentAttribute
        {
            List<string> args = new List<string>();
            foreach (var f in this.GetType().GetProperties()) {
                var attr = f.GetCustomAttributes(typeof(T), false).FirstOrDefault() as T;
                if (attr == null) continue;
                if (f.PropertyType == typeof(string)) {
                    var v = f.GetValue(this) as string;
                    if (!String.IsNullOrEmpty(v)) {
                        args.Add(attr.ParamName);
                        args.Add((attr.IsSensitive && hideSensitive) ? "[redacted]" : v);
                    }
                } else if (f.PropertyType == typeof(bool) || f.PropertyType == typeof(bool?)) {
                    var v = f.GetValue(this) as bool?;
                    if (v == true) {
                        args.Add(attr.ParamName);
                        if (!String.IsNullOrEmpty(attr.BoolDefaultValue)) args.Add(attr.BoolDefaultValue);
                    }
                } else {
                    throw new Exception("Unsupported sign argument type: " + f.PropertyType);
                }
            }
            return args;
        }

        public string GetArgsForNugetSign(string exePath, bool hideSensitive)
        {
            if (!shouldSign)
                return null;

            string[] defaultArgs = new string[] { exePath, "-NonInteractive", "-Overwrite", "-Verbosity", "detailed" };
            var args = GetSignArgs<NugetSignAttribute>(hideSensitive);
            return Utility.ArgsToCommandLine(defaultArgs.Concat(args));
        }

        public string GetArgsForSignTool(string[] exes, bool hideSensitive)
        {
            if (!String.IsNullOrEmpty(signParams)) {
                this.Log().Warn("The --signParams (-n) argument is deprecated. It will be removed in a future version");
                var exeString = String.Join(" ", exes.Select(x => $"\"{x}\""));
                return $"{signParams} {exeString}";
            } else {
                if (!shouldSign)
                    return null;
                var args = GetSignArgs<SignToolAttribute>(hideSensitive);
                args.Add("/v");
                var f = args.Concat(exes);
                return Utility.ArgsToCommandLine(f);
            }
        }
    }

    internal class ReleasifyOptions : SigningOptions
    {
        public string package { get; set; }
        public string baseUrl { get; private set; }
        public string framework { get; private set; }
        public string splashImage { get; private set; }
        public string updateIcon { get; private set; }
        public string appIcon { get; private set; }
        public string setupIcon { get; private set; }
        public bool noDelta { get; private set; }
        public bool allowUnaware { get; private set; }
        public string msi { get; private set; }

        public ReleasifyOptions()
        {
            // hidden arguments
            Add("b=|baseUrl=", "Provides a base URL to prefix the RELEASES file packages with", v => baseUrl = v, true);
            Add("allowUnaware", "Allows building packages without a SquirrelAwareApp (disabled by default)", v => allowUnaware = true, true);
            Add("addSearchPath=", "Add additional search directories when looking for helper exe's such as Setup.exe, Update.exe, etc", v => HelperExe.AddSearchPath(v), true);

            // public arguments
            Add("p=|package=", "Path to a nuget package to releasify", v => package = v);
            Add("f=|framework=", "Set the required .NET framework version, e.g. net461", v => framework = v);
            Add("i=|icon=", "Sets all the icons (update, app, setup). Can be used with other icon arguments, later arguments will take precedence.",
                (v) => { updateIcon = v; appIcon = v; setupIcon = v; });
            Add("updateIcon=", "ICO file that will be used for Update.exe", v => updateIcon = v);
            Add("appIcon=", "ICO file that will be used in the 'Apps and Features' list.", v => appIcon = v);
            Add("setupIcon=", "ICO file that will be used for Setup.exe", v => setupIcon = v);
            Add("splashImage=", "Image to be displayed during installation (can be jpg, png, gif, etc)", v => splashImage = v);
            Add("noDelta", "Skip the generation of delta packages to save time", v => noDelta = true);
            Add("msi=", "Will generate a .msi machine-wide deployment tool. If installed on a machine, this msi will silently install the releasified " +
                "app each time a user signs in if it is not already installed. This value must be either 'x86' or 'x64'.", v => msi = v.ToLower());
        }

        public override void Validate()
        {
            ValidateInternal(true);
        }

        protected virtual void ValidateInternal(bool checkPackage)
        {
            IsValidFile(nameof(appIcon), ".ico");
            IsValidFile(nameof(setupIcon), ".ico");
            IsValidFile(nameof(updateIcon), ".ico");
            IsValidFile(nameof(splashImage));
            IsValidUrl(nameof(baseUrl));

            if (checkPackage) {
                IsRequired(nameof(package));
                IsValidFile(nameof(package), ".nupkg");
            }

            if (!String.IsNullOrEmpty(msi))
                if (!msi.Equals("x86") && !msi.Equals("x64"))
                    throw new OptionValidationException($"Argument 'msi': File must be either 'x86' or 'x64'. Actual value was '{msi}'.");

            base.Validate();
        }
    }

    internal class PackOptions : ReleasifyOptions
    {
        public string packName { get; private set; }
        public string packVersion { get; private set; }
        public string packAuthors { get; private set; }
        public string packDirectory { get; private set; }
        public bool includePdb { get; private set; }

        public PackOptions()
        {
            Add("packName=", "The name of the package to create", v => packName = v);
            Add("packVersion=", "Package version", v => packVersion = v);
            Add("packAuthors=", "Comma delimited list of package authors", v => packAuthors = v);
            Add("packDirectory=", "The directory with the application files that will be packaged into a release", v => packDirectory = v);
            Add("includePdb", "Include the *.pdb files in the package (default: false)", v => includePdb = true);

            // remove 'package' argument
            Remove("package");
            Remove("p");
        }

        public override void Validate()
        {
            IsRequired(nameof(packName), nameof(packVersion), nameof(packAuthors), nameof(packDirectory));
            base.ValidateInternal(false);
        }
    }

    internal class SyncBackblazeOptions : BaseOptions
    {
        public string b2KeyId { get; private set; }
        public string b2AppKey { get; private set; }
        public string b2BucketId { get; private set; }

        public SyncBackblazeOptions()
        {
            Add("b2BucketId=", "Id or name of the bucket in B2, S3, etc", v => b2BucketId = v);
            Add("b2keyid=", "B2 Auth Key Id", v => b2KeyId = v);
            Add("b2key=", "B2 Auth Key", v => b2AppKey = v);
        }

        public override void Validate()
        {
            IsRequired(nameof(b2KeyId), nameof(b2AppKey), nameof(b2BucketId));
        }
    }

    internal class SyncHttpOptions : BaseOptions
    {
        public string url { get; private set; }
        public string token { get; private set; }

        public SyncHttpOptions()
        {
            Add("url=", "Url to the simple http folder where the releases are found", v => url = v);
        }

        public override void Validate()
        {
            IsRequired(nameof(url));
            IsValidUrl(nameof(url));
        }
    }

    internal class SyncGithubOptions : BaseOptions
    {
        public string repoUrl { get; private set; }
        public string token { get; private set; }

        public SyncGithubOptions()
        {
            Add("repoUrl=", "Url to the github repository (eg. 'https://github.com/myname/myrepo')", v => repoUrl = v);
            Add("token=", "The oauth token to use as login credentials", v => token = v);
        }

        public override void Validate()
        {
            IsRequired(nameof(repoUrl));
            IsValidUrl(nameof(repoUrl));
        }
    }
}
