using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace ParTree
{
    public class ParTreeDirectory : INotifyPropertyChanged
    {
        private static readonly string RECOVERY_DIR_NAME = ".ParTree";
        private static readonly string PAR2_EXTENSION = "par2";

        public string DirPath { get; }
        /// <summary>If this dir is set as the Base Directory of the associated PAR2 file</summary>
        private bool IsBaseDir => _selected;
        private DirectoryInfo DirInfo => new DirectoryInfo(DirPath);
        public string Name => DirInfo.Name;
        public bool Exists => DirInfo.Exists;

        /// <summary>Location of recovery files that are for this directory, not a parent directory</summary>
        private readonly string _thisRecoveryDirPath;
        /// <summary>Location of recovery files that are for this directory, not a parent directory</summary>
        private DirectoryInfo ThisRecoveryDirInfo => new DirectoryInfo(_thisRecoveryDirPath);
        /// <summary>Recovery files that are for this directory, not a parent directory</summary>
        private string ThisRecoveryFilePath => Path.Combine(_thisRecoveryDirPath, $"{ThisRecoveryDirInfo.Name}.{PAR2_EXTENSION}");
        /// <summary>Recovery files that are for this directory, not a parent directory</summary>
        private IEnumerable<FileInfo> ThisRecoveryFileInfos => ThisRecoveryDirInfo.EnumerateFilesOrEmpty($"*.{PAR2_EXTENSION}");
        /// <summary>If there is a recovery file that covers this directory, which may belong to this directory directly, or a parent directory</summary>
        public bool HasRecoveryFiles => IsBaseDir ? ThisRecoveryFileInfos.Any() : (_parent != null && _parent.HasRecoveryFiles);


        private readonly Lazy<ImageSource> _imageSource;
        /// <summary>Icon for this directory, according to Windows</summary>
        public ImageSource ImageSource => _imageSource.Value;


        private readonly ParTreeDirectory? _parent;
        /// <summary>Base directory of the recovery file for this directory, if any</summary>
        private ParTreeDirectory? BaseDir => IsBaseDir ? this : _parent?.BaseDir;
        /// <summary>The top level that ParTree is currently working with</summary>
        private ParTreeDirectory WorkingDir => _parent == null ? this : _parent.WorkingDir;

        private readonly Lazy<IReadOnlyCollection<ParTreeDirectory>> _subdirectories;
        public IReadOnlyCollection<ParTreeDirectory> Subdirectories => _subdirectories.Value;

        private IReadOnlyCollection<ParTreeFile> _allRecoverableFiles;
        /// <summary>Files covered by a recovery file, in this directory and all subdirectories</summary>
        private IReadOnlyCollection<ParTreeFile> AllRecoverableFiles => IsBaseDir || !HasRecoveryFiles
            ? _allRecoverableFiles
            : _parent!.AllRecoverableFiles
                .Where(x => x.DirPath.StartsWith(DirPath, StringComparison.Ordinal))
                .ToList();
        /// <summary>Files in this directory covered by a recovery file</summary>
        private IReadOnlyList<ParTreeFile> RecoverableFiles => AllRecoverableFiles.Where(x => x.DirPath == DirPath).ToList();
        /// <summary>All files in this directory and its subdirectories</summary>
        private IReadOnlyCollection<ParTreeFile> AllFiles => Files.Concat(Subdirectories.SelectMany(x => x.AllFiles)).ToList();
        /// <summary>Files currently in this directory, and any that the recovery file says should be here</summary>
        public IReadOnlyCollection<ParTreeFile> Files { get; }

        private bool HasSelectedAncestor => _parent != null && (_parent._selected || _parent.HasSelectedAncestor);
        // Note that this checks for recovery files rather than _selected to avoid enumerating Subdirectories, which would be slow if this was the top of a large tree.
        private bool ContainsSelectedSubdirectory => !_selected && ThisRecoveryDirInfo.EnumerateDirectoriesOrEmpty().Any(x => x.EnumerateFilesOrEmpty($"*.{PAR2_EXTENSION}", SearchOption.AllDirectories).Any());
        /// <summary>If this directory or any of its subdirectories has recovery files</summary>
        public bool ContainsRecoverableFiles => (HasRecoveryFiles && AllRecoverableFiles.Any()) || ContainsSelectedSubdirectory;

        /// <summary>Expanded in the GUI tree</summary>
        public bool Expanded { get; set; }

        private bool _accessible;
        /// <summary>False if the contents of this directory can't be read, due to permissions or otherwise</summary>
        public bool Accessible
        {
            get => _accessible;
            set
            {
                _accessible = value;
                OnPropertyChanged();
            }
        }

        public bool Enabled => !HasSelectedAncestor && Accessible;

        private bool _selected;
        // This is nullable so the checkboxes in the GUI can be tri-state.
        [RelatedProperties(nameof(Enabled), nameof(ContainsRecoverableFiles), nameof(Verified))]
        public bool? Selected
        {
            get => _selected ? true : ContainsSelectedSubdirectory ? (bool?)null : false;
            set
            {
                _selected = value ?? false;

                foreach (var subDir in Subdirectories)
                {
                    subDir.Selected = false;
                }

                OnPropertyChanged();
            }
        }

        [RelatedProperties(nameof(StatusSummary), nameof(Files))]
        [RelatedParentProperties(nameof(Verified))]
        public bool? Verified => !ContainsRecoverableFiles
                || !_subdirectories.IsValueCreated
                || (!HasRecoveryFiles && !Subdirectories.Any(x => x.Verified == false) && Subdirectories.Any(x => x.ContainsRecoverableFiles && x.Verified == null))
                || (HasRecoveryFiles && AllFiles.Any(x => x.IsVerifiable && !x.IsVerified) && !AllFiles.Any(x => x.IsIncomplete))
            ? (bool?)null
            : (!HasRecoveryFiles || Files.Where(x => x.IsVerifiable).All(x => x.IsComplete)) && Subdirectories.Where(x => x.ContainsRecoverableFiles).All(x => x.Verified == true);

        public string StatusSummary
        {
            get
            {
                if (!Accessible)
                {
                    return "Unable to read contents of this directory";
                }
                else if (!HasRecoveryFiles)
                {
                    return "No recovery files for this directory";
                }
                else if (!AllFiles.Any())
                {
                    return "Empty";
                }
                else
                {
                    string summary(IEnumerable<ParTreeFile> files) => string.Join(Environment.NewLine, files.GroupBy(x => x.Status).OrderBy(g => (int)g.Key).Select(g => $"{g.Key}: {g.Count()}"));
                    var hasFilesInThisDirectory = Files.Any();
                    var hasFilesInSubdirectories = AllFiles.Count > Files.Count;

                    return $@"{(hasFilesInThisDirectory ? $@"In this directory
{summary(Files)}" : "")}{(hasFilesInThisDirectory && hasFilesInSubdirectories ? @"

" : "")}{(hasFilesInSubdirectories ? $@"In subdirectories
{summary(AllFiles.Except(Files))}" : "")}";
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

            var property = GetType().GetProperty(propertyName!);
            var relatedProperties = property == null
                ? throw new ArgumentException($"There is no property with the name '{propertyName}'", nameof(propertyName))
                : property
                    .GetCustomAttributes(typeof(RelatedPropertiesAttribute), inherit: false)
                    .Cast<RelatedPropertiesAttribute>()
                    .FirstOrDefault()?.Names ?? new List<string>();

            foreach (var name in relatedProperties)
            {
                OnPropertyChanged(name);
            }

            if (_parent != null)
            {
                var relatedParentProperties = property
                    .GetCustomAttributes(typeof(RelatedParentPropertiesAttribute), inherit: false)
                    .Cast<RelatedParentPropertiesAttribute>()
                    .FirstOrDefault()?.Names ?? new List<string>();

                foreach (var name in relatedParentProperties)
                {
                    _parent.OnPropertyChanged(name);
                }
            }
        }

        public ParTreeDirectory(string path) : this(path, null)
        {
            Expanded = true;
        }

        private ParTreeDirectory(string path, ParTreeDirectory? parent)
        {
            DirPath = path;
            _parent = parent;
            _thisRecoveryDirPath = parent == null ? Path.Combine(path, RECOVERY_DIR_NAME) : Path.Combine(parent._thisRecoveryDirPath, DirInfo.Name);
            _selected = File.Exists(ThisRecoveryFilePath);
            _accessible = true;

            _imageSource = new Lazy<ImageSource>(() => DirInfo.Exists ? IconHelper.GetImageSource(DirPath) : IconHelper.GetImageSource(IconHelper.SIID.FOLDERBACK));

            _allRecoverableFiles = IsBaseDir
                ? Task.Run(() => Par2Helper.List(ThisRecoveryFilePath)).Result
                    .Select(x => new ParTreeFile(Path.GetFullPath(x, DirPath), FileStatus.Unverified))
                    .ToList()
                : new List<ParTreeFile>(0);

            try
            {
                var existantFiles = DirInfo.EnumerateFilesOrEmpty();
                var unverifiableFiles = DirInfo.EnumerateFilesOrEmpty()
                    .Where(x => !AllRecoverableFiles.Any(y => x.FullName == y.FullName))
                    .Select(x => new ParTreeFile(x, FileStatus.Unknown));

                Files = unverifiableFiles.Concat(RecoverableFiles).ToList();

                var missingFiles = RecoverableFiles
                    .Where(x => !existantFiles.Any(y => x.FullName == y.FullName));

                foreach (var file in missingFiles)
                {
                    file.Status = FileStatus.Missing;
                }
            }
            catch
            {
                Accessible = false;
                Files = new List<ParTreeFile>(0);
            }

            _subdirectories = new Lazy<IReadOnlyCollection<ParTreeDirectory>>(() =>
            {
                try
                {
                    var existingSubdirs = DirInfo.EnumerateDirectoriesOrEmpty()
                        .Where(childDirInfo => childDirInfo.Name != RECOVERY_DIR_NAME)
                        .Select(x => x.FullName);
                    var recoverableSubDirs = AllRecoverableFiles
                        .Where(x => x.IsVerifiable && x.IsInSubdirectoryOf(DirInfo))
                        .Select(x => x.DirPath)
                        .Distinct();
                    var subdirsContaningRecovery = ThisRecoveryDirInfo.EnumerateDirectoriesOrEmpty()
                        .Where(x => x.EnumerateFiles($"*.{PAR2_EXTENSION}", SearchOption.AllDirectories).Any())
                        .Select(x => x.FullName.Replace($"{RECOVERY_DIR_NAME}\\", "", StringComparison.Ordinal));

                    return existingSubdirs.Concat(recoverableSubDirs).Concat(subdirsContaningRecovery).Distinct()
                        // Passing "this" out here. This is fine since _subdirectories is lazy, so the subsequent constructor isn't executed until after
                        // construction of "this" is completed... as long as nothing within this constructor causes _subdirectories to be initialized.
                        .Select(x => new ParTreeDirectory(x, this))
                        .ToList();
                }
                catch
                {
                    Accessible = false;
                    return new List<ParTreeDirectory>(0);
                }
            });
        }

        // Call this to ensure that Verified can be computed for dirs with selected subdirs before they have been revealed in the TreeView.
        public static async Task CheckForVerifiableFiles(ParTreeDirectory topDir, Action<bool> foundRecoveryFiles, CancellationToken token)
        {
            // Unfortunately .Net doesn't have a built-in way to limit the number of async tasks running at once. Using a semaphore for that purpose seems to perform pretty well.
            // Since this involves IO, there's probably a limit to how many it's worth running in parallel. Not sure how to determine a good limit, so just pick 16.
            var maxThreads = Math.Min(Environment.ProcessorCount, 16);
            using (var sem = new SemaphoreSlim(maxThreads))
            {
                IEnumerable<ParTreeDirectory> dirs = new List<ParTreeDirectory> { topDir };

                while (dirs.Any() && !token.IsCancellationRequested)
                {
                    var tasks = dirs.Where(dir => dir.ContainsRecoverableFiles).Select(async dir =>
                    {
                        await sem.WaitAsync();

                        try
                        {
                            foundRecoveryFiles(dir.IsBaseDir);
                            return token.IsCancellationRequested
                                ? new List<ParTreeDirectory>(0)
                                : await Task.Run(() => dir.Subdirectories);
                        }
                        finally
                        {
                            sem.Release();
                        }
                    });

                    dirs = (await Task.WhenAll(tasks)).SelectMany(x => x);
                }
            }
        }

        /// <returns>Files in this directory or any subdirectory with recovery files, but not included in those recovery files</returns>
        public async Task<IReadOnlyCollection<ParTreeFile>> GetAllNewFiles(Action<int> filesFound, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return new List<ParTreeFile>();
            }

            if (IsBaseDir)
            {
                var newFiles = AllFiles.Where(x => x.Status == FileStatus.New).ToList();
                filesFound(newFiles.Count);
                return newFiles;
            }
            else
            {
                var newFiles = new List<ParTreeFile>();
                filesFound(0);
                foreach (var dir in Subdirectories)
                {
                    newFiles.AddRange(await dir.GetAllNewFiles(filesFound, token));
                }
                return newFiles;
            }
        }

        public async Task CreateRecoveryFiles(Action<string> updateStatus, double redundancy, CancellationToken token)
        {
            if (!WorkingDir.ThisRecoveryDirInfo.Exists)
            {
                // Create base recovery dir so it can be set to hidden. Par2j will create any other dirs required within it.
                WorkingDir.ThisRecoveryDirInfo.Create();
                WorkingDir.ThisRecoveryDirInfo.Attributes |= FileAttributes.Hidden;
            }

            var exitCode = await Par2Helper.Create(DirPath, ThisRecoveryFilePath, updateStatus, redundancy, token);
            if (exitCode == 0)
            {
                _allRecoverableFiles = AllFiles;

                foreach (var file in AllRecoverableFiles)
                {
                    file.Status = FileStatus.Complete;
                }

                for (var parent = _parent; parent != null; parent = parent._parent)
                {
                    // Ensure checkboxes of parent dirs are tri-stated when appropriate.
                    parent.OnPropertyChanged(nameof(Selected));
                }
            }
            else
            {
                // TODO: Display error.
            }

            OnPropertyChanged(nameof(Verified));
        }

        public void DeleteUnusedRecoveryFiles()
        {
            if (!ThisRecoveryDirInfo.Exists)
            {
                return;
            }

            foreach (var subDir in Subdirectories)
            {
                subDir.DeleteUnusedRecoveryFiles();
            }

            if (!_selected)
            {
                foreach (var par2file in ThisRecoveryFileInfos)
                {
                    par2file.Delete();
                }

                _allRecoverableFiles = new List<ParTreeFile>(0);

                DeleteRecoveryDirIfempty();

                for (var parent = _parent; parent != null; parent = parent._parent)
                {
                    parent.DeleteRecoveryDirIfempty();
                }
            }
        }

        private void DeleteRecoveryDirIfempty()
        {
            if (ThisRecoveryDirInfo.Exists && !ThisRecoveryDirInfo.EnumerateDirectories().Any() && !ThisRecoveryDirInfo.EnumerateFiles().Any())
            {
                ThisRecoveryDirInfo.Delete();
                ThisRecoveryDirInfo.Refresh(); // Sometimes DirectoryInfo.Exists remains true after calling Delete. Seems to happen if the dir is open in Windows Explorer when it's deleted. Calling Refresh fixes it.
                OnPropertyChanged(nameof(Selected));
            }
        }

        public async Task VerifyFiles(Action<bool> verified, Action<string> updateStatus, CancellationToken token)
        {
            await ((!_selected && HasSelectedAncestor)
                ? BaseDir!.verifyFiles(verified, updateStatus, token) // It's not possible to verify only some of the files covered by a recovery file, so do the whole lot.
                : verifyFiles(verified, updateStatus, token));
        }
        private async Task verifyFiles(Action<bool> verified, Action<string> updateStatus, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (_selected)
            {
                var results = DirInfo.Exists
                    ? await Par2Helper.Verify(DirPath, ThisRecoveryFilePath, updateStatus, token)
                    : null; // If the directory is missing, par2j can't check it. Just flag everything as missing.
                Func<ParTreeFile, string?> statusOfFile = DirInfo.Exists
                    ? file => results.SingleOrDefault(x => Path.GetFullPath(x.Filename, DirPath) == file.FullName)?.Status
                    : (Func<ParTreeFile, string?>)(file => "Missing");

                foreach (var file in AllFiles)
                {
                    file.Status = statusOfFile(file) switch
                    {
                        "Complete" => FileStatus.Complete,
                        "Missing" => FileStatus.Missing,
                        null => FileStatus.New,
                        // There are multiple types of corruption that can be detected, but there's no need to distinguish between them.
                        _ => FileStatus.Corrupt
                    };
                }
            }

            verified(_selected);

            foreach (var subDir in Subdirectories)
            {
                await subDir.verifyFiles(verified, updateStatus, token);
            }

            OnPropertyChanged(nameof(Verified));
        }

        public async Task RepairFiles(Action<bool> repaired, Action<string> updateStatus, CancellationToken token)
        {
            await ((!_selected && HasSelectedAncestor)
                ? BaseDir!.repairFiles(repaired, updateStatus, token) // It's not possible to repair only some of the files covered by a recovery file, so do the whole lot.
                : repairFiles(repaired, updateStatus, token));
        }
        private async Task repairFiles(Action<bool> repaired, Action<string> updateStatus, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (_selected)
            {
                var exitCode = await Par2Helper.Repair(DirPath, ThisRecoveryFilePath, updateStatus, token);

                if (exitCode == 0 || exitCode == 16)
                {
                    foreach (var file in AllRecoverableFiles)
                    {
                        file.Status = FileStatus.Complete;
                    }
                }
            }

            OnPropertyChanged(nameof(Verified));
            repaired(_selected);

            foreach (var subDir in Subdirectories)
            {
                await subDir.repairFiles(repaired, updateStatus, token);
            }
        }
    }

    public enum FileStatus
    {
        /// <summary>This file cannot be verified because there is no recovery file</summary>
        Unknown,
        /// <summary>Recovery file exists, but this file has not been checked against it</summary>
        Unverified,
        /// <summary>This file exists but is not listed in the recovery file</summary>
        New,
        /// <summary>Listed in recovery file, but not found</summary>
        Missing,
        /// <summary>This file is consistent with the recovery file</summary>
        Complete,
        /// <summary>This file exists but is not consistent with the recovery file</summary>
        Corrupt
    }

    public class ParTreeFile : INotifyPropertyChanged
    {
        public string Name { get; }
        public string FullName { get; }
        public string DirPath { get; }
        private readonly string _parentDirPath;

        private FileStatus _fileStatus;
        public FileStatus Status
        {
            get => _fileStatus;
            set
            {
                _fileStatus = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsVerifiable));
                OnPropertyChanged(nameof(IsVerified));
                OnPropertyChanged(nameof(IsComplete));
                OnPropertyChanged(nameof(IsIncomplete));
            }
        }
        /// <summary>Could be verified, but has not been yet</summary>
        public bool IsVerifiable => Status != FileStatus.Unknown && Status != FileStatus.New;
        public bool IsVerified => Status != FileStatus.Unknown && Status != FileStatus.Unverified;
        public bool IsComplete => Status == FileStatus.Complete;
        public bool IsIncomplete => Status == FileStatus.Missing || Status == FileStatus.Corrupt;

        public ParTreeFile(string path, FileStatus status) : this(new FileInfo(path), status) { }
        public ParTreeFile(FileInfo fileInfo, FileStatus status)
        {
            Status = status;
            DirPath = fileInfo.Directory.FullName;
            FullName = fileInfo.FullName;
            Name = fileInfo.Name;
            _parentDirPath = fileInfo.Directory.Parent?.FullName ?? "";
        }

        public bool IsInSubdirectoryOf(DirectoryInfo dir) => dir.FullName == _parentDirPath;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
