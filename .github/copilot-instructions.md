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
3. **Tab** → cycle through modes (Dictionary / Translate)
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

## Element Glossary (공식 명칭)
모든 기능, 창, 모듈, 단축키 등에는 아래 공식 명칭을 사용한다. 대화에서 이 이름을 기준으로 소통하며, 새로운 요소가 추가될 때도 반드시 이름을 부여하고 이 섹션에 등록한다.

### Windows & Overlays
| 명칭 | 클래스 | 설명 |
|------|--------|------|
| **Overlay** | `OverlayWindow` | 투명 전체화면 오버레이. Enso 스타일 입력 & 결과 표시 |
| **SettingsPanel** | `SettingsWindow` | FluentWindow(Mica) 기반 설정 + 히스토리 창 |
| **LegacyWindow** | `MainWindow` | [미사용] 탭 기반 초기 UI |

### Input Modes (입력 모드)
| 명칭 | 설명 |
|------|------|
| **EnsoMode** | CapsLock을 누른 채 타이핑 → 뗄 때 조회. 자동으로 사라지는 일시적 모드 |
| **PersistentMode** | CapsLock 퀵탭(0.5초 미만)으로 진입. TextBox가 열리고, Enter로 조회. 수동으로 닫기 전까지 유지 |

### Lookup Modes (조회 모드)
| 명칭 | Enum | 설명 |
|------|------|------|
| **DictMode** | `LookupMode.Dictionary` | 단어 사전 모드 — 어원·IPA·한국어 발음·용례 |
| **TransMode** | `LookupMode.Translate` | 번역 모드 — 번역 + 뉘앙스 노트 |
| **AutoMode** | (logic in `PromptBuilder`) | 입력 ≤3단어 → DictMode, >3단어 → TransMode 자동 선택 |

### Keyboard Shortcuts (단축키)
| 명칭 | 키 | 동작 |
|------|------|------|
| **EnsoHold** | `CapsLock` (길게) | Overlay 열기 + EnsoMode 진입, 뗄 때 Lookup 실행 |
| **QuickTap** | `CapsLock` (짧게, <0.5s) | PersistentMode 토글 |
| **ModeSwitch** | `Tab` | DictMode ↔ TransMode 순환 |
| **DeleteChar** | `Backspace` | 입력 버퍼에서 마지막 문자 삭제 |
| **Dismiss** | `Escape` | Overlay 닫기 / 조회 취소 |
| **SubmitInput** | `Enter` | PersistentMode에서 조회 실행 |
| **CopyResult** | `Ctrl+C` | 결과 텍스트를 클립보드로 복사 |
| **GlobalHotkey** | `Ctrl+Alt+V` (기본값, 변경 가능) | 어디서든 PersistentMode 열기 |

### Services (서비스)
| 명칭 | 클래스 | 설명 |
|------|--------|------|
| **KeyHook** | `CapsLockService` | 저수준 키보드 후킹. EnsoHold/QuickTap 감지, 버퍼 관리 |
| **AiEngine** | `OpenAiService` | LLM 스트리밍 호출 (6개 Provider 지원) |
| **PromptEngine** | `PromptBuilder` | 모드별 시스템/유저 프롬프트 생성 + AutoMode 판정 |
| **ConfigStore** | `SettingsService` | JSON 설정 로드/저장, DPAPI 암호화 |
| **HistoryStore** | `HistoryService` | JSON 히스토리 저장 (최대 200건) |
| **TextGrabber** | `CursorTextService` | UI Automation으로 포커스된 앱의 선택 텍스트 추출 |
| **VoiceInput** | `SpeechInputService` | System.Speech 음성 인식 입력 |
| **GlobalHotkeyService** | `HotkeyService` | NHotkey 기반 전역 단축키 등록/해제 |

### Models (데이터 모델)
| 명칭 | 클래스 | 설명 |
|------|--------|------|
| **Config** | `AppSettings` | 앱 전체 설정 (Provider, 키, 모델, 테마, 위치 등) |
| **LookupResult** | `LookupResult` | 단일 조회 결과 (입력, 모드, 응답, 스트리밍 여부) |
| **HistoryItem** | `LookupHistoryItem` | 히스토리 항목 (ID, 입력, 응답, 타임스탬프) |

### Enums
| 명칭 | Enum | 값 |
|------|------|------|
| **Provider** | `ApiProvider` | `OpenAI`, `AzureOpenAI`, `Anthropic`, `Google`, `OpenRouter`, `Custom` |
| **Position** | `OverlayPosition` | `TopLeft`~`BottomRight` (9방위) |
| **Theme** | `ThemeMode` | `System`, `Light`, `Dark` |

### UI Regions (Overlay 내부 영역)
| 명칭 | 설명 |
|------|------|
| **ModeLabel** | Overlay 상단 — 현재 모드 아이콘 + 이름 (📖 Dictionary / 🔄 Translate) |
| **LangIndicator** | Overlay 상단 — 소스→타겟 언어 표시 |
| **HintLabel** | Overlay — "Tab: 모드 전환 · Esc: 닫기" 등 힌트 텍스트 |
| **InputDisplay** | Overlay — EnsoMode에서 타이핑 중인 큰 글자 표시 영역 |
| **InputBox** | Overlay — PersistentMode에서 IME TextBox 입력 영역 |
| **BlinkingCursor** | Overlay — EnsoMode 입력 시 깜빡이는 커서 |
| **ResultViewer** | Overlay — Markdown 결과 표시 영역 (FlowDocumentScrollViewer) |
| **StatusBar** | Overlay — 하단 상태 메시지 ("로딩 중...", "복사됨" 등) |
| **LoadingSpinner** | Overlay — API 호출 중 표시되는 로딩 인디케이터 |

### Tray (시스템 트레이)
| 명칭 | 설명 |
|------|------|
| **TrayIcon** | 시스템 트레이 아이콘 (더블클릭 → SettingsPanel 열기) |
| **TrayMenu** | 우클릭 컨텍스트 메뉴 |
| **TrayMenu.Settings** | "⚙ 설정" 항목 → SettingsPanel 열기 |
| **TrayMenu.About** | "VerbaCore 정보" 항목 → About 대화상자 |
| **TrayMenu.Exit** | "종료" 항목 → 앱 종료 |

### Core Actions (핵심 동작)
| 명칭 | 설명 |
|------|------|
| **Lookup** | 입력 텍스트를 AiEngine에 보내 결과를 스트리밍으로 받는 동작 |
| **AutoHide** | 결과 표시 후 120초 뒤 Overlay를 자동으로 숨기는 동작 |
| **TextGrab** | CapsLock 누를 때 포커스된 앱의 선택 텍스트를 자동으로 가져오는 동작 |
| **StreamRender** | AiEngine 응답을 200ms 쓰로틀로 Markdown 렌더링하는 동작 |

### Naming Convention (명명 규칙)
새 기능/요소 추가 시 반드시:
1. 위 표에 등록
2. PascalCase 영문 명칭 사용
3. 대화에서는 **볼드**로 명칭을 참조 (예: "**Overlay**에서 **ModeSwitch** 동작을...")
4. 코드 클래스/메서드명과 명칭이 다를 수 있음 — 명칭은 커뮤니케이션용, 클래스명은 코드용

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
- 디버깅할 때 이미 실행 중인 인스턴스가 있으면 키보드 후킹이 꼬일 수 있으니, 강제로 `VerbaCore.exe` 프로세스를 완전히 종료한 후 디버깅 시작할 것.
