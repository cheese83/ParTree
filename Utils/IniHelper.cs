using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace ParTree
{
    public static class IniHelper
    {
        private static readonly string INI_PATH = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.ini");
        private static readonly int MAX_READ_LENGTH = 100; // I don't think there's an upper limit, but it has to be set to something.

        public static string LoadValue(string section, string key)
        {
            var returned = new StringBuilder(MAX_READ_LENGTH);
            _ = GetPrivateProfileString(section, key, "", returned, returned.Capacity, INI_PATH);
            return returned.ToString();
        }

        public static void SaveValue(string section, string key, string value)
        {
            if (WritePrivateProfileString(section, key, value, INI_PATH) == 0)
            {
                var errorCode = Marshal.GetLastWin32Error();
                throw new Win32Exception(errorCode);
            }
        }


        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern int GetPrivateProfileString(string lpAppName, string lpKeyName, string lpDefault, StringBuilder lpReturnedString, int nSize, string lpFileName);

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern long WritePrivateProfileString(string lpAppName, string lpKeyName, string lpString, string lpFileName);
    }
}
