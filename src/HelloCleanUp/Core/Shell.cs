using System;
using System.Diagnostics;
using System.Text;

namespace HelloCleanUp.Core
{
    /// <summary>Runs console tools (sc, schtasks, powershell, dism) hidden and captures output.</summary>
    public static class Shell
    {
        public static int Run(string file, string args, out string output, int timeoutMs = 120000)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var sb = new StringBuilder();
            try
            {
                using (var p = new Process { StartInfo = psi })
                {
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        output = sb.ToString();
                        return -1;
                    }
                    p.WaitForExit();
                    output = sb.ToString();
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return -1;
            }
        }

        public static int Run(string file, string args, int timeoutMs = 120000)
        {
            string _;
            return Run(file, args, out _, timeoutMs);
        }

        public static string System32(string exe)
        {
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), exe);
        }

        public static int PowerShell(string command, out string output, int timeoutMs = 300000)
        {
            var ps = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe");
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            return Run(ps, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded, out output, timeoutMs);
        }
    }
}
