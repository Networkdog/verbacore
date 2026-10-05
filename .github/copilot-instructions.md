# VerbaCore — Copilot Instructions

## Project Overview
VerbaCore is a lightweight Windows desktop AI dictionary & translation app. It lives in the system tray with no main window. Hold CapsLock, type a word, release — AI results stream onto a transparent overlay.

## Tech Stack
- **Language**: C# 12 / .NET 8 (`net8.0-windows7.0`)
- **UI**: WPF + WPF-UI 3.x (SettingsPanel) / raw WPF (Overlay)
- **Architecture**: MVVM (CommunityToolkit.Mvvm) — settings; code-behind — overlay
- **DI**: Microsoft.Extensions.DependencyInjection
- **AI**: HttpClient SSE streaming (7 providers: OpenAI, AzureOpenAI, Anthropic, Google, OpenRouter, Custom, Foundry); explicit Chat Completions / Anthropic Messages protocol; Model field doubles as Azure Deployment Name
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
│   ├── OpenAiService.cs       — Protocol-aware requests, validation, responses, and SSE parsing
│   ├── PromptBuilder.cs       — Mode-specific prompt generation + AutoMode selection
│   ├── SettingsService.cs     — JSON settings load/save + DPAPI (source-generated)
│   ├── HistoryService.cs      — JSON history + debounced save (source-generated)
│   ├── HotkeyService.cs       — NHotkey global hotkey registration/unregistration
│   ├── LocalizationService.cs — Runtime UI language switching via ResourceDictionary swap
│   ├── CursorTextService.cs   — Captured-window selection, MTA worker, cancellation/deadline (+ startup PreWarm)
│   ├── OfficeSelectionReader.cs — Word/Outlook editor, Excel cells, PowerPoint text via OBJID_NATIVEOM
│   └── AccessibleSelectionReader.cs — MSAA/IAccessible2 selected ranges for Chromium/Electron
└── Helpers/
    ├── NativeMethods.cs       — Win32 P/Invoke + CachedModuleHandle
    ├── UIA3Interop.cs         — COM UIA3 interface definitions
    ├── SelectionInterop.cs    — Source window/focus snapshot, MSAA/native OM and IAccessible2 interop
    └── Converters.cs          — XAML value converters

tests/VerbaCore.PopupTests/
├── VerbaCore.PopupTests.csproj # Standalone Windows popup regression harness
└── Program.cs                 # Popup, API contract, settings UI, and cache regression checks

docs/
├── foundry-api-compatibility.md # API research, settings, and compatibility boundaries
└── selected-text-compatibility.md # Clipboard-free selection research, app prerequisites, and verified boundaries
```

## Coding Conventions
- Use `file-scoped namespaces`
- Use `primary constructors` for classes whose only constructor sets readonly fields via DI injection (e.g., services). Do not use them when the constructor body contains any logic beyond field assignment — e.g. event subscriptions, method calls, validation, or conditional branching
- `CommunityToolkit.Mvvm` attributes: `[ObservableProperty]`, `[RelayCommand]`
- `System.Text.Json` serialization — **always use source generation contexts** (`SettingsJsonContext`, `HistoryJsonContext`, `ApiJsonContext`)
- All async methods must accept `CancellationToken`
- P/Invoke uses `DllImport` (not LibraryImport — avoids AllowUnsafeBlocks)
- When adding new JSON DTOs, register with `[JsonSerializable]` on an existing `JsonSerializerContext` or create a new one
- API options are explicit contracts, not model-name heuristics. Omit optional sampling/reasoning/token overrides by default (Messages still requires max_tokens). Keep protocol, instruction role, thinking schema, and output-token parameter independent; validate incompatible combinations before HTTP. Do not automatically retry with altered semantics. See `docs/foundry-api-compatibility.md`

## Performance Patterns
- **Dedicated hook thread**: `WH_KEYBOARD_LL` is installed on its own STA thread with a private `GetMessage` pump. Windows delivers the callback on the installing thread and lets the key through unhooked if it doesn't return within `LowLevelHooksTimeout` (300ms) — the UI thread is too easily blocked to host it. Keeping the callback off the UI thread is what makes CapsLock suppression reliable (no caps-mode toggling) even while the overlay/UIA initialize on first use
- **Mouse hook isolation**: `WH_MOUSE_LL` uses the same dedicated input thread, never the WPF dispatcher. Movement immediately passes through; only button-down coordinates are posted asynchronously for outside-click checks. Monitoring installation/removal is posted to that thread, so UI rendering cannot stall system-wide mouse delivery
- **Native hold-mode input**: both hold and quick-tap use the existing IME TextBox. While it owns keyboard focus and the overlay is foreground, the hook forwards text, Alt, and Hangul keys to Windows; the old character buffer is only a pre-focus fallback. Hold release drains queued input before reading the TextBox. Escape stays latched until CapsLock-up and raises cancellation, not lookup. Gesture IDs reject stale releases, focus callbacks, and outside clicks
- **Non-blocking hook callbacks**: every `CapsLockService` event handler marshals with `Dispatcher.BeginInvoke`, never `Invoke`. Blocking inside the callback is what makes CapsLock fall through to plain case-toggling
- **Hook re-arm watchdog**: the hook is reinstalled every 45s (skipped mid-keystroke), recovering from OS-dropped hooks and keeping the callback path resident in the working set
- **Async selected-text grab**: `CursorTextService` captures source HWND/process/native focus before overlay activation and reads UIA, Office native OM, and MSAA/IAccessible2 on a dedicated MTA worker. The overlay shows immediately; requests have a 2s caller deadline, bounded traversal, supersession, and hide/shutdown cancellation. No clipboard or synthetic copy; no document/value/name fallback for selections. Filter password controls and IA2 object markers; retain the 2,000-character return bound. In-flight third-party COM calls cannot be forcibly interrupted; disposal leaves queue cleanup to the worker. VS Code may require `editor.accessibilitySupport: on`; never change it silently. See `docs/selected-text-compatibility.md` for actual verification and limitations
- **Idle popup readiness**: `OverlayWindow.PreWarm()` realizes the HWND and lays out both input modes without activation. A 30s `ContextIdle` timer maintains the layout only when the overlay is fully transparent and not in use; it performs no bitmap rendering, UIA calls, or foreground activation. `CloseForShutdown()` stops the timer and guards pending warm-up callbacks. `CursorTextService.PreWarm()` remains a startup-only UIA initialization
- **Popup window reuse**: ordinary reactivation reuses the off-screen window instead of forcing `Hide()`/`Show()`. A shell-cloaked window (another virtual desktop) or an unavailable DWM query retains the hide/show fallback. Input reset runs after the exit animation and reuses an empty `FlowDocument`; localized labels are refreshed on activation
- **Independent foreground activation**: never join another application's input queue with `AttachThreadInput`. Quick tap verifies foreground HWND, native focus, and TextBox keyboard focus with at most six asynchronous attempts. A failed activation may use one left-Alt down/up pair on the current input desktop, only with no modifiers or CapsLock held; never use this during pre-warm. Hide, shutdown, a newer gesture, or a different foreground app ends recovery. UIA selection work remains outside the keyboard callback
- **JSON source generation**: All Settings/History/API DTOs use `JsonSerializerContext` — eliminates reflection
- **Live Markdown streaming**: results render as formatted Markdown *during* streaming (throttled to 200ms via `RenderThrottleMs`), not just at the end; `RenderMarkdown` also unwraps an outer ` ```markdown ` fence that some models (gpt-5.x) wrap the whole answer in. `_streamingRun`/`_streamingDoc` caching backs the plain-text fallback (`RenderPlainText`)
- **SSE Utf8JsonReader**: Scope parsing to the selected protocol's final-text paths, compare property names with UTF-8 spans, and avoid a `JsonDocument` per chunk. Assemble SSE data frames; ignore reasoning/tool payloads but surface errors, truncation, and incomplete termination before saving a successful lookup
- **Request-specific cache**: source-generated cache identity includes endpoint, protocol, deployment, and explicit request options, never API keys. Do not reuse results across different request contracts
- **Cursor animation GPU acceleration**: WPF Storyboard on composition thread instead of DispatcherTimer
- **Module handle caching**: `NativeMethods.CachedModuleHandle` avoids Process allocation on every hook install
- **History debounced save**: 500ms debounce on consecutive lookups to minimize I/O
- **ListBox virtualization**: `VirtualizingPanel.VirtualizationMode="Recycling"` enabled
- **PublishReadyToRun**: AOT precompilation on publish builds

## Key Architecture Decisions
1. **CapsLock quasimodal**: `SetWindowsHookEx` WH_KEYBOARD_LL intercepts CapsLock on a **dedicated message-pump thread** (never blocked by UI work → reliable suppression, no caps toggling). EnsoHold(≥0.5s) vs QuickTap(<0.5s) distinction. Tab raises `ModeSwitchRequested` instead of round-tripping through the buffer
2. **7-provider SSE**: HttpClient + `ResponseHeadersRead` + `StreamReader` → protocol-aware `Utf8JsonReader` parsing
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
