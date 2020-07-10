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

        /// <summary>Files covered by a recovery file, in this directory and all subdirectories</summary>
        private readonly List<ParTreeFile> _allRecoverableFiles;
        /// <summary>Files in this directory covered by a recovery file</summary>
        private IReadOnlyList<ParTreeFile> RecoverableFiles => _allRecoverableFiles.Where(x => x.DirPath == DirPath).ToList();
        /// <summary>All files in this directory and its subdirectories</summary>
        private IReadOnlyCollection<ParTreeFile> AllFiles => Files.Concat(Subdirectories.SelectMany(x => x.AllFiles)).ToList();
        /// <summary>Files currently in this directory, and any that the recovery file says should be here</summary>
        private readonly Lazy<IReadOnlyCollection<ParTreeFile>> _files;
        /// <summary>Files currently in this directory, and any that the recovery file says should be here</summary>
        public IReadOnlyCollection<ParTreeFile> Files => _files.Value;
        /// <summary>Files in this directory or any subdirectory with recovery files, but not included in those recovery files</summary>
        public IReadOnlyCollection<ParTreeFile> AllNewFiles => _selected ? AllFiles.Where(x => x.Status == FileStatus.New).ToList() : Subdirectories.SelectMany(x => x.AllNewFiles).ToList();

        private bool HasSelectedAncestor => _parent != null && (_parent._selected || _parent.HasSelectedAncestor);
        private bool ContainsSelectedSubdirectory => !_selected && ThisRecoveryDirInfo.EnumerateDirectoriesOrEmpty().Any(x => x.EnumerateFilesOrEmpty($"*.{PAR2_EXTENSION}", SearchOption.AllDirectories).Any());
        /// <summary>If this directory or any of its subdirectories has recovery files</summary>
        public bool ContainsRecoverableFiles => HasRecoveryFiles || ContainsSelectedSubdirectory;

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
        public bool? Selected
        {
            get => _selected ? true : ContainsSelectedSubdirectory ? (bool?)null : false;
            set
            {
                _selected = value ?? false;

                OnPropertyChanged();
                OnPropertyChanged(nameof(Enabled));

                foreach (var subDir in Subdirectories)
                {
                    // This only has to be done when this.Selected = true, but do it always so that subdirectories fire their OnPropertyChanged event for Enabled.
                    subDir.Selected = false;
                }
            }
        }

        public bool? Verified => !HasRecoveryFiles || !_files.IsValueCreated || AllFiles.Any(x => !x.IsVerified)
            ? (bool?)null
            : _files.Value.Where(x => x.IsVerifiable).All(x => x.IsComplete) && Subdirectories.All(x => x.Verified == true);

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
                    var hasFilesInThisDirectory = _files.Value.Any();
                    var hasFilesInSubdirectories = AllFiles.Count > _files.Value.Count;

                    return $@"{(hasFilesInThisDirectory ? $@"In this directory
{summary(_files.Value)}" : "")}{(hasFilesInThisDirectory && hasFilesInSubdirectories ? @"

" : "")}{(hasFilesInSubdirectories ? $@"In subdirectories
{summary(AllFiles.Except(_files.Value))}" : "")}";
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

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

            _subdirectories = new Lazy<IReadOnlyCollection<ParTreeDirectory>>(() =>
            {
                try
                {
                    var existingSubdirs = DirInfo.EnumerateDirectoriesOrEmpty()
                        .Where(childDirInfo => childDirInfo.Name != RECOVERY_DIR_NAME)
                        .Select(x => x.FullName);
                    var recoverableSubDirs = _allRecoverableFiles
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

            _allRecoverableFiles = new List<ParTreeFile>();
            _files = new Lazy<IReadOnlyCollection<ParTreeFile>>(() =>
            {
                try
                {
                    var existantFiles = DirInfo.EnumerateFilesOrEmpty().Select(x => x.FullName).ToList();

                    if (HasRecoveryFiles)
                    {
                        if (IsBaseDir)
                        {
                            _allRecoverableFiles.AddRange(Task.Run(() => Par2Helper.List(ThisRecoveryFilePath)).Result
                                .Select(x => new ParTreeFile(Path.GetFullPath(x, DirPath), FileStatus.Unverified)));
                        }
                        else
                        {
                            _allRecoverableFiles.AddRange(_parent!._allRecoverableFiles.Where(x => x.DirPath.StartsWith(DirPath, StringComparison.Ordinal)));
                        }
                    }

                    foreach (var file in RecoverableFiles)
                    {
                        file.Status = existantFiles.Contains(file.FullName) ? FileStatus.Unverified : FileStatus.Missing;
                    }

                    var unverifiableFiles = existantFiles.Except(RecoverableFiles.Select(x => x.FullName));

                    return HasRecoveryFiles
                        ? RecoverableFiles.Concat(unverifiableFiles.Select(x => new ParTreeFile(x, FileStatus.New))).ToList()
                        : unverifiableFiles.Select(x => new ParTreeFile(x, FileStatus.Unknown)).ToList();
                }
                catch
                {
                    Accessible = false;
                    return new List<ParTreeFile>(0);
                }
            });
        }

        public async Task CreateRecoveryFiles(Action<string> updateStatus, double redundancy, bool recreateExisting, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (!WorkingDir.ThisRecoveryDirInfo.Exists)
            {
                // Create base recovery dir so it can be set to hidden. Par2j will create any other dirs required within it.
                WorkingDir.ThisRecoveryDirInfo.Create();
                WorkingDir.ThisRecoveryDirInfo.Attributes |= FileAttributes.Hidden;
            }

            if (_selected)
            {
                if (recreateExisting || !HasRecoveryFiles)
                {
                    foreach (var par2file in ThisRecoveryFileInfos)
                    {
                        par2file.Delete();
                    }

                    var exitCode = await Par2Helper.Create(DirPath, ThisRecoveryFilePath, updateStatus, redundancy, token);
                    if (exitCode == 0)
                    {
                        _allRecoverableFiles.AddRange(AllFiles.Except(_allRecoverableFiles));
                        foreach (var file in _allRecoverableFiles)
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
                }
            }
            else if (HasSelectedAncestor)
            {
                OnPropertyChanged(nameof(Verified));
            }

            OnPropertyChanged(nameof(Verified));
            OnPropertyChanged(nameof(StatusSummary));
            OnPropertyChanged(nameof(Files));

            foreach (var subDir in Subdirectories)
            {
                await subDir.CreateRecoveryFiles(updateStatus, redundancy, recreateExisting, token);
            }
        }

        public void DeleteUnusedRecoveryFiles()
        {
            if (!ThisRecoveryDirInfo.Exists)
            {
                return;
            }

            if (!_selected)
            {
                foreach (var par2file in ThisRecoveryFileInfos)
                {
                    par2file.Delete();
                }

                for (var parent = _parent; parent != null; parent = parent._parent)
                {
                    parent.OnPropertyChanged(nameof(Selected));
                }
            }

            foreach (var subDir in Subdirectories)
            {
                subDir.DeleteUnusedRecoveryFiles();
            }

            if (ThisRecoveryDirInfo.Exists && !ThisRecoveryDirInfo.EnumerateDirectories().Any() && !ThisRecoveryDirInfo.EnumerateFiles().Any())
            {
                // Ensure checkboxes of parent dirs are tri-stated when appropriate.
                ThisRecoveryDirInfo.Delete();
            }
        }

        public async Task VerifyFiles(Action<string> updateStatus, CancellationToken token)
        {
            await ((!_selected && HasSelectedAncestor)
                ? BaseDir!.verifyFiles(updateStatus, token) // It's not possible to verify only some of the files covered by a recovery file, so do the whole lot.
                : verifyFiles(updateStatus, token));
        }
        private async Task verifyFiles(Action<string> updateStatus, CancellationToken token)
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

            foreach (var subDir in Subdirectories)
            {
                await subDir.verifyFiles(updateStatus, token);
            }

            OnPropertyChanged(nameof(Verified));
            OnPropertyChanged(nameof(StatusSummary));
            OnPropertyChanged(nameof(Files));
        }

        public async Task RepairFiles(Action<string> updateStatus, CancellationToken token)
        {
            await ((!_selected && HasSelectedAncestor)
                ? BaseDir!.repairFiles(updateStatus, token) // It's not possible to repair only some of the files covered by a recovery file, so do the whole lot.
                : repairFiles(updateStatus, token));
        }
        private async Task repairFiles(Action<string> updateStatus, CancellationToken token)
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
                    foreach (var file in _allRecoverableFiles)
                    {
                        file.Status = FileStatus.Complete;
                    }
                }
            }

            OnPropertyChanged(nameof(Verified));
            OnPropertyChanged(nameof(StatusSummary));
            OnPropertyChanged(nameof(Files));

            foreach (var subDir in Subdirectories)
            {
                await subDir.repairFiles(updateStatus, token);
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
        private readonly string _filePath;
        private FileInfo _fileInfo => new FileInfo(_filePath);
        public string Name => _fileInfo.Name;
        public string FullName => _fileInfo.FullName;
        public string DirPath => _fileInfo.Directory.FullName;

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

        public ParTreeFile(string path, FileStatus status)
        {
            _filePath = path;
            Status = status;
        }

        public bool IsInSubdirectoryOf(DirectoryInfo dir) => dir.FullName == _fileInfo.Directory.Parent.FullName;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
