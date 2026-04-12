using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AvaloniaVS.Models;
using AvaloniaVS.Services;
using AvaloniaVS.Shared.Services;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Serilog;
using VSLangProj;
using Task = System.Threading.Tasks.Task;

namespace AvaloniaVS.Views
{
    /// <summary>
    /// The Avalonia previewer control.
    /// </summary>
    internal partial class AvaloniaDesigner : UserControl, IDisposable
    {
        private static readonly DependencyPropertyKey TargetsPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(Targets),
                typeof(IReadOnlyList<DesignerRunTarget>),
                typeof(AvaloniaDesigner),
                new PropertyMetadata());

        public static readonly DependencyProperty SelectedTargetProperty =
            DependencyProperty.Register(
                nameof(SelectedTarget),
                typeof(DesignerRunTarget),
                typeof(AvaloniaDesigner),
                new PropertyMetadata(HandleSelectedTargetChanged));

        public static readonly DependencyProperty TargetsProperty =
            TargetsPropertyKey.DependencyProperty;

        public static readonly DependencyProperty ZoomLevelProperty =
            DependencyProperty.Register(
                nameof(ZoomLevel),
                typeof(string),
                typeof(AvaloniaDesigner),
                new PropertyMetadata("100%", HandleZoomLevelChanged));



        public static string[] ZoomLevels { get; } = AvaloniaVS.ZoomLevels.Levels;


        private static readonly GridLength OneStar = new GridLength(1, GridUnitType.Star);
        private readonly Throttle<string> _throttle;
        private Project _project;
        private string _currentFileName;
        private bool _loadingTargets;
        private bool _isStarted;
        private bool _isPaused;
        private SemaphoreSlim _startingProcess = new SemaphoreSlim(1, 1);
        private bool _disposed;
        private double _scaling = 1;
        private bool _buildRequired;
        private bool _firstFrame = true;
        private readonly Throttle<double> _previewResizethrottle;

        /// <summary>
        /// Initializes a new instance of the <see cref="AvaloniaDesigner"/> class.
        /// </summary>
        public AvaloniaDesigner()
        {
            InitializeComponent();

            _throttle = new Throttle<string>(TimeSpan.FromMilliseconds(300), _ => { });
            _previewResizethrottle = new(TimeSpan.FromMilliseconds(500), UpdateScaling);
            Process = new PreviewerProcess();
            Process.ErrorChanged += ErrorChanged;
            Process.FrameReceived += FrameReceived;
            Process.ProcessExited += ProcessExited;
            previewer.Process = Process;
            pausedMessage.Visibility = Visibility.Collapsed;

            Loaded += (s, e) =>
            {
                StartStopProcessAsync().FireAndForget();
            };
        }

        /// <summary>
        /// Gets or sets the paused state of the designer.
        /// </summary>
        public bool IsPaused
        {
            get => _isPaused;
            set
            {
                if (_isPaused != value)
                {
                    Log.Logger.Debug("Setting pause state to {State}", value);

                    _isPaused = value;
                    StartStopProcessAsync().FireAndForget();

                }
            }
        }

        /// <summary>
        /// Gets the previewer process used by the designer.
        /// </summary>
        public PreviewerProcess Process { get; }

        /// <summary>
        /// Gets the list of targets that the previewer can use.
        /// </summary>
        public IReadOnlyList<DesignerRunTarget> Targets
        {
            get => (IReadOnlyList<DesignerRunTarget>)GetValue(TargetsProperty);
            private set => SetValue(TargetsPropertyKey, value);
        }

        /// <summary>
        /// Gets or sets the selected target.
        /// </summary>
        public DesignerRunTarget SelectedTarget
        {
            get => (DesignerRunTarget)GetValue(SelectedTargetProperty);
            set => SetValue(SelectedTargetProperty, value);
        }

        /// <summary>
        /// Gets or sets the zoom level as a string.
        /// </summary>
        public string ZoomLevel
        {
            get => (string)GetValue(ZoomLevelProperty);
            set => SetValue(ZoomLevelProperty, value);
        }

        /// <summary>
        /// Starts the previewer.
        /// </summary>
        /// <param name="project">The project containing the preview target.</param>
        /// <param name="fileName">The active C# file used to infer the root view type.</param>
        public void Start(Project project, string fileName)
        {
            Log.Logger.Verbose("Started AvaloniaDesigner.Start()");

            if (_isStarted)
            {
                throw new InvalidOperationException("The designer has already been started.");
            }

            _project = project ?? throw new ArgumentNullException(nameof(project));
            _currentFileName = fileName;
            LoadTargetsAndStartProcessAsync().FireAndForget();

            Log.Logger.Verbose("Finished AvaloniaDesigner.Start()");
        }

        /// <summary>
        /// Disposes of the designer and all resources.
        /// </summary>
        public void Dispose()
        {
            _disposed = true;

            Process.FrameReceived -= FrameReceived;
            Process.ErrorChanged -= ErrorChanged;
            Process.ProcessExited -= ProcessExited;

            _throttle.Dispose();
            previewer.Dispose();
            Process.Dispose();
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            Process.SetScalingAsync(newDpi.DpiScaleX * _scaling).FireAndForget();
        }

        private async Task LoadTargetsAndStartProcessAsync()
        {
            Log.Logger.Verbose("Started AvaloniaDesigner.LoadTargetsAndStartProcessAsync()");

            await LoadTargetsAsync();

            if (!_disposed)
            {
                _isStarted = true;
                await StartStopProcessAsync();
            }

            Log.Logger.Verbose("Finished AvaloniaDesigner.LoadTargetsAndStartProcessAsync()");
        }

        private async Task LoadTargetsAsync()
        {
            Log.Logger.Verbose("Started AvaloniaDesigner.LoadTargetsAsync()");

            _loadingTargets = true;

            try
            {
                var projects = await AvaloniaPackage.SolutionService.GetProjectsAsync();

                bool IsValidTarget(ProjectInfo project)
                {
                    return project.IsExecutable &&
                        project.References.Contains("Avalonia.DesignerSupport") &&
                        (project.Project == _project || project.ProjectReferences.Contains(_project));
                }

                bool IsValidOutput(ProjectOutputInfo output)
                {
                    return (output.IsNetCore || output.IsNetFramework)
                        && output.RuntimeIdentifier != "browser-wasm"
                        && (output.TargetPlatformIdentifier == ""
                            || string.Equals(output.TargetPlatformIdentifier, "windows", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(output.TargetPlatformIdentifier, "macos", StringComparison.OrdinalIgnoreCase));
                }

                string GetTargetAssembly(ProjectOutputInfo output)
                {
                    var project = projects.FirstOrDefault(x => x.Project == _project);

                    // Ideally we'd have the path to the `project` assembly that `output` uses, but
                    // I'm not sure how to get that information, so instead look for a netcore output
                    // or failing that a netstandard output, and pray.
                    return project?.Outputs
                        .OrderBy(x => !x.IsNetCore)
                        .ThenBy(x => !x.IsNetStandard)
                        .FirstOrDefault()?
                        .TargetAssembly;
                }

                var oldSelectedTarget = SelectedTarget;

                Targets = (from project in projects
                           where IsValidTarget(project)
                           orderby project.Project != project, !project.IsStartupProject, project.Name
                           from output in project.Outputs
                           where IsValidOutput(output)
                           select new DesignerRunTarget
                           {
                               Name = $"{project.Name} [{output.TargetFramework}]",
                               ExecutableAssembly = output.TargetAssembly,
                               TargetAssembly = GetTargetAssembly(output),
                               RootViewTypeName = GetRootViewTypeName(),
                               HostApp = output.HostApp,
                               Project = project.Project,
                               IsNetFramework = output.IsNetFramework
                           }).ToList();

                SelectedTarget = Targets.FirstOrDefault(t => t.Name == oldSelectedTarget?.Name) ?? Targets.FirstOrDefault();
                previewer.SelectedProject = SelectedTarget?.Project;
            }
            finally
            {
                _loadingTargets = false;
            }

            Log.Logger.Verbose("Finished AvaloniaDesigner.LoadTargetsAsync()");
        }

        private async Task StartStopProcessAsync()
        {
            if (!_isStarted)
            {
                return;
            }

            // Change: keep the preview process alive even if we're in 
            // Source only mode - it prevents the error icon from showing because
            // of process exited and keeps the Error tagger active
            if (IsPaused)
            {
                pausedMessage.Visibility = Visibility.Visible;
                Process.Stop();
            }
            else if (!Process.IsRunning && IsLoaded)
            {
                pausedMessage.Visibility = Visibility.Collapsed;

                if (SelectedTarget == null)
                {
                    await LoadTargetsAsync();
                }

                await StartProcessAsync();
            }
        }

        public bool TryProcessZoomLevelValue(out double scaling)
        {
            scaling = 1;
            var zoomLevel = ZoomLevel;
            if (string.IsNullOrEmpty(zoomLevel))
                return false;

            BitmapSource bitmap = default;

            if (Process.IsReady)
            {
                bitmap = Process.Bitmap;
            }

            if (bitmap is not null && zoomLevel.StartsWith("Fit All", StringComparison.OrdinalIgnoreCase) == true)
            {
                var processScaling = Process.Scaling;
                var viewportSize = previewer.GetViewportSize(10);
                double x = viewportSize.Width / (bitmap.Width / processScaling);
                double y = viewportSize.Height / (bitmap.Height / processScaling);

                scaling = Math.Round(Math.Min(x, y), 2, MidpointRounding.ToEven);

                return true;
            }
            else if (bitmap is not null && zoomLevel.StartsWith("Fit to Width", StringComparison.OrdinalIgnoreCase) == true)
            {
                var processScaling = Process.Scaling;
                var viewportSize = previewer.GetViewportSize(10);
                double x = viewportSize.Width / (bitmap.Width / processScaling);
                //double y = viewportSize.Height / (bitmap.Height / processScaling);

                scaling = Math.Round(x, 2, MidpointRounding.ToEven);

                return true;
            }
            else if (double.TryParse(ZoomLevel.TrimEnd('%'), NumberStyles.Number, CultureInfo.InvariantCulture, out double zoomPercent)
                     && zoomPercent > 0 && zoomPercent <= 1000)
            {
                scaling = zoomPercent / 100;
                return true;
            }

            return false;
        }

        private async Task StartProcessAsync()
        {
            Log.Logger.Verbose("Started AvaloniaDesigner.StartProcessAsync()");

            ShowPreview();

            var assemblyPath = SelectedTarget?.TargetAssembly;
            var executablePath = SelectedTarget?.ExecutableAssembly;
            var hostAppPath = SelectedTarget?.HostApp;
            var isNetFx = SelectedTarget?.IsNetFramework;
            var rootViewTypeName = SelectedTarget?.RootViewTypeName ?? GetRootViewTypeName();

            if (assemblyPath != null && executablePath != null && hostAppPath != null && isNetFx != null)
            {
                try
                {
                    await _startingProcess.WaitAsync();

                    if (!IsPaused)
                    {
                        await Process.SetScalingAsync(VisualTreeHelper.GetDpi(this).DpiScaleX * _scaling);
                        await Process.StartAsync(assemblyPath, executablePath, hostAppPath, rootViewTypeName, (bool)isNetFx);
                    }
                }
                catch (ApplicationException ex)
                {
                    // Don't display an error here: ProcessExited should handle that.
                    Log.Logger.Debug(ex, "Process.StartAsync exited with error");
                }
                catch (FileNotFoundException ex)
                {
                    _buildRequired = true;
                    ShowError("Build Required", ex.Message);
                    Log.Logger.Debug(ex, "StartAsync could not find executable");
                }
                catch (Exception ex)
                {
                    ShowError("Error", ex.Message);
                    Log.Logger.Debug(ex, "StartAsync exception");
                }
                finally
                {
                    _startingProcess.Release();
                }
                _buildRequired = false;
            }
            else
            {
                Log.Logger.Error("No executable found");

                // This message is unfortunate but I can't work out how to tell when all references
                // have finished loading for all projects in the solution.
                ShowError(
                    "No Executable",
                    "Reference the library from an executable or wait for the solution to finish loading.");
            }

            Log.Logger.Verbose("Finished AvaloniaDesigner.StartProcessAsync()");
        }

        private async void ErrorChanged(object sender, EventArgs e)
        {
            if (Process.Bitmap == null || Process.Error != null)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                ShowError("Invalid Markup", "Check the Error List for more information.");
            }
            else if (Process.Error == null)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                ShowPreview();
            }
        }

        private async void FrameReceived(object sender, EventArgs e)
        {
            if (Process.Bitmap != null && Process.Error == null)
            {
                if (_firstFrame)
                {
                    _firstFrame = false;
                    if (TryProcessZoomLevelValue(out var scaling))
                    {
                        UpdateScaling(scaling);
                    }
                }
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ShowPreview();
            }
        }

        private async void ProcessExited(object sender, EventArgs e)
        {
            _firstFrame = true;
            if (!IsPaused)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                ShowError(
                    "Process Exited",
                    "The previewer process exited unexpectedly. See the output window for more information.");
            }
        }

        private void ShowError(string heading, string message)
        {
            errorIndicator.Visibility = Visibility.Visible;
            errorHeading.Text = heading;
            errorMessage.Text = message;
            if (_buildRequired == true)
            {
                previewer.buildButton.Visibility = Visibility.Visible;
            }
            else
            {
                previewer.buildButton.Visibility = Visibility.Hidden;
            }
            previewer.error.Visibility = Visibility.Visible;
            previewer.errorHeading.Text = heading;
            previewer.errorMessage.Text = message;
        }

        private void ShowPreview()
        {
            errorIndicator.Visibility = Visibility.Collapsed;
        }

        private void UpdateScaling(double scaling)
        {
            _scaling = scaling;

            if (Process.IsReady)
            {
                Process.SetScalingAsync(VisualTreeHelper.GetDpi(this).DpiScaleX * _scaling).FireAndForget();
            }
        }

        private async Task SelectedTargetChangedAsync(object sender, DependencyPropertyChangedEventArgs e)
        {
            var oldValue = (DesignerRunTarget)e.OldValue;
            var newValue = (DesignerRunTarget)e.NewValue;

            Log.Logger.Debug(
                "AvaloniaDesigner.SelectedTarget changed from {OldTarget} to {NewTarget}",
                oldValue?.ExecutableAssembly,
                newValue?.ExecutableAssembly);

            if (oldValue?.ExecutableAssembly != newValue?.ExecutableAssembly)
            {
                if (_isStarted)
                {
                    try
                    {
                        Log.Logger.Debug("Waiting for StartProcessAsync to finish");
                        await _startingProcess.WaitAsync();
                        Process.Stop();
                        StartProcessAsync().FireAndForget();
                    }
                    finally
                    {
                        _startingProcess.Release();
                    }
                }
            }
        }

        private static void HandleSelectedTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AvaloniaDesigner designer && !designer._loadingTargets)
            {
                designer.previewer.SelectedProject = (e.NewValue as DesignerRunTarget)?.Project;
                designer.SelectedTargetChangedAsync(d, e).FireAndForget();
            }
        }

        private static void HandleZoomLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AvaloniaDesigner designer && designer.TryProcessZoomLevelValue(out double scaling))
            {
                designer.UpdateScaling(scaling);
            }
        }

        private static async Task<string> ReadAllTextAsync(string fileName)
        {
            using (var reader = File.OpenText(fileName))
            {
                return await reader.ReadToEndAsync();
            }
        }

        private string GetRootViewTypeName()
        {
            if (string.IsNullOrWhiteSpace(_currentFileName) || !File.Exists(_currentFileName))
            {
                return null;
            }

            var sourceText = ReadAllTextAsync(_currentFileName).GetAwaiter().GetResult();
            var match = Regex.Match(sourceText, @"\bclass\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b");

            if (match.Success)
            {
                return match.Groups["name"].Value;
            }

            return Path.GetFileNameWithoutExtension(_currentFileName);
        }

        private IVsBuildPropertyStorage GetMSBuildPropertyStorage(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            IVsSolution solution = (IVsSolution)ServiceProvider.GlobalProvider.GetService(typeof(SVsSolution));

            int hr = solution.GetProjectOfUniqueName(project.FullName, out var hierarchy);
            System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(hr);

            return hierarchy as IVsBuildPropertyStorage;
        }

        private string GetMSBuildProperty(string key, IVsBuildPropertyStorage storage)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            int hr = storage.GetPropertyValue(key, null, (uint)_PersistStorageType.PST_USER_FILE, out var value);
            int E_XML_ATTRIBUTE_NOT_FOUND = unchecked((int)0x8004C738);

            // ignore this HR, it means that there's no value for this key
            if (hr != E_XML_ATTRIBUTE_NOT_FOUND)
            {
                System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(hr);
            }

            return value;
        }

        private void Preview_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (TryProcessZoomLevelValue(out double scaling))
            {
                _previewResizethrottle.Queue(scaling);
            }
        }
    }
}
