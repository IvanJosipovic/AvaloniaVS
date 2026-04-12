using System;
using System.IO;
using System.Runtime.InteropServices;
using AvaloniaVS.Shared.Views;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Serilog;

namespace AvaloniaVS.Services
{
    /// <summary>
    /// Implements <see cref="IVsEditorFactory"/> to create preview panes for Avalonia code views.
    /// </summary>
    [Guid(AvaloniaVS.Constants.AvaloviaFactoryEditorGuidString)]
    internal sealed class EditorFactory : IVsEditorFactory, IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EditorFactory"/> class.
        /// </summary>
        /// <param name="package">The package that the factory belongs to.</param>
        public EditorFactory(AvaloniaPackage package)
        {
        }

        /// <inheritdoc/>
        public int SetSite(Microsoft.VisualStudio.OLE.Interop.IServiceProvider psp)
        {
            return VSConstants.S_OK;
        }

        /// <inheritdoc/>
        public int MapLogicalView(ref Guid rguidLogicalView, out string pbstrPhysicalView)
        {
            pbstrPhysicalView = null;

            if (rguidLogicalView == VSConstants.LOGVIEWID_Primary ||
                rguidLogicalView == VSConstants.LOGVIEWID_Debugging ||
                rguidLogicalView == VSConstants.LOGVIEWID_Designer)
            {
                return VSConstants.S_OK;
            }

            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc/>
        public int CreateEditorInstance(
            uint grfCreateDoc,
            string pszMkDocument,
            string pszPhysicalView,
            IVsHierarchy pvHier,
            uint itemid,
            IntPtr punkDocDataExisting,
            out IntPtr ppunkDocView,
            out IntPtr ppunkDocData,
            out string pbstrEditorCaption,
            out Guid pguidCmdUI,
            out int pgrfCDW)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            Log.Logger.Verbose("Started EditorFactory.CreateEditorInstance({Filename})", pszMkDocument);

            ppunkDocView = IntPtr.Zero;
            ppunkDocData = IntPtr.Zero;
            pguidCmdUI = Constants.AvaloviaFactoryEditorGuid;
            pgrfCDW = 0;
            pbstrEditorCaption = Path.GetFileName(pszMkDocument);

            if ((grfCreateDoc & (VSConstants.CEF_OPENFILE | VSConstants.CEF_SILENT)) == 0)
            {
                return VSConstants.E_INVALIDARG;
            }

            var pane = new EditorPane(GetProject(pvHier), pszMkDocument);
            ppunkDocData = Marshal.GetIUnknownForObject(pane);
            ppunkDocView = Marshal.GetIUnknownForObject(pane);

            Log.Logger.Verbose("Finished EditorFactory.CreateEditorInstance({Filename})", pszMkDocument);
            return VSConstants.S_OK;
        }

        /// <inheritdoc/>
        public int Close() => VSConstants.S_OK;

        /// <inheritdoc/>
        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
        }

        private static Project GetProject(IVsHierarchy hierarchy)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            ErrorHandler.ThrowOnFailure(hierarchy.GetProperty(
                VSConstants.VSITEMID_ROOT,
                (int)__VSHPROPID.VSHPROPID_ExtObject,
                out var objProj));
            return objProj as Project;
        }
    }
}
