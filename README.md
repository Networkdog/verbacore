<p align="center">
  <h1 align="center">VerbaCore</h1>
  <p align="center">
    <strong>AI-powered dictionary, translator & grammar analyzer — always one keystroke away.</strong>
  </p>
  <p align="center">
    <a href="https://github.com/Networkdog/verbacore/releases"><img src="https://img.shields.io/github/v/release/Networkdog/verbacore?style=flat-square" alt="Release"></a>
    <a href="https://github.com/Networkdog/verbacore/blob/main/LICENSE"><img src="https://img.shields.io/github/license/Networkdog/verbacore?style=flat-square" alt="License"></a>
    <img src="https://img.shields.io/badge/platform-Windows-blue?style=flat-square" alt="Platform">
    <img src="https://img.shields.io/badge/.NET-8.0-purple?style=flat-square" alt=".NET 8">
  </p>
</p>

---

VerbaCore lives in your system tray and activates instantly with **CapsLock** or a global hotkey. Type a word or sentence, release the key, and get AI-powered results streamed in real time — no browser, no tab switching, no friction.

<!-- TODO: Add a GIF demo here -->
<!-- ![VerbaCore Demo](docs/demo.gif) -->

## Why VerbaCore?

Most dictionary & translation apps make you **leave** what you're doing. VerbaCore is designed to be **invisible until you need it** — then it appears, answers, and gets out of the way.

- **Zero-friction activation** — Hold CapsLock, type, release. That's it.
- **Real-time streaming** — Results appear word-by-word as the AI generates them.
- **No accidental CAPS LOCK** — The hook forces CapsLock off, so your text stays clean.
- **Lightweight** — Single-file executable, ~60 MB, minimal memory footprint.
- **DPI-aware** — PerMonitorV2 DPI awareness for crisp rendering on high-DPI and multi-monitor setups.

## Features

### Three AI-Powered Modes

| Mode | What it does |
|------|-------------|
| 📖 **Dictionary** | Deep word lookup with etymology storytelling, IPA pronunciation, Korean phonetic guide, synonyms/antonyms, and usage examples |
| 🔄 **Translate** | Context-aware translation with nuance notes, formality levels, and alternatives. Input text compacts to a single-line summary so the translation result takes center stage |
| 💡 **Assist** | Explains non-language content — code snippets, error messages, URLs, formulas, config files — with practical, actionable insight |

**Smart auto-selection**: The mode is chosen automatically — short input (≤3 words) → Dictionary, longer input → Translate, code/URLs/formulas → Assist. Press **Tab** to override manually.

### 6 AI Providers

| Provider | Auth | Notes |
|----------|------|-------|
| **OpenAI** | Bearer token | Default. Supports GPT-4o, GPT-4o-mini, o1, o3, GPT-5.x |
| **Azure OpenAI** | `api-key` header | Custom endpoint + deployment name + API version |
| **Anthropic** | `x-api-key` | Native Claude API with `content_block_delta` SSE parsing |
| **Google Gemini** | Bearer token | Via `generativelanguage.googleapis.com` OpenAI-compatible endpoint |
| **OpenRouter** | Bearer token | Access 100+ models through a single API |
| **Custom** | Bearer token | Any OpenAI-compatible endpoint (local LLMs, etc.) |

All providers use SSE streaming for real-time response rendering.

### Multiple Input Methods

| Method | How |
|--------|-----|
| ⌨️ **CapsLock Hold** (EnsoMode) | Hold CapsLock → type → release to look up. Large text overlay |
| ⌨️ **CapsLock Tap** (PersistentMode) | Quick-tap CapsLock (<0.5s) to open a persistent input box with full IME support |
| 🔥 **Global Hotkey** | `Ctrl+Alt+V` (customizable) to activate PersistentMode from any app |
| 🖱️ **Cursor Text Grab** | Automatically grabs selected text under your cursor via COM UIA3 (works with Chromium/Electron) |

### Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `CapsLock` (hold) | Open overlay + EnsoMode, lookup on release |
| `CapsLock` (tap <0.5s) | Toggle PersistentMode |
| `Tab` | Cycle Dictionary ↔ Translate ↔ Assist |
| `Backspace` | Delete last character |
| `Escape` | Close overlay / cancel lookup |
| `Enter` | Submit input (PersistentMode) |
| `Ctrl+C` | Copy result text to clipboard |
| `Ctrl+Alt+V` | Global hotkey (customizable) |

### 15 Languages Supported

English, Korean, Japanese, Chinese (Simplified & Traditional), Spanish, French, German, Portuguese, Russian, Arabic, Italian, Dutch, Vietnamese, Thai, Indonesian

### Overlay & UI

- 🎨 **Dark / Light / System theme** — auto-detects Windows theme via WPF-UI Mica
- 📐 **3 overlay sizes** — Small, Medium, Large (adjusts window size + font scaling)
- 📍 **9-point popup positioning** — place the overlay at any corner, edge, or center
- 🔄 **Animated loading spinner** — rotating arc spinner with smooth start/stop transitions
- ✂️ **Smart input compaction** — in Translate mode, the input text shrinks to a single-line summary with ellipsis so the translation result gets maximum space
- 🖋️ **Centralized font system** — UI Font, Content Font, and Code Font defined once in `App.xaml` and shared across the entire app

### More

- 📋 **Lookup history** — searchable, copyable, deletable, re-queryable (up to 200 items)
- 🔐 **Secure API key storage** — encrypted with Windows DPAPI (CurrentUser scope)
- 🚀 **Start with Windows** — auto-launch at login (registry-managed)
- 🧠 **Reasoning model support** — auto-detects o1, o1-mini, o3, o3-mini, o4-mini, GPT-5.x models and disables temperature
- 📎 **Ctrl+C in overlay** — copy AI results directly to clipboard
- 🔒 **Single instance** — prevents duplicate processes via named Mutex
- ⚡ **Live settings** — theme, hotkey, and auto-start changes apply immediately without restart
- 🔄 **Smart error handling** — distinct Korean messages for auth errors (401/403), rate limits (429), timeouts, and network issues
- 📊 **Markdown rendering** — results rendered as rich FlowDocuments with theme-aware styling (headings, code blocks, blockquotes, lists, tables)
- 🖥️ **Per-Monitor DPI awareness** — crisp rendering on high-DPI and mixed-DPI multi-monitor setups

## Quick Start

### Option 1: Download the Installer

1. Go to [**Releases**](https://github.com/Networkdog/verbacore/releases)
2. Download `VerbaCore-Setup-x.x.x.exe` (installer) or `VerbaCore-x.x.x-portable.zip` (portable)
3. Run → enter your API key in Settings (supports OpenAI, Azure, Anthropic, Google, OpenRouter)
4. Hold **CapsLock**, type a word, release — done!

### Option 2: Build from Source

**Prerequisites**: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), Windows 7+

```bash
git clone https://github.com/Networkdog/verbacore.git
cd verbacore
dotnet run --project src/VerbaCore/VerbaCore.csproj
```

## Configuration

Open **Settings** by double-clicking the system tray icon or right-clicking → ⚙ Settings.

| Setting | Default | Notes |
|---------|---------|-------|
| Provider | OpenAI | OpenAI, Azure OpenAI, Anthropic, Google Gemini, OpenRouter, Custom |
| Model | `gpt-4o-mini` | Cost-effective; switch to `gpt-4o` for higher quality |
| Reasoning Effort | `none` | For reasoning models (o1/o3): `none`, `minimal`, `low`, `medium`, `high`, `xhigh` |
| Global Hotkey | `Ctrl+Alt+V` | Customizable (e.g. `Shift+F12`, `Win+Z`) |
| Theme | System | Dark / Light / System |
| Popup Position | Center | 9-point grid: corners, edges, center |
| Overlay Size | Medium | Small / Medium / Large — adjusts window + font scaling |
| Start with Windows | Off | Adds to `HKCU\...\Run` registry |

### Azure OpenAI

Switch the provider to **Azure OpenAI** and fill in:
- **Endpoint** — e.g. `https://your-resource.openai.azure.com`
- **Deployment Name** — your model deployment name
- **API Version** — default `2024-10-21`

### Custom / Local LLMs

Switch the provider to **Custom** and enter any OpenAI-compatible endpoint. Works with:
- [Ollama](https://ollama.ai) (`http://localhost:11434/v1/chat/completions`)
- [LM Studio](https://lmstudio.ai) (`http://localhost:1234/v1/chat/completions`)
- Any other OpenAI-compatible API

## Tech Stack

| Component | Technology |
|-----------|-----------|
| Language | C# 12 / .NET 8 |
| UI Framework | WPF + [WPF-UI 3.x](https://github.com/lepoco/wpfui) (Fluent Design, Mica) |
| AI Backend | OpenAI / Azure OpenAI / Anthropic / Google / OpenRouter / Custom (SSE streaming) |
| Architecture | MVVM ([CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)) |
| DI | Microsoft.Extensions.DependencyInjection |
| Markdown | [Markdig.Wpf](https://github.com/Kryptos-FR/markdig.wpf) |
| Hotkeys | [NHotkey.Wpf](https://github.com/thomaslevesque/NHotkey) |
| Text Extraction | COM UIA3 (UI Automation, Chromium/Electron compatible) |
| DPI | PerMonitorV2 via `ApplicationHighDpiMode` |
| Installer | [Inno Setup](https://jrsoftware.org/isinfo.php) |
| CI/CD | GitHub Actions (auto-release on tag push) |

## Building the Installer

```bash
# 1. Publish self-contained single-file executable
dotnet publish src/VerbaCore/VerbaCore.csproj -c Release -r win-x64 ^
  --self-contained true -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true -o publish-standalone

# 2. Build installer (requires Inno Setup 6)
iscc installer.iss
```

Output: `installer-output/VerbaCore-Setup-x.x.x.exe`

> **Tip**: Just push a `v*` tag to GitHub and the CI will build & publish the release automatically.

## Project Structure

```
src/VerbaCore/
├── App.xaml(.cs)              # Entry point, DI container, tray icon, font resources
├── OverlayWindow.xaml(.cs)    # Frameless Enso-style overlay (animated spinner, compact input)
├── SettingsWindow.xaml(.cs)   # FluentWindow with Mica backdrop
├── app.manifest               # Application manifest (PerMonitorV2 DPI)
├── Services/
│   ├── CapsLockService.cs     # Low-level keyboard hook (EnsoMode + PersistentMode detection)
│   ├── OpenAiService.cs       # 6-provider SSE streaming + Utf8JsonReader parsing
│   ├── PromptBuilder.cs       # Mode-specific prompt engineering + auto-mode selection
│   ├── SettingsService.cs     # JSON persistence + DPAPI encryption (source-generated)
│   ├── HistoryService.cs      # Lookup history (200 items max, debounced save, source-generated)
│   ├── HotkeyService.cs       # NHotkey global hotkey registration
│   └── CursorTextService.cs   # COM UIA3 selected text extraction
├── Models/
│   ├── AppSettings.cs         # Settings model + enums (ApiProvider, OverlayPosition, OverlaySize, ThemeMode)
│   ├── AppJsonContext.cs      # System.Text.Json source generation contexts
│   ├── LookupResult.cs        # Single lookup result + LookupMode enum
│   └── LookupHistory.cs       # History item model
├── ViewModels/                # MVVM ViewModels (CommunityToolkit.Mvvm)
├── Views/                     # WPF UserControls (SettingsView, HistoryView)
└── Helpers/
    ├── NativeMethods.cs       # Win32 P/Invoke + CachedModuleHandle
    ├── UIA3Interop.cs         # COM UIA3 interop definitions
    └── Converters.cs          # XAML value converters
```

## Contributing

Contributions are welcome! Here are some ideas:

- 🌍 **More languages** — add prompt templates for new language pairs
- 🎨 **Custom themes** — accent colors, font customization
- 📚 **Offline dictionaries** — local dictionary fallback when API is unavailable
- 🖼️ **Screenshots & GIFs** — help make this README shine
- 🐛 **Bug reports** — open an issue with reproduction steps
- 📖 **Documentation** — usage guides, tutorials, translations

### How to Contribute

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

This project is open source. See the [LICENSE](LICENSE) file for details.

## Acknowledgments

- [WPF-UI](https://github.com/lepoco/wpfui) — Beautiful Fluent Design controls for WPF
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — Modern MVVM toolkit
- [Markdig.Wpf](https://github.com/Kryptos-FR/markdig.wpf) — Markdown rendering in WPF
- [NHotkey](https://github.com/thomaslevesque/NHotkey) — Global hotkey management
- [Inno Setup](https://jrsoftware.org/isinfo.php) — Windows installer framework
