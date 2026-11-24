using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace HelloCleanUp.Core
{
    /// <summary>
    /// Who the program runs as, and who is actually signed in to this Windows session.
    /// They differ when UAC was answered with another administrator's password:
    /// then every HKCU tweak lands in that administrator's profile, not the signed-in user's.
    /// </summary>
    public static class UserContext
    {
        private static string _sid;

        /// <summary>SID of the account the process runs as (whose hive HKCU is).</summary>
        public static string Sid
        {
            get
            {
                if (_sid == null)
                {
                    using (var id = WindowsIdentity.GetCurrent())
                        _sid = id.User != null ? id.User.Value : "unknown";
                }
                return _sid;
            }
        }

        public static string ProcessUser
        {
            get { return Environment.UserDomainName + "\\" + Environment.UserName; }
        }

        /// <summary>DOMAIN\user signed in to the current session, or null if it can't be told.</summary>
        public static string SessionUser()
        {
            var user = Query(WTSUserName);
            if (string.IsNullOrEmpty(user)) return null;
            var domain = Query(WTSDomainName);
            return string.IsNullOrEmpty(domain) ? user : domain + "\\" + user;
        }

        /// <summary>The signed-in user, when it is someone other than the account the program runs as; otherwise null.</summary>
        public static string OtherSessionUser()
        {
            try
            {
                var session = SessionUser();
                if (session == null) return null;
                var sessionName = session.Substring(session.LastIndexOf('\\') + 1);
                return string.Equals(sessionName, Environment.UserName, StringComparison.OrdinalIgnoreCase) ? null : session;
            }
            catch
            {
                return null;
            }
        }

        private const int WTSUserName = 5, WTSDomainName = 7;
        private const int WTS_CURRENT_SESSION = -1;

        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WTSQuerySessionInformation(IntPtr hServer, int sessionId, int infoClass, out IntPtr buffer, out int bytesReturned);

        [DllImport("wtsapi32.dll")]
        private static extern void WTSFreeMemory(IntPtr memory);

        private static string Query(int infoClass)
        {
            IntPtr buffer;
            int bytes;
            if (!WTSQuerySessionInformation(IntPtr.Zero, WTS_CURRENT_SESSION, infoClass, out buffer, out bytes)) return null;
            try { return Marshal.PtrToStringUni(buffer); }
            finally { WTSFreeMemory(buffer); }
        }
    }
}
