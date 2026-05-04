<div align="center">

<img src="res/icons/verbacore.png" alt="VerbaCore" width="128" height="128" />

# VerbaCore

### **Look up anything. Anywhere. Instantly.**

A keystroke-fast AI dictionary, translator, and code explainer that lives in your tray —
**not** in another browser tab.

<p>
  <a href="https://github.com/Networkdog/verbacore/releases/latest">
    <img src="https://img.shields.io/github/v/release/Networkdog/verbacore?style=for-the-badge&label=Download&color=2ea043" alt="Latest Release">
  </a>
  <a href="https://github.com/Networkdog/verbacore/releases">
    <img src="https://img.shields.io/github/downloads/Networkdog/verbacore/total?style=for-the-badge&color=blue" alt="Downloads">
  </a>
  <a href="https://github.com/Networkdog/verbacore/stargazers">
    <img src="https://img.shields.io/github/stars/Networkdog/verbacore?style=for-the-badge&color=yellow" alt="Stars">
  </a>
  <a href="LICENSE">
    <img src="https://img.shields.io/github/license/Networkdog/verbacore?style=for-the-badge&color=lightgrey" alt="License">
  </a>
</p>

<p>
  <img src="https://img.shields.io/badge/Windows-10%2F11-0078D6?style=flat-square&logo=windows&logoColor=white" alt="Windows">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet&logoColor=white" alt=".NET 8">
  <img src="https://img.shields.io/badge/C%23-12-239120?style=flat-square&logo=csharp&logoColor=white" alt="C# 12">
  <img src="https://img.shields.io/badge/Streaming-SSE-ff6b6b?style=flat-square" alt="SSE Streaming">
</p>

<p>
  <a href="#-quick-start"><b>Quick Start</b></a> ·
  <a href="#-features"><b>Features</b></a> ·
  <a href="#-how-it-works"><b>How It Works</b></a> ·
  <a href="#-providers"><b>Providers</b></a> ·
  <a href="#-faq"><b>FAQ</b></a> ·
  <a href="#-roadmap"><b>Roadmap</b></a>
</p>

<!-- TODO: Replace with the real demo GIF once recorded -->
<!-- <img src="docs/demo.gif" alt="VerbaCore demo" width="720" /> -->

</div>

---

## ✨ Why VerbaCore?

> Every dictionary or translator app forces you to **leave** what you're doing — switch windows, open a browser, paste text, click a button, wait. **VerbaCore deletes all of that.**

Hold **CapsLock**. Type a word. Release. The answer streams onto a transparent overlay above whatever you were reading — code, an email, a paper, a design tool — and disappears the moment you don't need it.

|   | VerbaCore | Browser tab | Built-in OS dictionary | Other AI apps |
|---|:---:|:---:|:---:|:---:|
| **Activate without leaving the app** | ✅ | ❌ | ⚠️ | ❌ |
| **Stream results in real time** | ✅ | ⚠️ | ❌ | ⚠️ |
| **Works on selected text under the cursor** | ✅ | ❌ | ⚠️ | ❌ |
| **Bring your own model (OpenAI, Claude, Gemini, local)** | ✅ | — | ❌ | ⚠️ |
| **No background CPU when idle** | ✅ | ❌ | ✅ | ❌ |
| **Keys never leave your machine (DPAPI)** | ✅ | — | — | ⚠️ |

---

## ⚡ Quick Start

### 1. Install

[<kbd> <br> &nbsp;&nbsp; **⬇️&nbsp; Download for Windows** &nbsp;&nbsp; <br> </kbd>](https://github.com/Networkdog/verbacore/releases/latest)

Pick `VerbaCore-Setup-x.x.x.exe` (installer) or `VerbaCore-x.x.x-portable.zip` (portable, no admin required).

### 2. Plug in any AI provider

OpenAI, Azure OpenAI, Anthropic, Google Gemini, OpenRouter, or **any OpenAI-compatible local model** (Ollama / LM Studio).
Your key is encrypted at rest with **Windows DPAPI** — bound to your user account, unreadable by any other process.

### 3. Use it

```
   Hold CapsLock  ➜  type a word  ➜  release
```

That's it. The overlay fades in, the AI streams its answer, and the overlay fades back out the moment you click away.

---

## 🚀 Features

### Three modes — auto-selected, or one keystroke away

<table>
<tr>
  <td align="center" width="33%">
    <h4>📖 Dictionary</h4>
    Etymology storytelling, IPA, phonetic guide, synonyms, antonyms, real-world usage examples.
  </td>
  <td align="center" width="33%">
    <h4>🔄 Translate</h4>
    Context-aware translation across <b>15 languages</b>, with nuance notes, formality levels, and alternatives.
  </td>
  <td align="center" width="33%">
    <h4>💡 Assist</h4>
    Explains code, errors, URLs, regex, formulas, config files — anything that isn't natural language.
  </td>
</tr>
</table>

> **Smart auto-selection:** ≤3 words → Dictionary · longer text → Translate · code/URLs/formulas → Assist.
> Press **`Tab`** to override at any time.

### Multiple ways to invoke

| Method | How it feels |
|---|---|
| 🅰️ **CapsLock Hold** *(EnsoMode)* | Hold → type → release. Big, focused overlay. |
| 🅰️ **CapsLock Tap** *(PersistentMode)* | Quick-tap (<0.5s) opens a persistent input box with full IME support. |
| 🔥 **Global Hotkey** | `Ctrl+Alt+V` (customizable) from anywhere. |
| 🖱️ **Cursor Text Grab** | Selected text under the cursor is auto-captured via UIA3 — works in Chromium / Electron / Office. |

### Keyboard-first by design

| Shortcut | Action |
|---|---|
| `CapsLock` *(hold)* | Open overlay → look up on release |
| `CapsLock` *(tap < 0.5s)* | Toggle PersistentMode |
| `Tab` | Cycle Dictionary ↔ Translate ↔ Assist |
| `Backspace` | Delete last character |
| `Esc` | Close overlay / cancel lookup |
| `Enter` | Submit (PersistentMode) |
| `Ctrl+C` | Copy result to clipboard |
| `Ctrl+Alt+V` | Global hotkey *(customizable)* |

### Built for serious daily use

- **🌍 15 languages** — English, Korean, Japanese, Chinese (Simplified & Traditional), Spanish, French, German, Portuguese, Russian, Arabic, Italian, Dutch, Vietnamese, Thai, Indonesian.
- **🎨 Native Fluent UI** — Dark / Light / System theme with **Mica** backdrop, auto-tracking Windows.
- **📐 9-point overlay positioning** × 3 size presets — pin the popup wherever fits your workflow.
- **🖋️ Typography system** — UI Font, Content Font, and Code Font defined once and shared everywhere.
- **📊 Rich Markdown rendering** — headings, code blocks, blockquotes, lists, tables — all theme-aware.
- **📋 Searchable history** — last 200 lookups, copyable, deletable, re-queryable.
- **🧠 Reasoning models supported** — auto-detects o1, o3, o4-mini, GPT-5.x and routes parameters correctly.
- **🚀 Start with Windows** — opt-in, registry-based, instant.
- **🖥️ Per-Monitor V2 DPI** — crisp on high-DPI laptops *and* mixed-DPI monitor setups.
- **🔒 Single-instance Mutex** — never two copies of the hook fighting.
- **⚡ Live settings** — theme, hotkey, autostart all apply without restart.
- **✂️ Smart input compaction** — in Translate mode, the input shrinks to a one-line summary so the answer takes center stage.
- **🔄 Smart error handling** — distinct messages for auth (401/403), rate limits (429), timeouts, and network issues.

---

## 🧠 How It Works

```
┌─────────────────────────────────────────────────────────────────────────┐
│  Low-level keyboard hook  ──▶  CapsLock detector  ──▶  Overlay window   │
│  (WH_KEYBOARD_LL)              (Hold vs Tap)           (transparent WPF)│
│                                                              │          │
│                                                              ▼          │
│                                              ┌──────────────────────┐   │
│                                              │  Mode auto-selector  │   │
│                                              │  (Dict / Trans / Asst)│  │
│                                              └──────────┬───────────┘   │
│                                                         ▼               │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │  HttpClient  ──▶  SSE stream  ──▶  Utf8JsonReader  ──▶  WPF UI   │   │
│  │  (zero-alloc parsing, FlowDocument reuse, GPU-driven cursor)     │   │
│  └──────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
```

Engineered to be **invisible until you need it**:

- **Zero-alloc SSE parsing** — `Utf8JsonReader` over chunk buffers; no `JsonDocument` per token.
- **FlowDocument reuse** — streaming `Run` cached so WPF doesn't allocate every 200 ms.
- **Source-generated JSON** — Settings / History / API DTOs use `JsonSerializerContext` (no reflection).
- **Composition-thread cursor** — the loading cursor animates on the GPU, not on the dispatcher.
- **Cached module handle** — keyboard hook reinstall doesn't allocate a `Process` object.
- **Debounced history I/O** — 500 ms debounce to avoid disk thrash on rapid lookups.
- **PublishReadyToRun** — AOT precompiled in release builds.
- **No accidental CAPS** — the hook forces CapsLock back off after every lookup, so your text stays clean.

---

## 🔌 Providers

| Provider | Auth | Notes |
|---|---|---|
| **OpenAI** | Bearer token | Default. GPT-4o, GPT-4o-mini, o1, o3, o4-mini, GPT-5.x |
| **Azure OpenAI** | `api-key` header | Endpoint + Deployment Name + API Version |
| **Anthropic** | `x-api-key` | Native Claude API with `content_block_delta` SSE |
| **Google Gemini** | Bearer token | Via OpenAI-compatible `generativelanguage.googleapis.com` |
| **OpenRouter** | Bearer token | 100+ models behind a single key |
| **Custom** | Bearer token | Any OpenAI-compatible endpoint |

### Run it fully local

VerbaCore works offline against your own machine — pick **Custom** and point it at:

- **Ollama** → `http://localhost:11434/v1/chat/completions`
- **LM Studio** → `http://localhost:1234/v1/chat/completions`
- Any other OpenAI-compatible local server

> No telemetry. No analytics. No phoning home. Your keys stay in DPAPI; your text goes only to the provider *you* chose.

---

## ⚙️ Configuration

Open **Settings** by double-clicking the tray icon, or right-click → **⚙ Settings**.

| Setting | Default | Notes |
|---|---|---|
| Provider | OpenAI | OpenAI · Azure · Anthropic · Gemini · OpenRouter · Custom |
| Model | `gpt-4o-mini` | Cost-effective; switch to `gpt-4o` or Claude for higher quality |
| Reasoning Effort | `none` | For o1 / o3 / GPT-5: `none` · `minimal` · `low` · `medium` · `high` · `xhigh` |
| Global Hotkey | `Ctrl+Alt+V` | Anything (e.g., `Shift+F12`, `Win+Z`) |
| Theme | System | Dark / Light / System |
| Popup Position | Center | 9-point grid: corners, edges, center |
| Overlay Size | Medium | Small / Medium / Large — adjusts window + font scaling |
| Start with Windows | Off | Registry: `HKCU\...\Run` |

### Azure OpenAI

Switch the provider to **Azure OpenAI** and fill in:

- **Endpoint** — e.g. `https://your-resource.openai.azure.com`
- **Deployment Name** — your model deployment name *(reuses the Model field)*
- **API Version** — default `2024-10-21`

---

## 🛠️ Build From Source

**Prerequisites:** [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), Windows 10/11.

```powershell
git clone https://github.com/Networkdog/verbacore.git
cd verbacore
dotnet run --project src/VerbaCore/VerbaCore.csproj
```

### Building the installer

```powershell
# 1. Self-contained single-file publish
dotnet publish src/VerbaCore/VerbaCore.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -o publish-standalone

# 2. Build installer (requires Inno Setup 6)
iscc installer.iss
```

Output → `installer-output/VerbaCore-Setup-x.x.x.exe`.

> **Tip:** Push a `v*` git tag and GitHub Actions builds & publishes the release for you.

---

## 🧱 Tech Stack

| Layer | Technology |
|---|---|
| Language | C# 12 / .NET 8 (`net8.0-windows7.0`) |
| UI | WPF + [WPF-UI 3.x](https://github.com/lepoco/wpfui) (Mica) — settings · raw WPF — overlay |
| Architecture | MVVM via [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) |
| DI | `Microsoft.Extensions.DependencyInjection` |
| AI | `HttpClient` + SSE streaming + `Utf8JsonReader` (6 providers) |
| Markdown | [Markdig.Wpf](https://github.com/Kryptos-FR/markdig.wpf) |
| Hotkeys | [NHotkey.Wpf](https://github.com/thomaslevesque/NHotkey) |
| Text grab | COM UIA3 — Chromium / Electron compatible |
| DPI | PerMonitorV2 via `ApplicationHighDpiMode` |
| Persistence | `System.Text.Json` source-gen + Windows DPAPI |
| Installer | [Inno Setup 6](https://jrsoftware.org/isinfo.php) |
| CI/CD | GitHub Actions (auto-release on tag push) |

---

<details>
<summary><h2 style="display:inline-block">📁 Project Structure</h2></summary>

```
src/VerbaCore/
├── App.xaml(.cs)              # DI, tray icon, CapsLock hook setup
├── OverlayWindow.xaml(.cs)    # Transparent fullscreen overlay (input & results)
├── SettingsWindow.xaml(.cs)   # FluentWindow (Mica) — settings + history
├── GlobalUsings.cs            # WPF / WinForms namespace conflict resolution
├── app.manifest               # PerMonitorV2 DPI awareness
├── Services/
│   ├── CapsLockService.cs     # Low-level keyboard hook (WH_KEYBOARD_LL)
│   ├── OpenAiService.cs       # 6-provider SSE streaming + Utf8JsonReader
│   ├── PromptBuilder.cs       # Mode-specific prompts + AutoMode selection
│   ├── SettingsService.cs     # JSON + DPAPI (source-generated)
│   ├── HistoryService.cs      # 200-item history + debounced save
│   ├── HotkeyService.cs       # NHotkey global hotkey lifecycle
│   └── CursorTextService.cs   # COM UIA3 selected-text extraction
├── Models/
│   ├── AppSettings.cs         # Settings + enums
│   ├── AppJsonContext.cs      # System.Text.Json source-gen contexts
│   ├── LookupResult.cs        # Lookup result + LookupMode enum
│   └── LookupHistory.cs       # History item model
├── ViewModels/                # SettingsViewModel, HistoryViewModel
├── Views/                     # SettingsView, HistoryView (UserControls)
└── Helpers/
    ├── NativeMethods.cs       # Win32 P/Invoke + CachedModuleHandle
    ├── UIA3Interop.cs         # COM UIA3 interop definitions
    └── Converters.cs          # XAML value converters
```

</details>

---

## ❓ FAQ

<details>
<summary><b>Does VerbaCore disable my CapsLock?</b></summary>
<br>
The hook intercepts CapsLock for the duration of a lookup and forces it back off afterwards, so your text stays clean. Outside of lookups CapsLock keeps working — but quick-tapping it (&lt;0.5s) toggles VerbaCore's PersistentMode instead of CAPS. Want classic CapsLock behavior? Unbind it in Settings; any other hotkey works.
</details>

<details>
<summary><b>Is my API key safe?</b></summary>
<br>
Keys are encrypted at rest with <b>Windows DPAPI</b> (CurrentUser scope) — only your Windows user account on this machine can decrypt them. They never leave your device except in outgoing requests to the AI provider <i>you</i> chose.
</details>

<details>
<summary><b>Can I use it without sending data to the cloud?</b></summary>
<br>
Yes — pick the <b>Custom</b> provider and point it at Ollama, LM Studio, llama.cpp, or any OpenAI-compatible local server. Everything stays on your machine.
</details>

<details>
<summary><b>How heavy is it?</b></summary>
<br>
A single ~60 MB self-contained executable. Idle, it costs you essentially nothing — just a low-level keyboard hook and a tray icon. No background polling, no telemetry, no auto-update phoning home.
</details>

<details>
<summary><b>Why CapsLock specifically?</b></summary>
<br>
CapsLock is the most under-utilized prime-real-estate key on the keyboard. Hijacking it gives you a one-handed, modal trigger that doesn't conflict with shortcuts in any IDE, browser, or game. (And if you really want CapsLock as CapsLock, the global hotkey works too.)
</details>

<details>
<summary><b>Does it work with Chromium / Electron / VS Code?</b></summary>
<br>
Yes. Cursor text-grab uses COM UIA3, which works with Chromium-based apps including VS Code, Discord, Slack, Notion, and modern browsers.
</details>

<details>
<summary><b>Will there be a Mac / Linux version?</b></summary>
<br>
Not currently — VerbaCore is deeply tied to Win32 (low-level hooks, UIA3, DPAPI, Mica). A cross-platform version would essentially be a rewrite. PRs welcome if you want to start one.
</details>

---

## 🗺️ Roadmap

- [ ] Animated GIF / video demo in this README
- [ ] Pin-to-screen — keep a result visible while you keep working
- [ ] Custom prompt templates per-mode
- [ ] Offline dictionary fallback (Wiktionary import)
- [ ] Voice input via Whisper / Azure Speech
- [ ] Plugin system for custom modes
- [ ] Cross-platform exploration (macOS / Linux)

Want one of these sooner? **[Open an issue](https://github.com/Networkdog/verbacore/issues)** or 👍 an existing one.

---

## 🤝 Contributing

Contributions of every size are welcome — code, docs, design, screenshots, GIFs, translations, bug reports.

```bash
# 1. Fork → clone
git clone https://github.com/<you>/verbacore.git
cd verbacore

# 2. Branch
git checkout -b feature/your-amazing-idea

# 3. Build
dotnet run --project src/VerbaCore/VerbaCore.csproj

# 4. Open a PR — describe what & why
```

Especially looking for:

- 🎨 Screenshots & demo GIFs
- 🌍 Prompt-template tuning for under-represented languages
- 🐛 Repro steps for any edge case you hit
- 📖 Tutorials & blog posts (we'll link to yours)

---

## 💖 If VerbaCore saves you time

Please **[⭐ star the repo](https://github.com/Networkdog/verbacore)** — it's the single most effective thing you can do to help the project, and it takes one click. Every star helps a new person discover a faster way to look things up.

[![Star History Chart](https://api.star-history.com/svg?repos=Networkdog/verbacore&type=Date)](https://star-history.com/#Networkdog/verbacore&Date)

---

## 📜 License

Released under the [MIT License](LICENSE). Free for personal and commercial use.
Copyright © 2025–2026 **Networkdog**.

## 🙏 Acknowledgments

Built on the shoulders of great open source:

- [WPF-UI](https://github.com/lepoco/wpfui) — Fluent Design controls for WPF
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — Modern MVVM toolkit
- [Markdig.Wpf](https://github.com/Kryptos-FR/markdig.wpf) — Markdown rendering for WPF
- [NHotkey](https://github.com/thomaslevesque/NHotkey) — Global hotkey management
- [Inno Setup](https://jrsoftware.org/isinfo.php) — Windows installer toolchain

<div align="center">
<sub>Built with ❤️ for everyone who's tired of context-switching just to look up a word.</sub>
</div>
