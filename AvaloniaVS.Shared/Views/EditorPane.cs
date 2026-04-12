using System;
using System.Runtime.InteropServices;
using AvaloniaVS.Services;
using AvaloniaVS.Views;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Serilog;

namespace AvaloniaVS.Shared.Views
{
    [ComVisible(true)]
    internal class EditorPane : WindowPane, IVsDeferredDocView
    {
        private readonly Project _project;
        private readonly string _fileName;
        private AvaloniaDesigner _content;
        private DTEEvents _dteEvents;
        private BuildEvents _buildEvents;
        private bool _isPaused;

        public EditorPane(Project project, string fileName)
        {
            _project = project ?? throw new ArgumentNullException(nameof(project));
            _fileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            _content = new AvaloniaDesigner();
        }

        public override object Content => _content;

        protected override void Initialize()
        {
            base.Initialize();
            InitializeEditorPane();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                UnsubscribeBuildEvents();
                _content?.Dispose();
                _content = null;
            }
        }

        private void InitializeEditorPane()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var dte = (DTE)Package.GetGlobalService(typeof(DTE));
            _buildEvents = dte.Events.BuildEvents;
            _dteEvents = dte.Events.DTEEvents;
            _isPaused = dte.Mode == vsIDEMode.vsIDEModeDebug;

            _buildEvents.OnBuildBegin += HandleBuildBegin;
            _buildEvents.OnBuildDone += HandleBuildDone;
            _dteEvents.ModeChanged += HandleModeChanged;

            var settings = this.GetMefService<IAvaloniaVSSettings>();
            var previewerView = _content;
            previewerView.IsPaused = _isPaused;
            previewerView.ZoomLevel = settings.ZoomLevel;
            previewerView.Start(_project, _fileName);
        }

        private void UnsubscribeBuildEvents()
        {
            if (_buildEvents != null)
            {
                _buildEvents.OnBuildBegin -= HandleBuildBegin;
                _buildEvents.OnBuildDone -= HandleBuildDone;
            }

            if (_dteEvents != null)
            {
                _dteEvents.ModeChanged -= HandleModeChanged;
            }
        }

        private void HandleModeChanged(vsIDEMode lastMode)
        {
            if (_content != null)
            {
                _content.IsPaused = _isPaused = lastMode == vsIDEMode.vsIDEModeDesign;
            }
        }

        private void HandleBuildBegin(vsBuildScope scope, vsBuildAction action)
        {
            Log.Logger.Debug("Build started");

            _isPaused = true;
            if (_content != null)
            {
                _content.IsPaused = _isPaused;
            }
        }

        private void HandleBuildDone(vsBuildScope scope, vsBuildAction action)
        {
            Log.Logger.Debug("Build finished");

            _isPaused = false;
            if (_content != null)
            {
                _content.IsPaused = _isPaused;
            }
        }

        int IVsDeferredDocView.get_CmdUIGuid(out Guid pGuidCmdId)
        {
            pGuidCmdId = AvaloniaVS.Constants.AvaloviaFactoryEditorGuid;
            return VSConstants.S_OK;
        }

        int IVsDeferredDocView.get_DocView(out IntPtr ppUnkDocView)
        {
            ppUnkDocView = Marshal.GetIUnknownForObject(this);
            return VSConstants.S_OK;
        }
    }
}
