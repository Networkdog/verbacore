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

## Features

### Three AI-Powered Modes

| Mode | What it does |
|------|-------------|
| 📖 **Dictionary** | Deep word lookup with etymology storytelling, IPA pronunciation, synonyms/antonyms, and usage examples |
| 🔄 **Translate** | Context-aware translation with nuance notes, formality levels, and alternatives |
| 📝 **Analyze** | Grammar breakdown — parts of speech, tense/voice analysis, sentence structure, idioms |

Press **Tab** to cycle between modes on the fly.

### Multiple Input Methods

| Method | How |
|--------|-----|
| ⌨️ CapsLock Hold | Hold CapsLock → type → release to look up (Enso-style large text overlay) |
| ⌨️ CapsLock Tap | Quick-tap CapsLock to open a persistent input box with full IME support |
| 🔥 Global Hotkey | `Ctrl+Alt+V` (customizable) to activate from any app |
| 🎤 Voice Input | Click the mic button — speaks and auto-looks up |
| 🖱️ Cursor Text | Automatically grabs selected text under your cursor via UI Automation |

### 15 Languages Supported

English, Korean, Japanese, Chinese (Simplified & Traditional), Spanish, French, German, Portuguese, Russian, Arabic, Italian, Dutch, Vietnamese, Thai, Indonesian

### More

- 🌙 **Dark / Light / System theme** with Fluent Design (WPF-UI)
- 📋 **Lookup history** — searchable, re-queryable, persisted across sessions (up to 200 items)
- 🔐 **Secure API key storage** — encrypted with Windows DPAPI (machine + user scoped)
- 📍 **9-point popup positioning** — place the overlay at any corner, edge, or center
- 🚀 **Start with Windows** — optional auto-launch at login
- 🧠 **Reasoning model support** — works with GPT-4o, GPT-4o-mini, o1, o3 with configurable thinking effort

## Quick Start

### Option 1: Download the Installer

1. Go to [**Releases**](https://github.com/Networkdog/verbacore/releases)
2. Download `VerbaCore-Setup-x.x.x.exe` (installer) or `VerbaCore-x.x.x-portable.zip` (portable)
3. Run → enter your [OpenAI API key](https://platform.openai.com/api-keys) in Settings
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
| Provider | OpenAI | Also supports Azure OpenAI |
| Model | `gpt-4o-mini` | Cost-effective; switch to `gpt-4o` for higher quality |
| Global Hotkey | `Ctrl+Alt+V` | Customizable (e.g. `Shift+F12`, `Win+Z`) |
| Theme | Dark | Dark / Light / System |
| Popup Position | Center | 9-point grid: corners, edges, center |
| Start with Windows | Off | Adds to `HKCU\...\Run` registry |

### Azure OpenAI

VerbaCore also supports Azure OpenAI Service — just switch the provider toggle and fill in your endpoint, deployment name, and API version.

## Tech Stack

| Component | Technology |
|-----------|-----------|
| Language | C# 12 / .NET 8 |
| UI Framework | WPF + [WPF-UI](https://github.com/lepoco/wpfui) (Fluent Design) |
| AI Backend | OpenAI / Azure OpenAI (streaming SSE) |
| Architecture | MVVM ([CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)) |
| Markdown | [Markdig.Wpf](https://github.com/Kryptos-FR/markdig.wpf) |
| Hotkeys | [NHotkey.Wpf](https://github.com/thomaslevesque/NHotkey) |
| Speech | System.Speech.Recognition |
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
├── App.xaml.cs              # Entry point, DI container, tray icon
├── OverlayWindow.xaml.cs    # Frameless Enso-style overlay (CapsLock UI)
├── Services/
│   ├── OpenAiService.cs     # Streaming HTTP client for OpenAI/Azure
│   ├── CapsLockService.cs   # Low-level keyboard hook
│   ├── PromptBuilder.cs     # Mode-specific prompt engineering
│   ├── SettingsService.cs   # JSON persistence + DPAPI encryption
│   ├── HistoryService.cs    # Lookup history (200 items max)
│   ├── SpeechInputService.cs # System.Speech wrapper
│   └── CursorTextService.cs # UI Automation text extraction
├── ViewModels/              # MVVM ViewModels
├── Views/                   # WPF UserControls
├── Models/                  # Data models
└── Helpers/                 # P/Invoke, value converters
```

## Contributing

Contributions are welcome! Here are some ideas:

- 🌍 **More languages** — add prompt templates for new language pairs
- 🎨 **Custom themes** — accent colors, font customization
- 📚 **Offline dictionaries** — local dictionary fallback when API is unavailable
- 🔌 **Plugin system** — support for other AI providers (Anthropic, Gemini, local LLMs)
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

## 라이선스

MIT
