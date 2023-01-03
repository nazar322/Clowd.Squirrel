using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Squirrel
{
    public class OsHelper
    {
        public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        public static bool IsOsx => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        public static string ExecuteCommand(string cmd)
        {
            string exeName;

            if (IsWindows)
            {
                exeName = "cmd.exe";
                cmd = "/C " + cmd;
            }
            else
            {
                exeName = "bash";
                cmd = $"-c \"{cmd}\"";
            }

            var psi = new ProcessStartInfo(exeName, cmd) {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            var console = Process.Start(psi);
            var output = console.StandardOutput.ReadToEnd();
            console.WaitForExit();

            return output;
        }
    }
}
