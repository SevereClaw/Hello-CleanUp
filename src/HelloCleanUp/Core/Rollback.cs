using System;
using System.Collections.Generic;

namespace HelloCleanUp.Core
{
    /// <summary>
    /// "Revert everything" works from the backup file itself, not from the current tweak list:
    /// entries written by older versions for tweaks that were later renamed or removed are reverted too.
    /// </summary>
    public static class Rollback
    {
        /// <summary>
        /// Reverts every entry, newest first. Removed apps that can only come back from the Microsoft Store are
        /// added to storeLinks. Returns how many entries are left (could not be reverted).
        /// </summary>
        public static int RevertAll(BackupStore backup, Action<string> log, IList<StoreLink> storeLinks = null)
        {
            var apps = new List<string>();
            foreach (var key in backup.KeysNewestFirst())
            {
                try
                {
                    if (key.StartsWith("appx|", StringComparison.Ordinal)) apps.Add(key); // restored together below: one PowerShell run
                    else if (key.StartsWith("reg|", StringComparison.Ordinal)) RegAction.RevertKey(backup, key, log);
                    else if (key.StartsWith("svc|", StringComparison.Ordinal)) ServiceAction.RevertKey(backup, key, log);
                    else if (key.StartsWith("task|", StringComparison.Ordinal)) TaskAction.RevertKey(backup, key, log);
                    else log("  непонятная запись бэкапа «" + key + "», пропуск");
                }
                catch (Exception ex)
                {
                    log("  ошибка при откате «" + key + "»: " + ex.Message);
                }
            }
            if (apps.Count > 0)
            {
                try { AppxManager.Restore(backup, apps, log, storeLinks); }
                catch (Exception ex) { log("  ошибка при возврате приложений: " + ex.Message); }
            }
            return backup.Count;
        }
    }
}
