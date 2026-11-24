using System;

namespace HelloCleanUp.Core
{
    public static class RestorePoint
    {
        /// <summary>
        /// Turns on System Protection for the system drive (it is often off on fresh installs)
        /// and creates a restore point. Windows allows one restore point per 24h by default;
        /// in that case the existing one is used and that is fine.
        /// </summary>
        public static bool Create(Action<string> log)
        {
            log("Создаю точку восстановления…");
            const string script =
                "$ErrorActionPreference='Stop';" +
                "try { Enable-ComputerRestore -Drive \"$env:SystemDrive\\\" } catch { }" +
                "try { Checkpoint-Computer -Description 'Hello CleanUp' -RestorePointType 'MODIFY_SETTINGS' -WarningAction Stop; 'OK' }" +
                "catch { 'FAIL:' + $_.Exception.Message }";
            string output;
            Shell.PowerShell(script, out output, 600000);

            // Look for our own marker line, wherever it is: PowerShell may print other things around it.
            string fail = null;
            foreach (var raw in (output ?? "").Split('\n'))
            {
                var line = raw.Trim();
                if (line == "OK")
                {
                    log("  точка восстановления «Hello CleanUp» создана");
                    return true;
                }
                if (fail == null && line.StartsWith("FAIL:", StringComparison.Ordinal)) fail = line.Substring(5).Trim();
            }
            var msg = fail ?? (output ?? "").Trim();
            if (msg.Length == 0) msg = "PowerShell ничего не ответил";
            if (msg.IndexOf("1440", StringComparison.Ordinal) >= 0)
            {
                log("  за последние 24 часа точка уже создавалась — Windows не даёт делать их чаще, используем её");
                return true;
            }
            log("  не удалось создать точку восстановления: " + msg);
            return false;
        }
    }
}
