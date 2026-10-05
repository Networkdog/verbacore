# Clipboard-Free Selected Text

Research and local validation: 2026-10-05.

## Support Boundary

Selection can be read without the clipboard when the source application exposes it through an accessibility or document API. There is no universal Windows API that can recover a selection that the application does not expose. VerbaCore does not send Ctrl+C, read or write clipboard contents, launch Office applications, or change the source application's settings while capturing text.

| Application / surface | Read path | Validation |
| --- | --- | --- |
| Native/WPF text controls | COM UI Automation TextPattern.GetSelection | Real WPF control: multiple paragraphs and blank lines, partial selection, focus transfer, no selection, password exclusion, Unicode limit |
| Word desktop | Captured `_WwG` window -> OBJID_NATIVEOM -> Window.Selection | Real Word: multiple paragraphs and blank lines, exact substring and caret-only selection, including after popup focus |
| Excel desktop cells | Captured `EXCEL7` window -> native object model -> selected range areas/cells | Real Excel: 2x2 rectangle as TSV, empty cell, popup focus; active Excel window must match the captured HWND |
| PowerPoint desktop text editing | Captured `paneClassDC` -> DocumentWindow.Selection.TextRange | Object-contract test for text vs shape selection; actual PowerPoint UI not exercised |
| Classic Outlook Word editor | Embedded `_WwG` native object model, then accessibility fallback | Implemented but not exercised against a real Outlook message or compose window |
| VS Code editor | UIA plus MSAA/IAccessible2; reject hypertext object placeholders | Real isolated VS Code: exact selection, popup focus transfer, collapsed selection, clipboard unchanged; requires the setting below in the tested version |
| Edge HTML selection | Document-level UIA selection before paragraph-level fallbacks | Real isolated Edge: all three selected paragraphs, inline formatting and empty paragraphs; unselected prefix/suffix excluded, clipboard unchanged |
| New Outlook, web mail, other Electron/WebView2 apps | Accessibility providers only | Provider/version dependent; not individually verified |

An Excel cell range is a selection even when it contains only one cell. Returned cell display text is separated by tabs and newlines. Shape/slide selections in PowerPoint are not treated as highlighted text. Excel in-cell/formula editing falls back to accessibility rather than knowingly substituting the complete cell value for a partial text selection.

## VS Code Setting

Set **Editor: Accessibility Support** to **On** in VS Code, or use this setting:

```json
"editor.accessibilitySupport": "on"
```

The `auto` default did not expose the selected editor characters reliably in the tested build, despite successful UIA and IAccessible2 interface discovery. Explicit `on` passed, including when a WPF popup took focus before the queued selection read ran. VerbaCore does not modify this preference. Screen reader mode can change VS Code behavior such as minimap/folding; see the official accessibility documentation.

The installed VS Code refused a separate instance while its updater mutex was held. Testing therefore used Microsoft's signed Windows ZIP distribution in a temporary directory, with a separate user-data directory and a development-only fixture extension that selects fixed test text. The fixture never becomes a VerbaCore runtime dependency and is not installed into the user's extension directory. This is not certification of every VS Code version, terminal selection, notebook, or extension webview.

## Implementation

- Capture the source root HWND, process ID, native focus HWND, and caret HWND before showing/activating the overlay. Do not use a process-wide active Office object or accept the overlay's later UIA focus as the source.
- Keep all selection COM/UIA work on the dedicated MTA worker. The keyboard hook only queues dispatcher work; cross-process accessibility calls never run in the hook.
- Read Office selection from the documented native document-window classes. Prefer the native renderer window for Chromium accessibility. Within the captured window, inspect outer UIA ancestors before their paragraph/text descendants and try document-level UIA selection before MSAA/IAccessible2. A first non-empty paragraph fragment must not take precedence over an available complete document selection. Keep the fallback when the UIA node budget is exhausted.
- UIA uses non-degenerate selection ranges, not DocumentRange or ValuePattern. IAccessible2 uses explicit selection offsets, not accValue/accName. Its U+FFFC embedded-object markers are not returned as user text.
- Return at most 2,000 UTF-16 code units, without splitting a surrogate pair. Bound ranges, nodes, and Excel cells/areas; skip password controls. Do not interpret failure or no selection as permission to grab a document, title, or mail body.
- The public selection request has a 2-second deadline. New requests supersede older requests; hiding/shutting down the overlay cancels its request. User typing and existing gesture/session guards still take precedence over late results.
- A deadline cannot forcibly interrupt an in-progress third-party COM call. The caller stops waiting and the worker checks cancellation between calls. A permanently hung provider can still delay later reads on that worker; the UI and keyboard hook remain independent. Disposal does not block the UI or dispose a queue still in use.

Elevated/protected applications, secure desktops, protected documents, unavailable accessibility providers, and unsupported custom/canvas controls can still return no text. No elevation, clipboard fallback, OCR, source document changes, or persistent accessibility-setting changes are attempted.

### Multiple Paragraphs

An Outlook report of first-paragraph-only input was reproduced in an isolated Edge HTML editor: a three-paragraph selection returned only the first 25 characters. Prioritizing document-level UIA selection returned all three paragraphs in order. Blank lines are not delimiters that stop the lookup. Word and WPF tests preserve explicit blank lines exactly; the tested Edge provider collapses empty HTML paragraphs into single line separators, so exact visual whitespace depends on the source provider. Outlook itself has not been exercised against the reported message. The 2,000-code-unit limit is unchanged.

## Regression Checks

Run on an unlocked Windows desktop:

```powershell
dotnet run --project tests/VerbaCore.PopupTests/VerbaCore.PopupTests.csproj -c Release -- --selection
dotnet run --project tests/VerbaCore.PopupTests/VerbaCore.PopupTests.csproj -c Release -- --selection-office
dotnet run --project tests/VerbaCore.PopupTests/VerbaCore.PopupTests.csproj -c Release -- --selection-browser
dotnet run --project tests/VerbaCore.PopupTests/VerbaCore.PopupTests.csproj -c Release -- --selection-vscode
```

`--selection-office` requires installed Word/Excel and refuses to run while either is already running. It creates and closes only unsaved test documents. It does not touch existing PowerPoint presentations or Outlook items. `--selection-vscode` defaults to `accessibilitySupport: on` in its temporary profile; `VERBACORE_TEST_VSCODE_PATH` can select a separate ZIP executable, and `VERBACORE_TEST_VSCODE_ACCESSIBILITY=auto` explicitly runs the diagnostic comparison that may fail on unsupported builds. Temporary profiles and test text remain under the system temporary directory for diagnosis.

The general selection test also covers cancellation behind a blocked worker, supersession, deadline completion, disposal during a pending read, and clean worker exit. `--selection-browser` requires Edge and creates its own temporary profile and local HTML fixture; it does not change an existing browser session. Office/browser/VS Code checks compare clipboard sequence numbers without reading clipboard data. No AI endpoint is called by these tests.

Earlier local validation required a cached .NET 8 targeting-pack workaround. Ordinary builds now succeed without those overrides, and the multi-paragraph regressions use the normal Release build. Project target frameworks and NuGet versions are unchanged. Installer publishing and physical CapsLock/IME behavior across every Office version are separate checks.

## Primary Sources

- [AccessibleObjectFromWindow](https://learn.microsoft.com/en-us/windows/win32/api/oleacc/nf-oleacc-accessibleobjectfromwindow): OBJID_NATIVEOM, exact Office document window classes, and returned native objects.
- [UI Automation threading](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading): dedicated non-window-owning MTA client thread.
- [Windows SDK UIAutomationClient.h](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/UIAutomationClient.h): verified native TextPattern IID and vtable. The existing GetSelection slot was correct; it was not the root cause.
- [IAccessible2 IAccessibleText IDL](https://github.com/LinuxA11y/IAccessible2/blob/master/api/AccessibleText.idl): selection counts, offsets, and text retrieval signatures.
- [Chromium QueryService implementation](https://github.com/chromium/chromium/blob/main/ui/accessibility/platform/ax_platform_node_win.cc): IAccessible/IAccessible2 service discovery and API-use detection.
- [Electron accessibility](https://www.electronjs.org/docs/latest/tutorial/accessibility): lazy accessibility activation and application-side accessibility configuration.
- [VS Code accessibility](https://code.visualstudio.com/docs/configure/accessibility/accessibility#_screen-reader-mode): auto/on/off behavior and screen reader mode side effects.

The older Chromium accessibility design page describes the OBJID 1 detection handshake, but its historical claims about limited UIA support must not be generalized to current Chromium. Successful interface discovery alone does not prove that an editor exposes its actual selected characters.