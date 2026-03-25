# VerbaCore

**경량 AI 사전 & 번역 앱** — Windows 데스크탑용

단어나 문장을 입력하면 OpenAI API를 통해 **사전 조회**, **번역**, **문법 분석** 결과를 실시간 스트리밍으로 표시합니다.

## 주요 기능

- **3가지 조회 모드**: 사전 (정의/발음/예문), 번역, 문법 분석
- **5가지 입력 방식**:
  - ⌨️ 키보드 직접 입력
  - 🔥 글로벌 단축키 (Ctrl+Alt+V)로 어디서나 빠른 실행
  - 🎤 음성 입력 (마이크)
  - 🖱 마우스 커서 아래 텍스트 자동 추출
  - 📋 클립보드 감시 (텍스트 복사 시 자동 조회)
- **Windows 11 Mica 배경**: 반투명 유리 효과의 현대적 UI
- **다크/라이트 테마**: 시스템 설정 연동
- **다국어 지원**: 영어, 한국어, 일본어, 중국어 등 15개 언어
- **조회 히스토리**: 이전 조회 기록 검색 및 재조회
- **API Key 보안**: DPAPI 암호화 저장

## 기술 스택

| 구성요소 | 기술 |
|----------|------|
| 언어 | C# 12 / .NET 8 |
| UI | WPF + WPF-UI (Fluent Design) |
| AI | OpenAI GPT-4o-mini / GPT-4o |
| 아키텍처 | MVVM (CommunityToolkit.Mvvm) |
| 단축키 | NHotkey.Wpf |
| 음성 | System.Speech.Recognition |

## 빌드 & 실행

### 필수 조건

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 7 이상 (WPF 앱)

### Debug 빌드

```bash
dotnet build src/VerbaCore/VerbaCore.csproj
dotnet run --project src/VerbaCore/VerbaCore.csproj
```

### Release 빌드

```bash
dotnet build src/VerbaCore/VerbaCore.csproj -c Release
```

출력 경로: `src/VerbaCore/bin/Release/net8.0-windows7.0/`

## Installer 패키징

### 필수 조건

- [Inno Setup 6](https://jrsoftware.org/isdl.php) 설치
- Release 빌드 완료

### 1단계: Self-Contained 단일 실행 파일 발행

```bash
dotnet publish src/VerbaCore/VerbaCore.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish-standalone
```

이 명령은 .NET 런타임을 포함한 단일 `VerbaCore.exe` 파일을 `publish-standalone/` 폴더에 생성합니다.

### 2단계: Inno Setup으로 설치 파일 생성

```bash
iscc installer.iss
```

또는 Inno Setup GUI에서 `installer.iss`를 열고 **Compile**을 실행합니다.

생성된 설치 파일: `installer-output/VerbaCore-Setup-1.0.0.exe`

### Installer 옵션

| 옵션 | 설명 |
|------|------|
| 설치 경로 | `%LocalAppData%\Programs\VerbaCore` (사용자별, 관리자 권한 불필요) |
| 바탕화면 바로가기 | 선택 가능 (기본: 해제) |
| 시작 시 자동 실행 | 선택 가능 (레지스트리 `HKCU\...\Run` 등록) |
| 언어 | 한국어, 영어 |
| 제거 | 프로그램 제거 시 `%LocalAppData%\VerbaCore` 데이터도 함께 삭제 |

## 설정

첫 실행 후 **⚙ 설정** 탭에서 OpenAI API Key를 입력하세요.

- API Key: [OpenAI Platform](https://platform.openai.com/api-keys)에서 발급
- 기본 모델: `gpt-4o-mini` (비용 효율적)
- 기본 단축키: `Ctrl+Alt+V`

## 스크린샷

*(추후 추가)*

## 라이선스

MIT
