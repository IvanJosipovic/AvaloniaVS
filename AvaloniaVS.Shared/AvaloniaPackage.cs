using System;
using System.ComponentModel.Design;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using AvaloniaVS.Services;
using AvaloniaVS.Shared.Services;
using AvaloniaVS.Views;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Serilog;
using Serilog.Core;
using Task = System.Threading.Tasks.Task;

namespace AvaloniaVS
{
    [Guid(Constants.PackageGuidString)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideEditorFactory(typeof(EditorFactory), 113, TrustLevel = __VSEDITORTRUSTLEVEL.ETL_AlwaysTrusted)]
    [ProvideEditorLogicalView(typeof(EditorFactory), LogicalViewID.Designer)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideOptionPage(typeof(OptionsDialogPage), Constants.PackageName, "General", 113, 0, supportsAutomation: true)]
    [ProvideBindingPath]
    internal sealed class AvaloniaPackage : AsyncPackage
    {
        public static SolutionService SolutionService { get; private set; }

        protected override async Task InitializeAsync(
            CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
            await base.InitializeAsync(cancellationToken, progress);

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            InitializeLogging();
            RegisterEditorFactory(new EditorFactory(this));
            await AvaloniaCommands.InitializeAsync(this);

            var dte = (DTE)await GetServiceAsync(typeof(DTE));
            SolutionService = new SolutionService(dte);

            var shell = await GetServiceAsync(typeof(SVsShell)) as IVsShell;
            var ans = new AnalyticsService(this.GetMefService<IAvaloniaVSSettings>(), dte, shell);
            await ans.TrackLaunchAsync();           

            Log.Logger.Information("Avalonia Package initialized");
        }

        private void InitializeLogging()
        {
            const string format = "{Timestamp:HH:mm:ss.fff} [{Level}] {Pid} {Message}{NewLine}{Exception}";
            var ouput = this.GetService<IVsOutputWindow, SVsOutputWindow>();
            var settings = this.GetMefService<IAvaloniaVSSettings>();
            var levelSwitch = new LoggingLevelSwitch() { MinimumLevel = settings.MinimumLogVerbosity };

            settings.PropertyChanged += (s, e) => levelSwitch.MinimumLevel = settings.MinimumLogVerbosity;

            var sink = new OutputPaneEventSink(ouput, outputTemplate: format);
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(levelSwitch)
                .WriteTo.Sink(sink, levelSwitch: levelSwitch)
                .WriteTo.Trace(outputTemplate: format)
                .CreateLogger();
        }
    }

    namespace Services
    {
        internal static class AvaloniaCommands
        {
            private const int CommandId = 0x0100;
            private static readonly Guid CommandSet = new("9d4f5c4a-6d55-4ca2-9b7b-9c8db5c3f2e1");
            private static AsyncPackage _package;

            public static async Task InitializeAsync(AsyncPackage package)
            {
                _package = package;
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
                if (commandService == null)
                {
                    return;
                }

                var menuCommandId = new CommandID(CommandSet, CommandId);
                var menuCommand = new OleMenuCommand(Execute, menuCommandId);
                menuCommand.BeforeQueryStatus += BeforeQueryStatus;
                commandService.AddCommand(menuCommand);
            }

            private static void BeforeQueryStatus(object sender, EventArgs e)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (sender is OleMenuCommand command)
                {
                    var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                    var isPreviewableDocument = TryGetTargetFileName(dte, out _);

                    command.Visible = true;
                    command.Enabled = isPreviewableDocument;
                }
            }

            private static void Execute(object sender, EventArgs e)
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                if (!TryGetTargetFileName(dte, out var fileName))
                {
                    return;
                }

                VsShellUtilities.OpenDocumentWithSpecificEditor(
                    _package,
                    fileName,
                    AvaloniaVS.Constants.AvaloviaFactoryEditorGuid,
                    VSConstants.LOGVIEWID_Designer,
                    out IVsUIHierarchy _,
                    out uint _,
                    out IVsWindowFrame _);
            }

            private static bool TryGetTargetFileName(DTE dte, out string fileName)
            {
                fileName = string.Empty;

                if (dte == null)
                {
                    return false;
                }

                var selectedItems = dte.SelectedItems;
                if (selectedItems != null)
                {
                    foreach (SelectedItem selectedItem in selectedItems)
                    {
                        var projectItem = selectedItem.ProjectItem;
                        if (projectItem == null)
                        {
                            continue;
                        }

                        var selectedFileName = projectItem.FileNames[1];
                        if (IsCSharpFileName(selectedFileName, out fileName))
                        {
                            return true;
                        }
                    }
                }

                var activeDocument = dte.ActiveDocument;
                if (activeDocument == null)
                {
                    return false;
                }

                return IsCSharpFileName(activeDocument.FullName, out fileName);
            }

            private static bool IsCSharpFileName(string fileName, out string previewFileName)
            {
                previewFileName = fileName;

                if (string.IsNullOrWhiteSpace(fileName))
                {
                    return false;
                }

                return Path.GetExtension(fileName).Equals(".cs", StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
