using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
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
        private async Task<bool> ShowOverlayUntilComplete(string busyMessage, Func<CancellationToken, Task> action)
        {
            var tokenSource = new CancellationTokenSource();
            void cancelButton_Click(object sender, RoutedEventArgs e)
            {
                tokenSource.Cancel();
                ViewModel.AddLineToOutputLog("Cancelled");
            };

            try
            {
                ViewModel.Busy = true;
                ViewModel.BusyMessage = busyMessage;
                CancelButton.Click += cancelButton_Click;
                await action(tokenSource.Token);
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
            var cancelled = await ShowOverlayUntilComplete("Checking for recoverable files", token =>
            {
                return ViewModel.SetWorkingDir(WorkingDirPath.Text, token);
            });

            // If a dir was previously open, then opening a new one select the first node.
            // That causes spurious scrolling under some conditions, so deselect it now.
            var firstItem = DirTree.ItemContainerGenerator.ContainerFromIndex(0) as TreeViewItem;
            if (firstItem != null)
            {
                firstItem.IsSelected = false;
            }
        }

        private T DataContextFromEventSender<T>(object sender) => (T)((FrameworkElement)sender).DataContext;

        private async void TreeViewItem_Checked(object sender, RoutedEventArgs e)
        {
            var dirInfo = DataContextFromEventSender<ParTreeDirectory>(sender);
            var cancelled = await ShowOverlayUntilComplete("Creating recovery Files", token =>
            {
                return dirInfo.CreateRecoveryFiles(ViewModel.AddLineToOutputLog, ViewModel.RedundancyPercent, recreateExisting: false, token: token);
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
        private void TreeViewItem_Click(object sender, RoutedEventArgs e)
        {
            var dirInfo = DataContextFromEventSender<ParTreeDirectory>(sender);

            if (dirInfo.Selected != true) // Selected may be null here because subdirs haven't had their recovery files deleted at this point.
            {
                dirInfo.DeleteUnusedRecoveryFiles();
            }
        }

        private void CheckForNewFiles_Click(object sender, RoutedEventArgs e)
        {
            var dirInfo = DataContextFromEventSender<ParTreeDirectory>(sender);
            var files = dirInfo.AllNewFiles;

            ViewModel.AddLineToOutputLog($"Found {files.Count} files not included in existing recovery files.");

            foreach (var file in files)
            {
                ViewModel.AddLineToOutputLog(file.FullName);
            }
        }

        private async void Recreate_Click(object sender, RoutedEventArgs e)
        {
            await ShowOverlayUntilComplete("Creating recovery Files", token =>
            {
                var dirInfo = DataContextFromEventSender<ParTreeDirectory>(sender);
                return dirInfo.CreateRecoveryFiles(ViewModel.AddLineToOutputLog, ViewModel.RedundancyPercent, recreateExisting: true, token: token);
            });
            // TODO: If this is cancelled partway through, the recovery files might be incomplete. Perhaps change the process to be atomic?
        }

        private void DeleteUnused_Click(object sender, RoutedEventArgs e)
        {
            var dirInfo = DataContextFromEventSender<ParTreeDirectory>(sender);
            dirInfo.DeleteUnusedRecoveryFiles();
        }

        private async void Verify_Click(object sender, RoutedEventArgs e)
        {
            await ShowOverlayUntilComplete("Verifying Files", token =>
            {
                var dirInfo = DataContextFromEventSender<ParTreeDirectory>(sender);
                return dirInfo.VerifyFiles(ViewModel.AddLineToOutputLog, token);
            });
        }
        private async void Repair_Click(object sender, RoutedEventArgs e)
        {
            await ShowOverlayUntilComplete("Repairing Files", token =>
            {
                var dirInfo = DataContextFromEventSender<ParTreeDirectory>(sender);
                return dirInfo.RepairFiles(ViewModel.AddLineToOutputLog, token);
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
                if (element is TreeViewItem)
                {
                    ((TreeViewItem)element).IsSelected = true;
                    break;
                }
            }
        }

        private void OutpuLogClearButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.OutputLog = "";
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

        private string _outputLog = "";
        public string OutputLog
        {
            get => _outputLog;
            set
            {
                _outputLog = value;
                OnPropertyChanged();
            }
        }

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
        private string _busyMessage = "Busy";
        public string BusyMessage
        {
            get => _busyMessage;
            set
            {
                _busyMessage = value;
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

        public async Task SetWorkingDir(string? workingDirPath, CancellationToken token)
        {
            OutputLog = "";

            if (string.IsNullOrWhiteSpace(workingDirPath))
            {
                _workingDirectory = null;
            }
            else
            {
                _workingDirectory = new ParTreeDirectory(workingDirPath);
                await _workingDirectory.CheckForVerifiableFiles(token);
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

        public void AddLineToOutputLog(string line) => OutputLog += line + Environment.NewLine;
    }
}
