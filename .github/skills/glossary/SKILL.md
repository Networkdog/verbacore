# VerbaCore — Element Glossary

This document defines the official names for all features, windows, modules, and shortcuts in the VerbaCore project.
Use these names when communicating. When adding new elements, always assign a name and register it here.

## Windows & Overlays
| Name | Class | Description |
|------|-------|-------------|
| **Overlay** | `OverlayWindow` | Transparent fullscreen overlay for input & results |
| **SettingsPanel** | `SettingsWindow` | FluentWindow(Mica) settings + history panel |

## Input Modes
| Name | Description |
|------|-------------|
| **EnsoMode** | Hold CapsLock + type through the native IME TextBox → lookup on release. Ephemeral mode |
| **PersistentMode** | Quick-tap CapsLock (<0.5s) to open TextBox + Enter to look up. Stays open until dismissed |

## Lookup Modes
| Name | Enum | Description |
|------|------|-------------|
| **DictMode** | `LookupMode.Dictionary` | Word dictionary — etymology, IPA, pronunciation, examples |
| **TransMode** | `LookupMode.Translate` | Translation — translation + nuance notes |
| **AssistMode** | `LookupMode.Assist` | Assistant — explains code/URL/formula/non-language content |
| **AutoMode** | `PromptBuilder.AutoSelectMode()` | ≤3 words → Dict, >3 words → Trans, non-language → Assist |

## Keyboard Shortcuts
| Name | Key | Action |
|------|-----|--------|
| **EnsoHold** | `CapsLock` (hold ≥0.5s) | Open Overlay + EnsoMode, Lookup on release |
| **QuickTap** | `CapsLock` (tap <0.5s) | Toggle PersistentMode |
| **ImeSwitch** | Right `Alt` / Hangul key (Windows keyboard layout) | Korean/English switching in EnsoMode and PersistentMode |
| **ModeSwitch** | `Tab` | Cycle DictMode → TransMode → AssistMode |
| **DeleteChar** | `Backspace` | Delete last character from input buffer |
| **Dismiss** | `Escape` | Close Overlay / cancel lookup |
| **SubmitInput** | `Enter` | Execute lookup in PersistentMode |
| **CopyResult** | `Ctrl+C` | Copy result text to clipboard |
| **GlobalHotkey** | `Ctrl+Alt+V` (customizable) | Open PersistentMode from any app |

## Services
| Name | Class | Description |
|------|-------|-------------|
| **KeyHook** | `CapsLockService` | Dedicated keyboard/mouse hook thread, EnsoHold/QuickTap detection, pre-focus buffer management |
| **AiEngine** | `OpenAiService` | 7-provider protocol-aware requests, validation, responses, and SSE parsing |
| **PromptEngine** | `PromptBuilder` | Mode-specific prompt generation + AutoMode selection |
| **ConfigStore** | `SettingsService` | JSON settings + DPAPI encryption (source-generated) |
| **HistoryStore** | `HistoryService` | JSON history + debounced save (source-generated) |
| **TextGrabber** | `CursorTextService` | Clipboard-free captured-window selection with UIA, native Office, and IAccessible2 on a bounded MTA worker |
| **OfficeSelection** | `OfficeSelectionReader` | Native document-window selection for Word/Outlook editor, Excel cells, and PowerPoint text |
| **AccessibleSelection** | `AccessibleSelectionReader` | MSAA/IAccessible2 selected ranges, including Chromium renderer discovery |
| **GlobalHotkeyService** | `HotkeyService` | NHotkey global hotkey registration/unregistration |

## Models
| Name | Class | Description |
|------|-------|-------------|
| **Config** | `AppSettings` | App-wide settings (provider, key, model, theme, position, size) |
| **LookupResult** | `LookupResult` | Single lookup result + `LookupMode` enum |
| **HistoryItem** | `LookupHistoryItem` | History entry (ID, input, response, timestamp) |
| **JsonContexts** | `SettingsJsonContext`, `HistoryJsonContext`, `ApiJsonContext` | Source-generated JSON contexts |

## Enums
| Name | Enum | Values |
|------|------|--------|
| **Provider** | `ApiProvider` | `OpenAI`, `AzureOpenAI`, `Anthropic`, `Google`, `OpenRouter`, `Custom`, `Foundry` |
| **ApiProtocol** | `InferenceProtocol` | `ChatCompletions`, `AnthropicMessages` |
| **ReasoningOptions** | `ReasoningMode` | `ModelDefault`, `OpenAiEffort`, `ThinkingEnabled`, `ThinkingDisabled`, `AnthropicAdaptive`, `AnthropicBudgeted` |
| **InstructionRole** | `InstructionRole` | `System`, `Developer`, `User` |
| **TokenParameter** | `OutputTokenParameter` | `ModelDefault`, `MaxCompletionTokens`, `MaxTokens` |
| **Position** | `OverlayPosition` | `TopLeft` through `BottomRight` (9 positions) |
| **Size** | `OverlaySize` | `Small`, `Medium`, `Large` |
| **Theme** | `ThemeMode` | `System`, `Light`, `Dark` |
| **Mode** | `LookupMode` | `Dictionary`, `Translate`, `Assist` |

## API Settings
| Name | Binding | Description |
|------|---------|-------------|
| **ApiProtocol** | `Protocol` | Explicit Foundry/Custom request and response format |
| **RequestOptions** | Advanced request options Expander | Collapsed by default; model-specific overrides |
| **ReasoningOptions** | `SelectedReasoningMode`, `ReasoningEffort` | Explicit reasoning schema and optional effort |
| **TokenLimit** | `TokenLimitParameter`, `MaxOutputTokens` | Optional Chat limit or required Messages limit |
| **ThinkingBudget** | `ThinkingBudgetTokens` | Budgeted Claude thinking, below the output limit |
| **InstructionRole** | `SelectedInstructionRole` | Independent Chat Completions instruction role |

## UI Regions (Overlay)
| Name | x:Name | Description |
|------|--------|-------------|
| **ModeLabel** | `ModeLabel` | Top — current mode icon + name |
| **HintLabel** | `HintLabel` | "Tab: switch mode" hint text |
| **InputDisplay** | `InputDisplay` | Non-editable input display and layout preparation |
| **InputBox** | `InputTextBox` | Shared EnsoMode/PersistentMode IME TextBox with native caret |
| **BlinkingCursor** | `BlinkingCursor` | Legacy non-editable input cursor; hidden while the IME TextBox is active |
| **ResultViewer** | `ResultViewer` | Markdown result display (FlowDocumentScrollViewer) |
| **StatusBar** | `StatusLabel` | Bottom status message |
| **LoadingSpinner** | `LoadingPanel` | Loading indicator during API calls |

## Tray (System Tray)
| Name | Description |
|------|-------------|
| **TrayIcon** | System tray icon (double-click → SettingsPanel) |
| **TrayMenu** | Right-click context menu |
| **TrayMenu.Settings** | "Settings" → open SettingsPanel |
| **TrayMenu.About** | "About VerbaCore" → about dialog |
| **TrayMenu.Exit** | "Exit" → terminate app |

## Core Actions
| Name | Description |
|------|-------------|
| **Lookup** | Send input to AiEngine and receive SSE streaming result |
| **AutoHide** | Auto-hide Overlay 120s after result display |
| **TextGrab** | Auto-extract selected text from focused app on CapsLock press |
| **StreamRender** | 200ms-throttled plain-text rendering (FlowDocument reuse) → final Markdown render on completion |

## Naming Convention
1. All new elements must be registered in the tables above
2. Use PascalCase English names
3. Reference names in **bold** in conversations (e.g., "In **Overlay**, the **ModeSwitch** action...")
4. Code class names may differ from glossary names — glossary names are for communication
