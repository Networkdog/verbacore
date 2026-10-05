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
| 🅰️ **CapsLock Hold** *(EnsoMode)* | Hold → type with native IME support → release to look up. Big, focused overlay. |
| 🅰️ **CapsLock Tap** *(PersistentMode)* | Quick-tap (<0.5s) opens a persistent input box with full IME support. |
| 🔥 **Global Hotkey** | `Ctrl+Alt+V` (customizable) from anywhere. |
| 🖱️ **Cursor Text Grab** | Capture the foreground app's selection without the clipboard using UIA, native Office APIs, and IAccessible2. VS Code may require accessibility support to be On. |

Selection support depends on the source application. Word and Excel have native
document-window readers; PowerPoint text and classic Outlook's Word editor also
have native-object paths. For VS Code, set **Editor: Accessibility Support** to
**On** (`"editor.accessibilitySupport": "on"`). VerbaCore never changes that setting
automatically. See [selection compatibility and verified limits](docs/selected-text-compatibility.md)
for tested apps, unsupported surfaces, and regression commands.

### Keyboard-first by design

| Shortcut | Action |
|---|---|
| `CapsLock` *(hold)* | Open overlay → look up on release |
| `CapsLock` *(tap < 0.5s)* | Toggle PersistentMode |
| Right `Alt` / Hangul key | Korean/English switching in either input mode, according to the installed Windows keyboard layout |
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
- **🧠 Model-default requests** — no model-name guessing or fixed `temperature`; explicit protocol, reasoning, role, and token options for Foundry and other providers.
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
| **Microsoft Foundry** | `api-key` or `x-api-key`, by protocol | Resource endpoint + deployment name; Chat Completions or Anthropic Messages |
| **Anthropic** | `x-api-key` | Native Claude API with `content_block_delta` SSE |
| **Google Gemini** | Bearer token | Via OpenAI-compatible `generativelanguage.googleapis.com` |
| **OpenRouter** | Bearer token | 100+ models behind a single key |
| **Custom** | Bearer token or `x-api-key`, by protocol | Chat Completions or Anthropic Messages endpoint |

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
| Provider | OpenAI | OpenAI · Azure OpenAI · Microsoft Foundry · Anthropic · Gemini · OpenRouter · Custom |
| Model | `gpt-4o-mini` | Cost-effective; switch to `gpt-4o` or Claude for higher quality |
| API protocol | Chat Completions | Select Anthropic Messages for Claude through Foundry or a compatible custom endpoint |
| Reasoning options | Model defaults | Advanced: explicit `reasoning_effort`, `thinking`, or Claude adaptive/budgeted thinking; verify model/version support |
| Output token parameter | Model defaults | Chat: omit, `max_tokens`, or `max_completion_tokens`; Messages requires `max_tokens` (8192 by default) |
| Instruction role | `system` | Advanced Chat Completions choice; independent of reasoning settings |
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

### Microsoft Foundry

Choose **Microsoft Foundry**, enter your resource endpoint (for example,
`https://your-resource.services.ai.azure.com`), resource API key, and deployment name.
Select **OpenAI Chat Completions** for compatible GPT/DeepSeek/Kimi deployments or
**Anthropic Messages** for Claude. Leave advanced options at **Model defaults** unless
the deployed model's documentation requires an override. Model defaults do not mean
reasoning is disabled. Entra ID-only deployments are not supported by this API-key client.

See [Foundry API research and compatibility design](docs/foundry-api-compatibility.md)
for protocol differences, parameter constraints, migration behavior, and limitations.

---

## 🛠️ Build From Source

**Prerequisites:** [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), Windows 10/11.

```powershell
git clone https://github.com/Networkdog/verbacore.git
cd verbacore
dotnet run --project src/VerbaCore/VerbaCore.csproj
```

### API request regression checks

```powershell
dotnet run --project tests/VerbaCore.PopupTests/VerbaCore.PopupTests.csproj -c Release -- --api-requests
```

Checks all provider protocols, model-default requests, explicit options, SSE errors,
incomplete responses, cancellation, settings serialization, and cache boundaries using
fake HTTP handlers. No network, API key, keyboard hooks, or interactive desktop is needed.

### Settings UI regression checks

```powershell
dotnet run --project tests/VerbaCore.PopupTests/VerbaCore.PopupTests.csproj -c Release -- --settings
```

Renders the real settings control in an offscreen FluentWindow for four languages and
two widths, checks option bindings, arbitrary deployment names, and invalid-save
rejection, and writes temporary PNGs. Does not write user settings or call AI services.

### Quick-tap focus regression

```powershell
dotnet run --project tests/VerbaCore.PopupTests/VerbaCore.PopupTests.csproj -c Release -- --focus
```

Requires an unlocked interactive desktop. The isolated probe switches between a
separate foreground window and the real popup, verifies native/WPF keyboard focus,
and types a test digit without mouse clicks. It also tests delayed input readiness,
stale focus requests, cancellation on hide, and the guarded left-Alt recovery pair.
Windows foreground-lock policy can vary; the probe checks actual focus and key
delivery rather than assuming a lock request guarantees activation denial.
No user settings, history, or AI requests are written.

### Popup regression checks

Run on an unlocked Windows desktop:

```powershell
dotnet run --project tests/VerbaCore.PopupTests/VerbaCore.PopupTests.csproj -c Release
```

The isolated harness uses synthetic CapsLock events, a separate foreground window, and
forced working-set eviction. It checks native window reuse, foreground focus, WPF text
composition, idle preparation, shutdown, and a 250ms first-render-event budget. It does
not load or save user settings/history, install a keyboard hook, or call an AI API.

For the dedicated input-thread regression, first exit any running VerbaCore instance:

```powershell
dotnet run --project tests/VerbaCore.PopupTests/VerbaCore.PopupTests.csproj -c Release -- --input-hooks
```

This separate mode temporarily installs real hooks. It checks 400 repeated CapsLock
messages, cancellation until physical key release, native Alt/Hangul/IME key routing,
and mouse delivery while the caller does not pump UI messages. The mouse probe sends
32 paired one-pixel moves before and after enabling the app's mouse hook; it does not
click. An interactive desktop is required. Hooks are removed when the test exits.

For a locked or unattended session:

```powershell
dotnet run --project tests/VerbaCore.PopupTests/VerbaCore.PopupTests.csproj -c Release -- --offscreen
```

Offscreen mode checks layout, window reuse, idle preparation, hold-text submission to
a no-network test double, cancellation, stale gesture isolation, and outside-click
dismissal. It writes a PNG to the temporary directory. `UI-ready` measures dispatched UI preparation; `offscreen-render`
includes CPU bitmap rasterization. Neither measures pixels reaching the display, so the
first-render budget and foreground/input checks are explicitly skipped in this mode.
Real long-idle/resume behavior, physical CapsLock handling, IME composition, and virtual
desktop switching still require interactive verification. The probe cannot guarantee
latency under all Windows paging or scheduling conditions.

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
| AI | `HttpClient` + SSE streaming + `Utf8JsonReader` (7 providers; Chat Completions / Messages) |
| Markdown | [Markdig.Wpf](https://github.com/Kryptos-FR/markdig.wpf) |
| Hotkeys | [NHotkey.Wpf](https://github.com/thomaslevesque/NHotkey) |
| Text grab | COM UIA3 + MSAA/IAccessible2 + Office native object model; clipboard-free |
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
│   ├── OpenAiService.cs       # 7-provider protocol-aware requests + SSE parsing
│   ├── PromptBuilder.cs       # Mode-specific prompts + AutoMode selection
│   ├── SettingsService.cs     # JSON + DPAPI (source-generated)
│   ├── HistoryService.cs      # 200-item history + debounced save
│   ├── HotkeyService.cs       # NHotkey global hotkey lifecycle
│   ├── CursorTextService.cs   # Captured-window selected text, MTA worker and cancellation
│   ├── OfficeSelectionReader.cs # Native Office document/cell/text selections
│   └── AccessibleSelectionReader.cs # MSAA/IAccessible2 selected ranges
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
    ├── SelectionInterop.cs    # Window/focus capture, native OM and IAccessible2 interop
    └── Converters.cs          # XAML value converters

  tests/VerbaCore.PopupTests/
  ├── VerbaCore.PopupTests.csproj # Standalone Windows popup regression harness
  └── Program.cs                 # Popup, API contract, settings UI, and cache regression checks

  docs/
  ├── foundry-api-compatibility.md # API research, settings, and compatibility boundaries
  └── selected-text-compatibility.md # Clipboard-free selection support and validation
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
