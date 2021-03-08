using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ParTree
{
    // Adapted from https://stackoverflow.com/questions/42910628/is-there-a-way-to-get-the-windows-default-folder-icon-using-c
    public static class IconHelper
    {
        // https://docs.microsoft.com/en-us/windows/win32/api/shellapi/ne-shellapi-shstockiconid
        public enum SIID
        {
            DOCNOASSOC,
            DOCASSOC,
            APPLICATION,
            FOLDER,
            FOLDEROPEN,
            DRIVE525,
            DRIVE35,
            DRIVEREMOVE,
            DRIVEFIXED,
            DRIVENET,
            DRIVENETDISABLED,
            DRIVECD,
            DRIVERAM,
            WORLD,
            SERVER = 15,
            PRINTER,
            MYNETWORK,
            FIND = 22,
            HELP,
            SHARE = 28,
            LINK,
            SLOWFILE,
            RECYCLER,
            RECYCLERFULL,
            MEDIACDAUDIO = 40,
            LOCK = 47,
            AUTOLIST = 49,
            PRINTERNET,
            SERVERSHARE,
            PRINTERFAX,
            PRINTERFAXNET,
            PRINTERFILE,
            STACK,
            MEDIASVCD,
            STUFFEDFOLDER,
            DRIVEUNKNOWN,
            DRIVEDVD,
            MEDIADVD,
            MEDIADVDRAM,
            MEDIADVDRW,
            MEDIADVDR,
            MEDIADVDROM,
            MEDIACDAUDIOPLUS,
            MEDIACDRW,
            MEDIACDR,
            MEDIACDBURN,
            MEDIABLANKCD,
            MEDIACDROM,
            AUDIOFILES,
            IMAGEFILES,
            VIDEOFILES,
            MIXEDFILES,
            FOLDERBACK,
            FOLDERFRONT,
            SHIELD,
            WARNING,
            INFO,
            ERROR,
            KEY,
            SOFTWARE,
            RENAME,
            DELETE,
            MEDIAAUDIODVD,
            MEDIAMOVIEDVD,
            MEDIAENHANCEDCD,
            MEDIAENHANCEDDVD,
            MEDIAHDDVD,
            MEDIABLURAY,
            MEDIAVCD,
            MEDIADVDPLUSR,
            MEDIADVDPLUSRW,
            DESKTOPPC,
            MOBILEPC,
            USERS,
            MEDIASMARTMEDIA,
            MEDIACOMPACTFLASH,
            DEVICECELLPHONE,
            DEVICECAMERA,
            DEVICEVIDEOCAMERA,
            DEVICEAUDIOPLAYER,
            NETWORKCONNECT,
            INTERNET,
            ZIPFILE,
            SETTINGS,
            DRIVEHDDVD = 132,
            DRIVEBD,
            MEDIAHDDVDROM,
            MEDIAHDDVDR,
            MEDIAHDDVDRAM,
            MEDIABDROM,
            MEDIABDR,
            MEDIABDRE,
            CLUSTEREDDRIVE,
            MAX_ICONS = 181
        };

        public static ImageSource GetImageSource(string path, bool large = false)
        {
            using var icon = FetchIcon(path, large);
            return icon == null
                ? GetImageSource(SIID.DOCNOASSOC, large)
                : Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }

        public static ImageSource GetImageSource(SIID iconId, bool large = false)
        {
            using var icon = StockIcons.GetStockIcon(iconId, large);
            return Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }

        private static Icon? FetchIcon(string path, bool large)
        {
            return Directory.Exists(path) ? FileInfo.ExtractFromPath(path, large)
                : File.Exists(path) ? Icon.ExtractAssociatedIcon(path)
                : throw new FileNotFoundException();
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        private static class FileInfo
        {
            private enum SHGFI
            {
                LARGEICON = 0x0,
                SMALLICON = 0x1,
                OPENICON = 0x2,
                SHELLICONSIZE = 0x4,
                PIDL = 0x8,
                USEFILEATTRIBUTES = 0x10,
                ADDOVERLAYS = 0x20,
                OVERLAYINDEX = 0x40,
                ICON = 0x100,
                DISPLAYNAME = 0x200,
                TYPENAME = 0x400,
                ATTRIBUTES = 0x800,
                ICONLOCATION = 0x1000,
                EXETYPE = 0x2000,
                SYSICONINDEX = 0x4000,
                LINKOVERLAY = 0x8000,
                SELECTED = 0x10000,
                ATTR_SPECIFIED = 0x20000
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct SHFILEINFO
            {
                public IntPtr hIcon;
                public int iIcon;
                public uint dwAttributes;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
                public string szDisplayName;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
                public string szTypeName;
            };

            [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
            private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

            public static Icon ExtractFromPath(string path, bool large)
            {
                var shinfo = new SHFILEINFO();
                SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), (uint)(SHGFI.ICON | (large ? SHGFI.LARGEICON : SHGFI.SMALLICON)));
                var icon = (Icon)Icon.FromHandle(shinfo.hIcon).Clone();
                DestroyIcon(shinfo.hIcon);
                return icon;
            }
        }
        private static class StockIcons
        {
            [Flags]
            private enum SHGSI : uint
            {
                LARGEICON = 0x0,
                SMALLICON = 0x1,
                SHELLICONSIZE = 0x4,
                ICON = 0x100,
                SYSICONINDEX = 0x4000,
                LINKOVERLAY = 0x8000,
                ICONLOCATION = 0x1000,
                SELECTED = 0x10000
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct SHSTOCKICONINFO
            {
                public uint cbSize;
                public IntPtr hIcon;
                public int iSysIconIndex;
                public int iIcon;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
                public string szPath;
            }

            [DllImport("shell32.dll")]
            private static extern int SHGetStockIconInfo(uint siid, uint uFlags, ref SHSTOCKICONINFO psii);

            public static Icon GetStockIcon(SIID iconId, bool large)
            {
                var info = new SHSTOCKICONINFO();
                info.cbSize = (uint)Marshal.SizeOf(info);
                var result = SHGetStockIconInfo((uint)iconId, (uint)(SHGSI.ICON | (large ? SHGSI.LARGEICON : SHGSI.SMALLICON)), ref info);

                if (result != 0)
                {
                    throw new Win32Exception(result);
                }

                var icon = (Icon)Icon.FromHandle(info.hIcon).Clone(); // Get a copy that doesn't use the original handle
                DestroyIcon(info.hIcon); // Clean up native icon to prevent resource leak

                return icon;
            }
        }
    }
}
