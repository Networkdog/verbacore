# VerbaCore — Copilot Instructions

## Project Overview
VerbaCore is a lightweight Windows desktop AI dictionary & translation app. It lives in the system tray with no main window. Hold CapsLock, type a word, release — AI results stream onto a transparent overlay.

## Tech Stack
- **Language**: C# 12 / .NET 8 (`net8.0-windows7.0`)
- **UI**: WPF + WPF-UI 3.x (SettingsPanel) / raw WPF (Overlay)
- **Architecture**: MVVM (CommunityToolkit.Mvvm) — settings; code-behind — overlay
- **DI**: Microsoft.Extensions.DependencyInjection
- **AI**: HttpClient SSE streaming (6 providers: OpenAI, AzureOpenAI, Anthropic, Google, OpenRouter, Custom) — Model field doubles as Azure Deployment Name
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
│   ├── AppSettings.cs         — Settings model + Enums (ApiProvider, OverlayPosition, OverlaySize, ThemeMode, UiLanguage)
│   ├── AppJsonContext.cs      — System.Text.Json source generation contexts
│   ├── LookupResult.cs        — Lookup result + LookupMode enum (Dictionary, Translate, Assist)
│   └── LookupHistory.cs       — History item model
├── ViewModels/                — SettingsViewModel, HistoryViewModel
├── Views/                     — SettingsView, HistoryView (UserControls)
├── Resources/
│   ├── Strings.ko.xaml        — Korean UI string resources
│   └── Strings.en.xaml        — English UI string resources
├── Services/
│   ├── CapsLockService.cs     — Keyboard/mouse hooks on a dedicated message-pump thread, EnsoHold/QuickTap detection
│   ├── OpenAiService.cs       — 6-provider SSE streaming + Utf8JsonReader parsing
│   ├── PromptBuilder.cs       — Mode-specific prompt generation + AutoMode selection
│   ├── SettingsService.cs     — JSON settings load/save + DPAPI (source-generated)
│   ├── HistoryService.cs      — JSON history + debounced save (source-generated)
│   ├── HotkeyService.cs       — NHotkey global hotkey registration/unregistration
│   ├── LocalizationService.cs — Runtime UI language switching via ResourceDictionary swap
    └── CursorTextService.cs   — COM UIA3 selected text extraction (+ startup PreWarm)
└── Helpers/
    ├── NativeMethods.cs       — Win32 P/Invoke + CachedModuleHandle
    ├── UIA3Interop.cs         — COM UIA3 interface definitions
    └── Converters.cs          — XAML value converters

tests/VerbaCore.PopupTests/
├── VerbaCore.PopupTests.csproj # Standalone Windows popup regression harness
└── Program.cs                 # Isolated foreground, idle, and working-set tests
```

## Coding Conventions
- Use `file-scoped namespaces`
- Use `primary constructors` for classes whose only constructor sets readonly fields via DI injection (e.g., services). Do not use them when the constructor body contains any logic beyond field assignment — e.g. event subscriptions, method calls, validation, or conditional branching
- `CommunityToolkit.Mvvm` attributes: `[ObservableProperty]`, `[RelayCommand]`
- `System.Text.Json` serialization — **always use source generation contexts** (`SettingsJsonContext`, `HistoryJsonContext`, `ApiJsonContext`)
- All async methods must accept `CancellationToken`
- P/Invoke uses `DllImport` (not LibraryImport — avoids AllowUnsafeBlocks)
- When adding new JSON DTOs, register with `[JsonSerializable]` on an existing `JsonSerializerContext` or create a new one

## Performance Patterns
- **Dedicated hook thread**: `WH_KEYBOARD_LL` is installed on its own STA thread with a private `GetMessage` pump. Windows delivers the callback on the installing thread and lets the key through unhooked if it doesn't return within `LowLevelHooksTimeout` (300ms) — the UI thread is too easily blocked to host it. Keeping the callback off the UI thread is what makes CapsLock suppression reliable (no caps-mode toggling) even while the overlay/UIA initialize on first use
- **Mouse hook isolation**: `WH_MOUSE_LL` uses the same dedicated input thread, never the WPF dispatcher. Movement immediately passes through; only button-down coordinates are posted asynchronously for outside-click checks. Monitoring installation/removal is posted to that thread, so UI rendering cannot stall system-wide mouse delivery
- **Native hold-mode input**: both hold and quick-tap use the existing IME TextBox. While it owns keyboard focus and the overlay is foreground, the hook forwards text, Alt, and Hangul keys to Windows; the old character buffer is only a pre-focus fallback. Hold release drains queued input before reading the TextBox. Escape stays latched until CapsLock-up and raises cancellation, not lookup. Gesture IDs reject stale releases, focus callbacks, and outside clicks
- **Non-blocking hook callbacks**: every `CapsLockService` event handler marshals with `Dispatcher.BeginInvoke`, never `Invoke`. Blocking inside the callback is what makes CapsLock fall through to plain case-toggling
- **Hook re-arm watchdog**: the hook is reinstalled every 45s (skipped mid-keystroke), recovering from OS-dropped hooks and keeping the callback path resident in the working set
- **Async UIA text grab**: `CursorTextService` runs all UIA3 calls on a dedicated STA worker; the overlay shows immediately and fills in the selection when it lands (800ms budget, stale requests dropped)
- **Idle popup readiness**: `OverlayWindow.PreWarm()` realizes the HWND and lays out both input modes without activation. A 30s `ContextIdle` timer maintains the layout only when the overlay is fully transparent and not in use; it performs no bitmap rendering, UIA calls, or foreground activation. `CloseForShutdown()` stops the timer and guards pending warm-up callbacks. `CursorTextService.PreWarm()` remains a startup-only UIA initialization
- **Popup window reuse**: ordinary reactivation reuses the off-screen window instead of forcing `Hide()`/`Show()`. A shell-cloaked window (another virtual desktop) or an unavailable DWM query retains the hide/show fallback. Input reset runs after the exit animation and reuses an empty `FlowDocument`; localized labels are refreshed on activation
- **Independent foreground activation**: use ordinary WPF activation without `AttachThreadInput`; another application's input queue must not be joined to the UI thread. UIA selection work is queued after the keyboard callback has posted to the dispatcher. Interactive focus and actual IME composition require an unlocked desktop check
- **JSON source generation**: All Settings/History/API DTOs use `JsonSerializerContext` — eliminates reflection
- **Live Markdown streaming**: results render as formatted Markdown *during* streaming (throttled to 200ms via `RenderThrottleMs`), not just at the end; `RenderMarkdown` also unwraps an outer ` ```markdown ` fence that some models (gpt-5.x) wrap the whole answer in. `_streamingRun`/`_streamingDoc` caching backs the plain-text fallback (`RenderPlainText`)
- **SSE Utf8JsonReader**: Zero-alloc `Utf8JsonReader` instead of `JsonDocument` for streaming JSON parsing
- **Cursor animation GPU acceleration**: WPF Storyboard on composition thread instead of DispatcherTimer
- **Module handle caching**: `NativeMethods.CachedModuleHandle` avoids Process allocation on every hook install
- **History debounced save**: 500ms debounce on consecutive lookups to minimize I/O
- **ListBox virtualization**: `VirtualizingPanel.VirtualizationMode="Recycling"` enabled
- **PublishReadyToRun**: AOT precompilation on publish builds

## Key Architecture Decisions
1. **CapsLock quasimodal**: `SetWindowsHookEx` WH_KEYBOARD_LL intercepts CapsLock on a **dedicated message-pump thread** (never blocked by UI work → reliable suppression, no caps toggling). EnsoHold(≥0.5s) vs QuickTap(<0.5s) distinction. Tab raises `ModeSwitchRequested` instead of round-tripping through the buffer
2. **6-provider SSE**: HttpClient + `ResponseHeadersRead` + `StreamReader` → `Utf8JsonReader` chunk parsing
3. **3 Lookup Modes**: Dictionary(≤3 words), Translate(>3 words), Assist(code/URL/formula/non-language) — `PromptBuilder.AutoSelectMode()` auto-selects
4. **Overlay**: Transparent `Window` + `AllowsTransparency="True"`. 220ms fade in / 180ms fade out. Global mouse hook for outside-click detection
5. **System tray**: `NotifyIcon` + `ShutdownMode="OnExplicitShutdown"`. Only tray exit terminates the app
6. **Localization**: `ResourceDictionary` swap (`Strings.ko.xaml`/`Strings.en.xaml`) via `LocalizationService`. XAML uses `DynamicResource`; code-behind uses `Loc("key")` helper. Language persisted as `UiLanguage` enum in settings

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

Look up the row matching your change, then update only the sections named in each column (— means no update needed for that document):

| Change trigger | copilot-instructions.md (this file) | README.md | SKILL.md (glossary) |
|----------------|-------------------------------------|-----------|---------------------|
| New file/service added | Project Structure | Project Structure | — |
| File/service removed or renamed | Project Structure | Project Structure | Element Glossary |
| New NuGet package | Tech Stack | Tech Stack | — |
| New performance optimization | Performance Patterns | — | — |
| Coding convention added/changed | Coding Conventions | — | — |
| New UI element/shortcut/mode | — | — | Element Glossary |
| Feature added/removed | — | Features | — |

## Important Notes
- `UseWindowsForms=true` — for NotifyIcon; `GlobalUsings.cs` resolves WPF/WinForms conflicts
- OverlayWindow uses raw WPF transparency (not WPF-UI)
- `ShutdownMode="OnExplicitShutdown"` — only exits via tray menu
- When debugging, kill any existing `VerbaCore.exe` process first to avoid keyboard hook conflicts
