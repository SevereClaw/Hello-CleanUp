using System;
using System.Diagnostics;
using System.Text;

namespace HelloCleanUp.Core
{
    /// <summary>Runs console tools (sc, schtasks, powershell, dism) hidden and captures output.</summary>
    public static class Shell
    {
        public static int Run(string file, string args, out string output, int timeoutMs = 120000, Encoding encoding = null)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            if (encoding != null)
            {
                psi.StandardOutputEncoding = encoding;
                psi.StandardErrorEncoding = encoding;
            }

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

        /// <summary>True if the service exists and is running (or starting).</summary>
        public static bool IsServiceRunning(string name)
        {
            try
            {
                using (var sc = new System.ServiceProcess.ServiceController(name))
                {
                    var s = sc.Status;
                    return s == System.ServiceProcess.ServiceControllerStatus.Running
                        || s == System.ServiceProcess.ServiceControllerStatus.StartPending;
                }
            }
            catch
            {
                return false;
            }
        }

        public static string System32(string exe)
        {
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), exe);
        }

        public static int PowerShell(string command, out string output, int timeoutMs = 300000)
        {
            var ps = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe");
            // With redirected streams PowerShell serializes progress bars ("Preparing modules…", "Creating a restore point… 9%")
            // to stderr as CLIXML, which then gets mixed into the output. Turn progress off and ask for plain text.
            // Output as UTF-8 too: otherwise Windows' localized (e.g. Russian) error messages arrive garbled.
            const string prologue =
                "$ProgressPreference = 'SilentlyContinue'\r\n" +
                "try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false } catch { }\r\n";
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(prologue + command));
            var code = Run(ps, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -OutputFormat Text -EncodedCommand " + encoded,
                           out output, timeoutMs, new UTF8Encoding(false));
            output = StripClixml(output);
            return code;
        }

        /// <summary>Removes any CLIXML that still slipped through ("#&lt; CLIXML" marker and &lt;Objs …&gt;…&lt;/Objs&gt; blocks).</summary>
        public static string StripClixml(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<Objs\b.*?</Objs>", "",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            text = text.Replace("#< CLIXML", "");
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var sb = new StringBuilder();
            foreach (var line in lines)
                if (line.Trim().Length > 0) sb.AppendLine(line.TrimEnd());
            return sb.ToString();
        }
    }
}
