using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ParTree
{
    public static class IoExtensions
    {
        /// <returns>Subdirectories of this directory, or an empty enumerable if the directory does not exist</returns>
        public static IEnumerable<DirectoryInfo> EnumerateDirectoriesOrEmpty(this DirectoryInfo dir) =>
            dir.Exists ? dir.EnumerateDirectories() : Enumerable.Empty<DirectoryInfo>();

        /// <returns>Files in this directory, or an empty enumerable if the directory does not exist</returns>
        public static IEnumerable<FileInfo> EnumerateFilesOrEmpty(this DirectoryInfo dir) =>
            dir.Exists ? dir.EnumerateFiles() : Enumerable.Empty<FileInfo>();

        /// <returns>Files in this directory, or an empty enumerable if the directory does not exist</returns>
        public static IEnumerable<FileInfo> EnumerateFilesOrEmpty(this DirectoryInfo dir, string searchPattern) =>
            dir.Exists ? dir.EnumerateFiles(searchPattern) : Enumerable.Empty<FileInfo>();

        /// <returns>Files in this directory, or an empty enumerable if the directory does not exist</returns>
        public static IEnumerable<FileInfo> EnumerateFilesOrEmpty(this DirectoryInfo dir, string searchPattern, SearchOption searchOption) =>
            dir.Exists ? dir.EnumerateFiles(searchPattern, searchOption) : Enumerable.Empty<FileInfo>();
    }
}
