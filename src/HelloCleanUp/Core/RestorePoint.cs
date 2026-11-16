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
            output = (output ?? "").Trim();
            if (output.EndsWith("OK", StringComparison.Ordinal))
            {
                log("  точка восстановления «Hello CleanUp» создана");
                return true;
            }
            var msg = output.StartsWith("FAIL:", StringComparison.Ordinal) ? output.Substring(5) : output;
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
