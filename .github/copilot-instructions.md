# VerbaCore — Copilot Instructions

## Project Overview
VerbaCore is a lightweight Windows desktop AI dictionary & translation app. It lives in the system tray with no main window. Hold CapsLock, type a word, release — AI results stream onto a transparent overlay.

## Tech Stack
- **Language**: C# 12 / .NET 8 (`net8.0-windows7.0`)
- **UI**: WPF + WPF-UI 3.x (SettingsPanel) / raw WPF (Overlay)
- **Architecture**: MVVM (CommunityToolkit.Mvvm) — settings; code-behind — overlay
- **DI**: Microsoft.Extensions.DependencyInjection
- **AI**: HttpClient SSE streaming (6 providers: OpenAI, AzureOpenAI, Anthropic, Google, OpenRouter, Custom)
- **Input**: CapsLock quasimodal keyboard hook (WH_KEYBOARD_LL)
- **Tray**: System.Windows.Forms.NotifyIcon
- **Settings**: JSON + DPAPI encryption (`%AppData%\VerbaCore\settings.json`)
- **History**: JSON (`%AppData%\VerbaCore\history.json`, max 200 items)
- **Installer**: Inno Setup (`installer.iss`)

## Project Structure
```
src/VerbaCore/
├── App.xaml(.cs)              — DI, tray icon, CapsLock hook setup
├── GlobalUsings.cs            — WPF/WinForms namespace conflict resolution
├── OverlayWindow.xaml(.cs)    — Transparent fullscreen overlay (input & results)
├── SettingsWindow.xaml(.cs)   — FluentWindow(Mica) settings + history
├── Models/
│   ├── AppSettings.cs         — Settings model + Enums (ApiProvider, OverlayPosition, OverlaySize, ThemeMode)
│   ├── AppJsonContext.cs      — System.Text.Json source generation contexts
│   ├── LookupResult.cs        — Lookup result + LookupMode enum (Dictionary, Translate, Assist)
│   └── LookupHistory.cs       — History item model
├── ViewModels/                — SettingsViewModel, HistoryViewModel
├── Views/                     — SettingsView, HistoryView (UserControls)
├── Services/
│   ├── CapsLockService.cs     — Low-level keyboard hook, EnsoHold/QuickTap detection
│   ├── OpenAiService.cs       — 6-provider SSE streaming + Utf8JsonReader parsing
│   ├── PromptBuilder.cs       — Mode-specific prompt generation + AutoMode selection
│   ├── SettingsService.cs     — JSON settings load/save + DPAPI (source-generated)
│   ├── HistoryService.cs      — JSON history + debounced save (source-generated)
│   ├── HotkeyService.cs       — NHotkey global hotkey registration/unregistration
│   └── CursorTextService.cs   — COM UIA3 selected text extraction
└── Helpers/
    ├── NativeMethods.cs       — Win32 P/Invoke + CachedModuleHandle
    ├── UIA3Interop.cs         — COM UIA3 interface definitions
    └── Converters.cs          — XAML value converters
```

## Coding Conventions
- Use `file-scoped namespaces`
- Use `primary constructors` where appropriate
- `CommunityToolkit.Mvvm` attributes: `[ObservableProperty]`, `[RelayCommand]`
- `System.Text.Json` serialization — **always use source generation contexts** (`SettingsJsonContext`, `HistoryJsonContext`, `ApiJsonContext`)
- All async methods must accept `CancellationToken`
- P/Invoke uses `DllImport` (not LibraryImport — avoids AllowUnsafeBlocks)
- When adding new JSON DTOs, register with `[JsonSerializable]` on an existing `JsonSerializerContext` or create a new one

## Performance Patterns
- **JSON source generation**: All Settings/History/API DTOs use `JsonSerializerContext` — eliminates reflection
- **Streaming FlowDocument reuse**: `_streamingRun`/`_streamingDoc` caching prevents new WPF object creation every 200ms
- **SSE Utf8JsonReader**: Zero-alloc `Utf8JsonReader` instead of `JsonDocument` for streaming JSON parsing
- **Cursor animation GPU acceleration**: WPF Storyboard on composition thread instead of DispatcherTimer
- **Module handle caching**: `NativeMethods.CachedModuleHandle` avoids Process allocation on every hook install
- **History debounced save**: 500ms debounce on consecutive lookups to minimize I/O
- **ListBox virtualization**: `VirtualizingPanel.VirtualizationMode="Recycling"` enabled
- **PublishReadyToRun**: AOT precompilation on publish builds

## Key Architecture Decisions
1. **CapsLock quasimodal**: `SetWindowsHookEx` WH_KEYBOARD_LL intercepts CapsLock. EnsoHold(≥0.5s) vs QuickTap(<0.5s) distinction
2. **6-provider SSE**: HttpClient + `ResponseHeadersRead` + `StreamReader` → `Utf8JsonReader` chunk parsing
3. **3 Lookup Modes**: Dictionary(≤3 words), Translate(>3 words), Assist(code/URL/formula/non-language) — `PromptBuilder.AutoSelectMode()` auto-selects
4. **Overlay**: Transparent `Window` + `AllowsTransparency="True"`. 220ms fade in / 180ms fade out. Global mouse hook for outside-click detection
5. **System tray**: `NotifyIcon` + `ShutdownMode="OnExplicitShutdown"`. Only tray exit terminates the app

## Build & Run
```bash
cd src/VerbaCore
dotnet build
dotnet run
```

## Documentation Sync Rules
When code changes, update these documents accordingly:
- **This file** (`copilot-instructions.md`): Project Structure, Tech Stack, Coding Conventions, Performance Patterns sections
- **`README.md`**: Features, Project Structure, Tech Stack sections
- **`.github/skills/glossary/SKILL.md`**: Element Glossary (when adding/removing/renaming elements)

Specifically:
1. New file/service added → update Project Structure
2. New NuGet package → update Tech Stack
3. New performance optimization → update Performance Patterns
4. New UI element/shortcut/mode → update SKILL.md Glossary
5. Feature added/removed → update README.md Features

## Important Notes
- `UseWindowsForms=true` — for NotifyIcon; `GlobalUsings.cs` resolves WPF/WinForms conflicts
- OverlayWindow uses raw WPF transparency (not WPF-UI)
- `ShutdownMode="OnExplicitShutdown"` — only exits via tray menu
- When debugging, kill any existing `VerbaCore.exe` process first to avoid keyboard hook conflicts
