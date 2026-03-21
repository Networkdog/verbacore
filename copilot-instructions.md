# VerbaCore — Copilot Instructions

## Project Overview
VerbaCore is a lightweight Windows desktop AI dictionary & translation app inspired by [Enso Launcher](https://github.com/tartakynov/enso). It runs as a system tray application with no main window. Hold CapsLock, type a word, release CapsLock — an AI-powered result appears on a transparent overlay.

## Tech Stack
- **Language**: C# 12 / .NET 8
- **UI Framework**: WPF + WPF-UI (Wpf.Ui 3.x) for settings window; raw WPF for transparent overlay
- **Architecture**: MVVM (CommunityToolkit.Mvvm) for settings; direct code-behind for overlay
- **DI**: Microsoft.Extensions.DependencyInjection
- **AI**: OpenAI / Azure OpenAI via HttpClient (no SDK — lightweight)
- **Input**: CapsLock quasimodal keyboard hook (low-level, SetWindowsHookEx)
- **Speech**: System.Speech.Recognition
- **Tray**: System.Windows.Forms.NotifyIcon
- **Settings**: JSON file with DPAPI encryption for API keys

## UX Pattern (Enso-style)
1. **CapsLock down** → transparent overlay appears at screen center
2. **Type characters** → input shown in large font on overlay in real-time
3. **Tab** → cycle through modes (Dictionary / Translate / Analyze)
4. **Backspace** → delete character
5. **Escape** → cancel and dismiss
6. **CapsLock up** → trigger AI lookup, show streaming result on overlay
7. **Auto-hide** → overlay fades out after 15 seconds
8. **System tray** → right-click for settings/about/exit

## Project Structure
```
src/VerbaCore/
├── App.xaml(.cs)              — Tray icon, DI, CapsLock hook setup, no main window
├── GlobalUsings.cs            — Resolve WPF vs WinForms namespace ambiguities
├── OverlayWindow.xaml(.cs)    — Transparent fullscreen overlay (Enso-style)
├── SettingsWindow.xaml(.cs)   — FluentWindow with Mica, settings + history tabs
├── MainWindow.xaml(.cs)       — [Legacy] Original tabbed window (unused)
├── Models/                    — AppSettings, LookupResult, LookupHistory
├── ViewModels/                — SettingsViewModel, HistoryViewModel
├── Views/                     — SettingsView, HistoryView, MainView (UserControls)
├── Services/
│   ├── CapsLockService.cs     — Low-level keyboard hook, CapsLock interception
│   ├── OpenAiService.cs       — OpenAI / Azure OpenAI streaming API
│   ├── PromptBuilder.cs       — Mode-specific prompt templates
│   ├── SettingsService.cs     — JSON settings with DPAPI encryption
│   ├── HistoryService.cs      — JSON history storage
│   ├── ClipboardMonitorService.cs — Win32 clipboard monitoring
│   ├── SpeechInputService.cs  — System.Speech voice input
│   └── CursorTextService.cs   — UI Automation cursor text extraction
└── Helpers/
    ├── NativeMethods.cs       — Win32 P/Invoke (keyboard hook, clipboard, etc.)
    └── Converters.cs          — XAML value converters
```

## Coding Conventions
- Use `file-scoped namespaces`
- Use `primary constructors` where appropriate
- Use `CommunityToolkit.Mvvm` attributes: `[ObservableProperty]`, `[RelayCommand]`
- Use `record` types for immutable DTOs
- Use `System.Text.Json` for serialization (not Newtonsoft)
- All async methods should accept `CancellationToken`
- P/Invoke should use `DllImport` (not LibraryImport, to avoid AllowUnsafeBlocks)
- Settings file location: `%AppData%\VerbaCore\settings.json`
- API keys encrypted with `ProtectedData` (DPAPI, CurrentUser scope)
- History stored in `%AppData%\VerbaCore\history.json`

## Key Patterns
1. **CapsLock quasimodal input**: Low-level keyboard hook (`SetWindowsHookEx` WH_KEYBOARD_LL) intercepts CapsLock. While held, all keystrokes are captured into a buffer. CapsLock toggle is suppressed.
2. **OpenAI calls**: HttpClient with SSE streaming for real-time response. Supports both OpenAI and Azure OpenAI via `ApiProvider` enum.
3. **Azure OpenAI**: Uses `api-key` header auth and `/openai/deployments/{name}/chat/completions?api-version=` URL format.
4. **Overlay window**: Borderless, transparent `Window` with `AllowsTransparency="True"`. Fade in/out animations. Shows input in large text, results in smaller text below.
5. **System tray**: `System.Windows.Forms.NotifyIcon` with context menu. App uses `ShutdownMode="OnExplicitShutdown"`.
6. **Clipboard monitoring**: Win32 `AddClipboardFormatListener` + `WM_CLIPBOARDUPDATE`
7. **Voice input**: `System.Speech.Recognition.SpeechRecognitionEngine` with dictation grammar

## Build & Run
```bash
cd src/VerbaCore
dotnet build
dotnet run
```

## Important Notes
- Target framework: `net8.0-windows7.0` (Windows-specific APIs)
- `UseWindowsForms=true` in csproj for NotifyIcon; `GlobalUsings.cs` resolves WPF/WinForms ambiguities
- SettingsWindow uses FluentWindow with `WindowBackdropType="Mica"` for semi-transparent look
- OverlayWindow uses raw WPF transparency (no WPF-UI) for maximum control
- Dark theme is default
- `ShutdownMode="OnExplicitShutdown"` — app stays in tray, explicit exit via tray menu
- No SQLite dependency — history is JSON-based for minimal footprint
- No OCR dependency — cursor text extraction uses UI Automation only
- No SQLite dependency — history is JSON-based for minimal footprint
- No OCR dependency — cursor text extraction uses UI Automation only
