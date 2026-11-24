using System;

namespace HelloCleanUp.Core
{
    /// <summary>
    /// "Revert everything" works from the backup file itself, not from the current tweak list:
    /// entries written by older versions for tweaks that were later renamed or removed are reverted too.
    /// </summary>
    public static class Rollback
    {
        /// <summary>Reverts every entry, newest first. Returns how many entries are left (could not be reverted).</summary>
        public static int RevertAll(BackupStore backup, Action<string> log)
        {
            foreach (var key in backup.KeysNewestFirst())
            {
                try
                {
                    if (key.StartsWith("reg|", StringComparison.Ordinal)) RegAction.RevertKey(backup, key, log);
                    else if (key.StartsWith("svc|", StringComparison.Ordinal)) ServiceAction.RevertKey(backup, key, log);
                    else if (key.StartsWith("task|", StringComparison.Ordinal)) TaskAction.RevertKey(backup, key, log);
                    else log("  непонятная запись бэкапа «" + key + "», пропуск");
                }
                catch (Exception ex)
                {
                    log("  ошибка при откате «" + key + "»: " + ex.Message);
                }
            }
            return backup.Count;
        }
    }
}
