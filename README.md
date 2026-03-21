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

```bash
cd src/VerbaCore
dotnet build
dotnet run
```

## 설정

첫 실행 후 **⚙ 설정** 탭에서 OpenAI API Key를 입력하세요.

- API Key: [OpenAI Platform](https://platform.openai.com/api-keys)에서 발급
- 기본 모델: `gpt-4o-mini` (비용 효율적)
- 기본 단축키: `Ctrl+Alt+V`

## 스크린샷

*(추후 추가)*

## 라이선스

MIT
