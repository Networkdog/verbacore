# VerbaCore — Copilot History Log

## 2026-03-21: Initial Project Setup & Full Implementation

### Task
Windows 데스크탑용 경량 AI 사전/번역 앱(VerbaCore) 기획 및 전체 구현

### Requirements (사용자 요구사항)
1. 단어/문장 입력 → 생성형 AI 프롬프트 → 결과 표시
2. 경량 설계, 빠른 실행속도
3. 다양한 입력 방식: 키보드, 음성, 마우스 커서 텍스트, 클립보드 감시, 글로벌 단축키
4. Windows 데스크탑에 자연스럽게 녹아드는 UI
5. 반투명 Mica 배경, 테두리 없는 윈도우 디자인
6. OpenAI API (GPT-4o-mini 기본)
7. 영어 ↔ 한국어 + 다국어 지원

### Decisions Made
| Decision | Rationale |
|----------|-----------|
| C# + WPF (.NET 8) | 네이티브 Windows 통합, 빠른 실행 |
| WPF-UI (Wpf.Ui 3.x) | Windows 11 Fluent Design, Mica backdrop |
| HttpClient 직접 사용 (OpenAI SDK 미사용) | 종속성 최소화, 경량화 |
| System.Speech.Recognition | .NET 8 내장, 추가 패키지 불필요 |
| NHotkey.Wpf | WPF/MVVM 통합이 우수한 글로벌 단축키 |
| UI Automation (OCR 미사용) | Tesseract OCR은 100MB+ 추가, 경량화 목표에 반함 |
| JSON 파일 (SQLite 미사용) | 설치 부담 감소, 소량 데이터 |
| DPAPI 암호화 | API Key 안전 저장 |

### Files Created
- `src/VerbaCore/VerbaCore.csproj` — 프로젝트 파일 (NuGet: WPF-UI, CommunityToolkit.Mvvm, NHotkey.Wpf, System.Speech, Microsoft.Extensions.*)
- `src/VerbaCore/App.xaml` / `App.xaml.cs` — DI 컨테이너, WPF-UI 테마, 글로벌 에러 핸들링
- `src/VerbaCore/MainWindow.xaml` / `MainWindow.xaml.cs` — FluentWindow, Mica backdrop, 탭 네비게이션, 단축키/클립보드 훅
- `src/VerbaCore/Models/AppSettings.cs` — 설정 모델 (API Key, 모델, 언어, 테마 등)
- `src/VerbaCore/Models/LookupResult.cs` — 조회 결과 모델 + LookupMode enum
- `src/VerbaCore/Models/LookupHistory.cs` — 히스토리 모델
- `src/VerbaCore/Services/SettingsService.cs` — JSON 기반 설정 저장/로드, DPAPI 암호화
- `src/VerbaCore/Services/PromptBuilder.cs` — 3가지 모드별 프롬프트 템플릿 (사전/번역/분석)
- `src/VerbaCore/Services/OpenAiService.cs` — OpenAI Chat Completions API (SSE 스트리밍)
- `src/VerbaCore/Services/HistoryService.cs` — JSON 기반 히스토리 저장/로드
- `src/VerbaCore/Services/HotkeyService.cs` — NHotkey.Wpf 글로벌 단축키 (Ctrl+Alt+V)
- `src/VerbaCore/Services/ClipboardMonitorService.cs` — Win32 AddClipboardFormatListener
- `src/VerbaCore/Services/SpeechInputService.cs` — System.Speech 음성 인식
- `src/VerbaCore/Services/CursorTextService.cs` — UI Automation 커서 텍스트 추출
- `src/VerbaCore/ViewModels/MainViewModel.cs` — 메인 화면 로직 (입력→프롬프트→API→결과)
- `src/VerbaCore/ViewModels/SettingsViewModel.cs` — 설정 페이지 로직  
- `src/VerbaCore/ViewModels/HistoryViewModel.cs` — 히스토리 페이지 로직
- `src/VerbaCore/Views/MainView.xaml` / `.cs` — 메인 입력/결과 UI
- `src/VerbaCore/Views/SettingsView.xaml` / `.cs` — 설정 UI
- `src/VerbaCore/Views/HistoryView.xaml` / `.cs` — 히스토리 UI
- `src/VerbaCore/Helpers/NativeMethods.cs` — Win32 P/Invoke (클립보드, 커서, 윈도우)
- `src/VerbaCore/Helpers/Converters.cs` — XAML 값 변환기 (Mode→Appearance, Bool→Visibility 등)
- `copilot-instructions.md` — Copilot 개발 지침
- `logs/copilot-history.md` — 이 파일 (작업 이력)

### Build Status
✅ **Build Succeeded** — 0 Warnings, 0 Errors

### NuGet Packages
| Package | Version |
|---------|---------|
| WPF-UI | 3.0.5 |
| CommunityToolkit.Mvvm | 8.4.1 |
| Microsoft.Extensions.DependencyInjection | 10.0.5 |
| Microsoft.Extensions.Hosting | 10.0.5 |
| Microsoft.Extensions.Http | 10.0.5 |
| NHotkey.Wpf | 4.0.0 |
| System.Speech | 10.0.5 |

### Issues Resolved During Implementation
1. **LibraryImport requires AllowUnsafeBlocks** → DllImport으로 변경하여 해결
2. **AddHttpClient not found** → Microsoft.Extensions.Http 패키지 추가
3. **HttpRequestException missing using** → `System.Net.Http` using 추가
4. **.NET SDK not in PATH** → `DOTNET_ROOT` 환경변수 수동 설정
5. **Icon file not found** → ApplicationIcon 참조 임시 제거

---

## 2026-03-21: Azure OpenAI Support

### Task
OpenAI와 Azure OpenAI를 모두 지원하도록 앱 확장

### Changes
- `Models/AppSettings.cs`: `ApiProvider` enum 추가 (OpenAI, AzureOpenAI), Azure 전용 필드 (Endpoint, DeploymentName, ApiVersion)
- `Services/OpenAiService.cs`: `GetApiUrl()` — 프로바이더별 URL 분기, `ApplyAuth()` — OpenAI는 Bearer 토큰, Azure는 api-key 헤더
- `Services/SettingsService.cs`: Azure 설정 필드 직렬화/역직렬화
- `ViewModels/SettingsViewModel.cs`: 프로바이더 선택 UI 로직, IsAzure 플래그로 Azure 전용 필드 표시/숨김
- `Views/SettingsView.xaml`: 프로바이더 드롭다운, Azure Endpoint/DeploymentName/ApiVersion 입력 필드 (조건부 표시)
- `Helpers/Converters.cs`: `BoolToAzureKeyPlaceholderConverter` 추가
- `App.xaml`: 새 Converter 리소스 등록

### Build Status
✅ **Build Succeeded** — 0 Warnings, 0 Errors (Azure OpenAI)

---

## 2026-03-21: Enso-Style UI Redesign

### Task
Enso Launcher 스타일로 UI 전면 재설계. CapsLock 누른 상태에서 단어 입력 → 반투명 오버레이에 실시간 표시 → CapsLock 떼면 AI 조회. 메인 윈도우 제거, 시스템 트레이 아이콘으로 설정만 제공.

### Concept
- **Quasimodal 입력**: CapsLock을 누르고 있는 동안만 VerbaCore가 활성화됨
- **투명 오버레이**: 화면 중앙에 반투명 검은 배경 + 흰색 큰 텍스트로 입력 표시
- **시스템 트레이**: 설정/히스토리/정보/종료 — 메인 윈도우 없음
- **키보드 훅**: `SetWindowsHookEx(WH_KEYBOARD_LL)` 저수준 훅으로 CapsLock 토글 억제

### Files Created
- `Services/CapsLockService.cs` — 저수준 키보드 훅, CapsLock 인터셉트, 문자 버퍼링
- `OverlayWindow.xaml` / `.cs` — 투명 오버레이, fade in/out, 실시간 스트리밍 결과
- `SettingsWindow.xaml` / `.cs` — FluentWindow (Mica), 설정 + 히스토리 탭
- `GlobalUsings.cs` — WPF/WinForms 네임스페이스 충돌 해결

### Files Modified
- `Helpers/NativeMethods.cs` — 키보드 훅 Win32 API 추가
- `App.xaml` — `ShutdownMode="OnExplicitShutdown"`
- `App.xaml.cs` — 트레이 아이콘, CapsLockService, OverlayWindow
- `VerbaCore.csproj` — `UseWindowsForms=true`
- `copilot-instructions.md` — Enso 스타일 문서화

### Build Status
✅ **Build Succeeded** — 0 Warnings, 0 Errors (Enso Redesign)

---

## 2026-03-21: CapsLock 대소문자 토글 완전 비활성화

### Task
CapsLock 키의 원래 기능(대소문자 전환)을 완전히 비활성화. VerbaCore 실행 중 CapsLock은 오직 VerbaCore 활성화 용도로만 사용.

### Changes
- `Helpers/NativeMethods.cs`: `GetKeyState`, `keybd_event`, `IsCapsLockOn()`, `ToggleCapsLockOff()` 추가
- `Services/CapsLockService.cs`:
  - `Install()` 시 `ToggleCapsLockOff()` 호출 → 앱 시작 시 CapsLock 강제 OFF
  - CapsLock KeyUp 시 `EnsureCapsLockOff()` 호출 → 혹시 켜지면 즉시 OFF

### Build Status
✅ Build Succeeded

---

## 2026-03-21: CapsLock 시뮬레이션 루프 버그 수정

### 증상
`ToggleCapsLockOff()`가 `keybd_event`로 CapsLock을 시뮬레이션 → 키보드 훅이 시뮬레이션된 키를 다시 감지 → CapsLockPressed/Released 이벤트 반복 → 창이 열렸다 닫혔다 반복

### 원인
`KBDLLHOOKSTRUCT.flags`의 `LLKHF_INJECTED` 플래그 미확인

### 수정
- `NativeMethods.cs`: `LLKHF_INJECTED = 0x10` 상수 추가
- `CapsLockService.HookCallback()`: CapsLock에 `isInjected` 체크 → 시뮬레이션된 키면 즉시 억제 (이벤트 발생 안 함)
- `EnsureCapsLockOff()`: 비동기 Task.Delay 제거, 동기 호출로 단순화

### Build Status
✅ **Build Succeeded** — 0 Warnings, 0 Errors

---

## 2026-03-21: CapsLock 듀얼 모드 (퀵 탭 / 롱 프레스)

### Task
CapsLock 동작을 두 가지 모드로 분기:
- **퀵 탭** (< 0.5초, 타이핑 없음): 검색창 토글 (열기/닫기), 키보드로 직접 입력 후 Enter로 조회
- **롱 프레스** (≥ 0.5초 또는 홀드 중 타이핑): 기존 Enso 스타일 — CapsLock 떼면 조회 후 자동 사라짐

### Changes
- `Services/CapsLockService.cs`:
  - `CapsLockReleased` 이벤트 → `QuickTapReleased` + `LongPressReleased` 두 이벤트로 분리
  - `Stopwatch.GetTimestamp()` 기반 500ms 임계값으로 퀵/롱 판별
  - `_typedWhileHeld` 플래그로 홀드 중 타이핑 감지
  - `PersistentModeActive` 프로퍼티: 퀵탭 모드에서 CapsLock 없이도 키 캡처
  - `EnterPressed` 이벤트: 퍼시스턴트 모드에서 Enter로 조회 트리거
  - 퍼시스턴트 모드에서 Escape/Tab/Backspace/Space/문자 키 처리
- `OverlayWindow.xaml.cs`:
  - `_persistentMode`, `_isShown` 상태 플래그 추가
  - `OnQuickTapReleased()` — 오버레이 토글, PersistentModeActive 동기화
  - `OnLongPressReleased()` — 기존 동작 (조회 후 자동 사라짐)
  - `OnEnterPressed()` — 퍼시스턴트 모드에서 Enter 조회 처리
  - `ShowOverlay()`/`HideOverlay()`에 `_isShown`/PersistentModeActive 상태 관리

### Build Status
✅ **Build Succeeded** — 0 Warnings, 0 Errors
