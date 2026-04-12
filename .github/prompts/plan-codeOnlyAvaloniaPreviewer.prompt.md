## Plan: Code-only Avalonia Previewer

Replace the current XAML designer/completion experience with a previewer focused on compiled C# views from Avalonia.Markup.Declarative. The extension today is built around `.axaml`/`.xaml` text buffers, XAML completion, and XAML-specific diagnostics; the new shape should remove that surface and keep only the preview path that instantiates real view types from compiled output.

**Steps**
1. Remove XAML editor registration and routing from the package and editor factory. This means deleting the XAML-specific extension attributes and the document-handling logic that routes `.axaml`, `.xaml`, and `.paml` into the designer experience.
2. Refactor the preview host so it no longer depends on `XamlBufferMetadata`, XML content types, or nested code-behind file discovery. The preview shell should instead resolve a target view from the code project and open a preview-only surface.
3. Replace the current XAML transport contract with a code-view activation contract. The previewer process should instantiate the compiled `ViewBase` or `ViewBase<T>` control tree, and keep DI/service-provider support so constructor injection still works.
4. Delete the XAML-only IntelliSense stack and tests. That includes completion, paste handling, text-manipulator registration, error tagging, and the standalone completion-engine project if nothing else consumes it.
5. Simplify the UI to preview-only behavior. Keep frame rendering, scaling, resize handling, and reload/pause behavior, but remove split source/design modes and any source-editor-specific labels or state.
6. Rename metadata and package descriptions so the extension no longer presents itself as a XAML editor. Adjust target naming and any preview configuration to reflect code-based Avalonia views instead of XAML assemblies.
7. Validate the new flow against the AvaloniaDeclerative sample app by confirming the previewer can render both `ViewBase` and `ViewBase<T>` roots, including a view that uses DI or explicit state.

**Relevant files**
- `c:\repos\AvaloniaVS\AvaloniaVS.Shared\AvaloniaPackage.cs` - remove XAML editor registrations and re-scope the package.
- `c:\repos\AvaloniaVS\AvaloniaVS.Shared\Services\EditorFactory.cs` - remove XAML document routing and code-behind lookup.
- `c:\repos\AvaloniaVS\AvaloniaVS.Shared\Views\AvaloniaDesigner.xaml.cs` - convert the designer shell into a preview-only host.
- `c:\repos\AvaloniaVS\AvaloniaVS.Shared\Services\PreviewerProcess.cs` - replace XAML payload updates with code-view activation.
- `c:\repos\AvaloniaVS\AvaloniaVS.Shared\Views\TextEditorHost.cs` - remove XML language service setup and XAML metadata creation.
- `c:\repos\AvaloniaVS\AvaloniaVS.Shared\IntelliSense\*` - delete XAML completion, tagger, and command-handler components.
- `c:\repos\AvaloniaVS\AvaloniaVS.Shared\Models\XamlBufferMetadata.cs` - remove if no consumers remain.
- `c:\repos\AvaloniaVS\CompletionEngine\*` - remove if only used by XAML completion.
- `c:\repos\AvaloniaVS\tests\CompletionEngineTests\*` - remove XAML-only tests.

**Verification**
1. Build the solution after each major cut to confirm the preview path still compiles cleanly without XAML editor dependencies.
2. Search for `.axaml`, `.xaml`, `XamlBufferMetadata`, `XamlCompletion`, `UpdateXaml`, and `ContentType("xml")` to confirm the XAML-specific path is fully removed.
3. Test against the declarative sample app and verify the previewer can resolve and render a root view automatically from the active C# file or project convention.
4. Confirm the previewer still renders frames, resizes correctly, and reloads after rebuilds.

**Decisions**
- Scope is preview-only for code-based declarative views; XAML editing, XAML IntelliSense, and XAML diagnostics are out of scope.
- Preview should instantiate the real compiled view type rather than synthesizing a preview from source text.
- Based on your answer, the previewer should auto-detect the root view and should also support plain `UserControl` / `Control` previews in addition to `ViewBase` types.

Example app is located at C:\repos\AvaloniaDeclerative
