﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ParTree
{
    public partial class MainWindow : Window
    {
        private readonly ParTreeViewModel ViewModel;

        public MainWindow()
        {
            // Show tooltips on all disabled elements by default.
            // Especially important because tooltips are useful for explaining why a control is disabled.
            ToolTipService.ShowOnDisabledProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(true));

            InitializeComponent();
            ViewModel = new ParTreeViewModel();
            DataContext = ViewModel;

            // Automatically scroll down when new text added, but only if already scrolled to the bottom.
            OutputLogScroll.ScrollChanged += (sender, e) =>
            {
                var needsVerticalScrollbar = e.ViewportHeight < e.ExtentHeight;
                var linesAdded = e.ExtentHeightChange > 0;

                if (!needsVerticalScrollbar || !linesAdded)
                {
                    return;
                }

                var wasScrolledToBottom = (e.VerticalOffset - e.VerticalChange) >= (e.ExtentHeight - e.ExtentHeightChange) - (e.ViewportHeight - e.ViewportHeightChange);

                if (wasScrolledToBottom)
                {
                    ((ScrollViewer)sender).ScrollToBottom();
                }
            };
        }

        /// <returns>True if the task was cancelled</returns>
        private async Task<bool> ShowOverlayUntilComplete(string busyTitle, Func<IProgress<string>, CancellationToken, Task> action)
        {
            ViewModel.ClearOutputLog();
            var tokenSource = new CancellationTokenSource();
            var progress = new Progress<string>(s => ViewModel.BusyProgress = s);
            void cancelButton_Click(object sender, RoutedEventArgs e)
            {
                tokenSource.Cancel();
                ViewModel.AddLineToOutputLog("Cancelled");
            };

            try
            {
                ViewModel.Busy = true;
                ViewModel.BusyTitle = busyTitle;
                ViewModel.BusyProgress = "";
                CancelButton.Click += cancelButton_Click;
                await action(progress, tokenSource.Token);
            }
            finally
            {
                CancelButton.Click -= cancelButton_Click;
                ViewModel.Busy = false;
            }

            return tokenSource.IsCancellationRequested;
        }

        private async void WorkingDirButton_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog()
            {
                ShowNewFolderButton = false,
                RootFolder = Environment.SpecialFolder.Desktop, // Setting the root to "Desktop" causes the actual root to be the parent of SelectedPath.
                SelectedPath = ViewModel.WorkingDirPath,
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                WorkingDirPath.Text = dialog.SelectedPath;
                await SetWorkingDir();
            }
        }

        private async void WorkingDirPath_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Return)
            {
                await SetWorkingDir();
            }
        }

        private async Task SetWorkingDir()
        {
            var cancelled = await ShowOverlayUntilComplete("Checking for recoverable files", (progress, token) =>
            {
                return ViewModel.SetWorkingDir(WorkingDirPath.Text, progress, token);
            });

            // If a dir was previously open, then opening a new one select the first node.
            // That causes spurious scrolling under some conditions, so deselect it now.
            if (DirTree.ItemContainerGenerator.ContainerFromIndex(0) is TreeViewItem firstItem)
            {
                firstItem.IsSelected = false;
            }
        }

        private static T DataContextFromEventSender<T>(object sender) => (T)((FrameworkElement)sender).DataContext;

        private async void TreeViewItem_Checked(object sender, RoutedEventArgs e)
        {
            var dirInfo = DataContextFromEventSender<ParTreeDirectory>(sender);
            var cancelled = await ShowOverlayUntilComplete("Creating recovery Files", (progress, token) =>
            {
                return dirInfo.CreateRecoveryFiles(ViewModel.AddLineToOutputLog, ViewModel.RedundancyPercent, token: token);
            });

            if (cancelled)
            {
                dirInfo.Selected = false;
                // Don't need to delete created files here because TreeViewItem_Unchecked will be called due to the change in Selected, and will delete them.
            }
        }

        // When a tri-state node is unchecked, it is supposed to uncheck all its children too, but if a node has never been expanded
        // then its children don't exist in the TreeView yet, so their Unchecked event isn't called. This means that Selected for *this* node
        // Doesn't actually change, and it's Unchecked event isn't called either. Use the Click event instead.
        private async void TreeViewItem_Click(object sender, RoutedEventArgs e)
        {
            var dirInfo = DataContextFromEventSender<ParTreeDirectory>(sender);

            if (dirInfo.Selected != true) // Selected may be null here because subdirs haven't had their recovery files deleted at this point.
            {
                await ShowOverlayUntilComplete("Deleting old recovery files", (progress, token) =>
                {
                    return Task.Run(async () => await dirInfo.DeleteUnusedRecoveryFiles(token), token);
                });
            }
        }

        private async void CheckForNewFiles_Click(object sender, RoutedEventArgs e)
        {
            await ShowOverlayUntilComplete("Checking for new files", (progress, token) =>
            {
                var dirInfo = DataContextFromEventSender<ParTreeDirectory>(sender);
                return Task.Run(async () =>
                {
                    var dirCount = 0;
                    var fileCount = 0;
                    var files = await dirInfo.GetAllNewFiles(count => progress.Report($"Found {fileCount += count} files in {++dirCount} directories"), token);

                    ViewModel.AddLineToOutputLog($"Found {files.Count} files not included in existing recovery files.");

                    foreach (var file in files)
                    {
                        ViewModel.AddLineToOutputLog(file.FullName);
                    }
                }, token);
            });
        }

        private async void CheckForUnrecoverableDirs_Click(object sender, RoutedEventArgs e)
        {
            await ShowOverlayUntilComplete("Checking for unrecoverable directories", (progress, token) =>
            {
                var dirInfo = DataContextFromEventSender<ParTreeDirectory>(sender);
                return Task.Run(async () =>
                {
                    var dirCount = 0;
                    var dirs = await dirInfo.GetAllUnrecoverableDirectories(count => progress.Report($"Found {dirCount += count} directories"), token);

                    ViewModel.AddLineToOutputLog($"Found {dirs.Count} unselected directories.");

                    foreach (var dir in dirs)
                    {
                        ViewModel.AddLineToOutputLog(dir.DirPath);
                    }
                }, token);
            });
        }

        private async void Verify_Click(object sender, RoutedEventArgs e)
        {
            var verifiedDirCount = 0;
            var completeDirCount = 0;
            await ShowOverlayUntilComplete("Verifying Files", (progress, token) =>
            {
                var dirInfo = DataContextFromEventSender<ParTreeDirectory>(sender);
                return dirInfo.VerifyFiles(verified =>
                {
                    if (verified.HasValue)
                    {
                        completeDirCount += verified.Value ? 1 : 0;
                        progress.Report($"Verified {++verifiedDirCount} directories");
                    }
                }, ViewModel.AddLineToOutputLog, token);
            });
            ViewModel.AddLineToOutputLog($"Verified {verifiedDirCount} directories.", newline: true);
            ViewModel.AddLineToOutputLog($"{completeDirCount} complete.", newline: true);
            ViewModel.AddLineToOutputLog($"{verifiedDirCount - completeDirCount} corrupt.", newline: true);
        }

        private async void Repair_Click(object sender, RoutedEventArgs e)
        {
            await ShowOverlayUntilComplete("Repairing Files", (progress, token) =>
            {
                var dirInfo = DataContextFromEventSender<ParTreeDirectory>(sender);
                var repairedDirCount = 0;
                return dirInfo.RepairFiles(repaired => { if (repaired) progress.Report($"Repaired {++repairedDirCount} directories"); }, ViewModel.AddLineToOutputLog, token);
            });
        }

        private async void Explore_Click(object sender, RoutedEventArgs e)
        {
            var dirInfo = DataContextFromEventSender<ParTreeDirectory>(sender);
            await ProcessHelper.RunProcessAsync
            (
                "cmd",
                "/c",
                "start",
                "\"\"", // "start" uses the first quoted arg as the window title, so the path can't be here. Supply a blank quoted string instead.
                $"\"{dirInfo.DirPath}\""
            );
        }

        private void ContextMenu_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // Select the clicked item, so it's visually clear which one the context menu applies to.
            // This would be better done in XAML, but I can't find a way to do it.
            for (var element = VisualTreeHelper.GetParent((FrameworkElement)sender); element != null; element = VisualTreeHelper.GetParent(element))
            {
                if (element is TreeViewItem item)
                {
                    item.IsSelected = true;
                    break;
                }
            }
        }

        private void OutpuLogClearButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ClearOutputLog();
        }

        protected override void OnClosed(EventArgs e)
        {
            ViewModel.SaveConfig();
        }
    }

    public class ParTreeViewModel : INotifyPropertyChanged
    {
        public static ImageSource WorkingDirIcon => IconHelper.GetImageSource(IconHelper.SIID.FOLDEROPEN);
        public static ImageSource OutpuLogClearIcon => IconHelper.GetImageSource(IconHelper.SIID.DELETE);

        private ParTreeDirectory? _workingDirectory;
        public string? WorkingDirPath
        {
            get => _workingDirectory?.DirPath;
            set
            {
                // Setter needed for two-way binding to work, but the value is actually set in SetWorkingDir.
            }
        }

        public IReadOnlyList<ParTreeDirectory> DirectoryList =>
            _workingDirectory == null
                ? new List<ParTreeDirectory>()
                : new List<ParTreeDirectory> { _workingDirectory };

        private readonly IList<string> _outputLog = new List<string>();
        public string OutputLog => string.Join(Environment.NewLine, _outputLog);

        private bool _busy;
        public bool Busy
        {
            get => _busy;
            set
            {
                _busy = value;
                OnPropertyChanged();
            }
        }
        private string _busyTitle = "Busy";
        public string BusyTitle
        {
            get => _busyTitle;
            set
            {
                _busyTitle = value;
                OnPropertyChanged();
            }
        }
        private string _busyProgress = "";
        public string BusyProgress
        {
            get => _busyProgress;
            set
            {
                _busyProgress = value;
                OnPropertyChanged();
            }
        }

        private double _redundancy;
        public double Redundancy
        {
            get => _redundancy;
            set
            {
                _redundancy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RedundancyPercent));
            }
        }
        // As the possible range of redundancy is very wide, the slider is logarithmic.
        // par2j supports 0.01% to 1000% to 2dp. That's a wider range than is useful, so allow values from 1% to 1000% to 3sf only.
        public double RedundancyPercent => double.Parse(Math.Pow(10, Redundancy).ToString("G3", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public ParTreeViewModel()
        {
            LoadConfig();
        }

        public async Task SetWorkingDir(string? workingDirPath, IProgress<string> progress, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(workingDirPath))
            {
                _workingDirectory = null;
            }
            else
            {
                progress.Report("Initializing");
                await Task.Run(() => _workingDirectory = new ParTreeDirectory(workingDirPath), token);
                var recoveryFiles = 0;
                await ParTreeDirectory.CheckForVerifiableFiles(_workingDirectory!, found => { if (found) progress.Report($"Found {++recoveryFiles} recovery files"); }, token);
            }

            OnPropertyChanged(nameof(WorkingDirPath));
            OnPropertyChanged(nameof(DirectoryList));
        }

        public void LoadConfig()
        {
            var percent = double.TryParse(IniHelper.LoadValue("par2j", nameof(RedundancyPercent)), out var result) ? result : 10.0;
            Redundancy = Math.Log10(percent);
        }

        public void SaveConfig()
        {
            IniHelper.SaveValue("par2j", nameof(RedundancyPercent), RedundancyPercent.ToString(CultureInfo.InvariantCulture));
        }

        public void ClearOutputLog()
        {
            _outputLog.Clear();
            OnPropertyChanged(nameof(OutputLog));
        }

        public void AddLineToOutputLog(string line, bool newline = true)
        {
            if (newline || !_outputLog.Any())
            {
                _outputLog.Add(line);
            }
            else
            {
                var i = _outputLog.Count - 1;
                var previousLine = _outputLog[i];
                _outputLog[i] = line + previousLine[Math.Min(line.Length, previousLine.Length)..];
            }

            OnPropertyChanged(nameof(OutputLog));
        }
    }
}
