# Foundry Inference API Compatibility

Research date: 2026-10-04. This document distinguishes published API contracts,
implemented client behavior, and behavior that still needs a deployed-model test.
Model catalogs and vendor documentation change independently of this application.

## Recommendation

Treat **hosting provider**, **wire protocol**, and **model request options** as
separate decisions. A deployment named `production` conveys no reliable model
capabilities. Extending a `gpt-5`/`gpt-6` prefix list cannot solve this problem.

Use the smallest supported text request by default. Add optional parameters only
when the selected deployment's model/version documents them. Do not copy every
parameter from a quickstart, and do not assume an SDK validates model capabilities.

## Research Findings

| Model/API family | Foundry resource route | Request and response differences |
| --- | --- | --- |
| OpenAI-compatible Chat Completions, including applicable DeepSeek and Kimi deployments | `/openai/v1/chat/completions` | `model` is a deployment name. Uses `messages` and `choices[0].message.content`; streaming uses `choices[0].delta.content`. v1 GA does not require a dated `api-version`. Parameter support still depends on the model. [1][2][3] |
| Anthropic Claude Messages | `/anthropic/v1/messages` | Uses `x-api-key` and `anthropic-version: 2023-06-01` for API-key authentication. `system` is top-level and `max_tokens` is required. Non-streaming text is in `content[]` blocks, not `choices`. [4][5] |
| Claude thinking | Messages route | Budgeted thinking uses `thinking.type=enabled` plus `budget_tokens >= 1024` and below `max_tokens`. Adaptive thinking uses `thinking.type=adaptive`; explicit effort belongs under `output_config.effort`. Supported thinking modes and effort values vary by Claude version. Newer models can reject sampling overrides altogether. [5][6] |
| Kimi | Chat Completions route for cataloged Foundry chat models | The Azure catalog identifies the chat-completion capability, but does not establish one shared optional-parameter schema for all versions. Moonshot's current upstream reference demonstrates fixed sampling and different reasoning controls across versions. For example, K2.6 uses `thinking` and fixed temperature depending on mode; K2.7 Code has stricter thinking controls; K3 uses `reasoning_effort`. Upstream availability or parameter support is not proof of Azure deployment support. [2][7] |
| DeepSeek | Chat Completions route | Microsoft's reasoning guidance warns against assuming support for `temperature`, `top_p`, and penalties. Final `content` is distinct from `reasoning_content`. The tutorial uses `max_tokens`, while general reasoning guidance also discusses `max_completion_tokens`; verify the actual model/API before overriding either. [3][8] |

**Foundry is not one uniform hosting or API contract.** The catalog distinguishes
models sold by Azure from partner offers. Claude also has different hosting offers.
Do not infer residency, licensing, authentication, or optional parameters solely
from a catalog label. This change adds protocol support, not a hosting guarantee.

### Why defaults are safer

- `temperature` omission lets the model use its own default. Setting it to `1`
  everywhere is not equivalent: some models reject the parameter itself.
- `reasoning_effort` is not a universal reasoning switch. Claude has another
  schema, and a Kimi or DeepSeek version may reject or constrain the field.
- `developer` is not a universal instruction role. It must not be selected merely
  because reasoning is enabled. Some integrations prefer instructions in `user`.
- Token-limit field names and budgets differ. Claude requires `max_tokens`, and
  reasoning tokens can consume the output budget before any final text appears.
- A successful HTTP status does not mean a complete answer: SSE error events,
  token-limit stops, tool requests, and broken streams require separate handling.

## Implemented Settings

Choose **Microsoft Foundry**, enter the **resource** endpoint, resource API key,
and the exact **deployment name**, then select an API protocol:

| Deployment | API protocol | Endpoint example |
| --- | --- | --- |
| Chat-compatible GPT, DeepSeek, Kimi, or other documented chat models | OpenAI Chat Completions | `https://<resource>.services.ai.azure.com` |
| Claude through Foundry | Anthropic Messages | `https://<resource>.services.ai.azure.com` |

The client builds the appropriate operation path. Matching base URLs or full
operation URLs are also accepted without appending the operation twice. Existing
**Azure OpenAI** retains its deployment-scoped, dated API route. **Custom** can
select either protocol, including an explicitly configured proxy.

### Default behavior

- No model-name parameter heuristics and no explicit `temperature`, `top_p`, or
  penalty fields.
- Chat Completions sends no output-limit or reasoning override by default.
- Messages sends its required `max_tokens`, defaulting to 8192. This is a client
  output ceiling, not an assertion about the model's maximum.
- Instructions default to `system` for Chat Completions and top-level `system`
  for Messages. `developer` and a merged single `user` message are explicit choices
  for Chat Completions.
- Changing provider, protocol, or deployment resets model-specific overrides.
  Loading settings preserves arbitrary deployment names outside the suggestion list.

### Advanced request options

| Setting | Wire behavior |
| --- | --- |
| Model defaults | Omit optional reasoning fields; this does **not** guarantee reasoning is disabled. |
| `reasoning_effort` | Chat Completions only. Sends the explicitly selected value; `default` omits it. Accepted values are a protocol-level choice list, not a promise that every model accepts every value. |
| `thinking: enabled` / `thinking: disabled` | Explicit Chat Completions extensions for deployments that document this schema. Disabled thinking is also available for Messages models that support it. No automatic vendor-based selection. |
| Claude adaptive thinking | Messages `thinking.type=adaptive`, with optional `output_config.effort`. |
| Claude budgeted thinking | Messages `thinking.type=enabled` and `budget_tokens`. Invalid budgets are rejected locally. |
| Output token parameter | Chat Completions: omit, `max_tokens`, or `max_completion_tokens`. Messages always uses `max_tokens`. |

The client rejects protocol-incompatible combinations before sending HTTP. It
cannot certify optional parameter support for a particular remote deployment.
For example, a model requiring `thinking.keep=all` should use model defaults here;
the UI does not expose every vendor-specific extension.

Legacy `ReasoningEffort="none"` meant omission and keeps that meaning when no new
reasoning-mode setting exists. An explicit new `reasoning_effort` mode with `none`
actually sends `none`. Existing non-empty legacy efforts are preserved for
Chat Completions; their instruction role is no longer automatically changed.

## Error and State Handling

- HTTP 400 reports request/model-option errors; 401, 403, and 404 retain distinct
  authentication, authorization, and endpoint/deployment guidance. Server error
  message, type, code, and parameter are retained when available.
- There is no blind retry that removes options or changes protocols. A failed
  stream is not replayed after partial text has been displayed.
- SSE frames support multiple `data:` lines and both protocol termination forms.
  Unknown events are skipped, but error events and malformed data fail explicitly.
- Claude thinking/tool blocks and Chat Completions `reasoning_content` are not
  rendered as final dictionary or translation content.
- Empty final text, token-limit stops, unsupported tool/continuation stops, and
  premature stream termination fail before the lookup is saved as successful.
- Cache identity includes endpoint, protocol, deployment, instruction role,
  reasoning configuration, token settings, languages, mode, and normalized input.
  It contains no API key. Older cache identities are not reused for these requests.
- Settings use the existing source-generated JSON and DPAPI flow. Invalid options
  do not mutate the active settings or write the settings file.

## Scope and Next Step

This implementation covers API-key-authenticated resource endpoints and single-turn
text/SSE requests. It does not add Microsoft Entra ID sign-in, project-endpoint token
authentication, Responses API, tool execution, or multimodal requests. A model or
resource that requires Entra ID cannot be used merely by placing a token in the
API Key field. Claude's Foundry documentation explicitly identifies Entra-only
models; consult the current model page. [4]

No Azure deployment, subscription modification, customer credential use, or paid
inference call was performed. Local HTTP fixtures prove request/response contracts,
not regional availability, authorization, quota, or live model compatibility.

For further automation, add an opt-in deployment profile keyed by the **actual
publisher/model/version and API operation**, with a versioned capability record
and a user-triggered minimal connection test. Use deployment metadata as identity,
not a guessed prefix. Unknown versions should retain conservative defaults, not
inherit every capability of an older model. Official SDKs can simplify richer
protocol support, but do not replace this capability policy.

## Validation

```powershell
dotnet run --project tests/VerbaCore.PopupTests/VerbaCore.PopupTests.csproj -c Release -- --api-requests
dotnet run --project tests/VerbaCore.PopupTests/VerbaCore.PopupTests.csproj -c Release -- --settings
```

The first mode uses fake HTTP handlers and source-generated settings snapshots.
The second renders the real settings control in an offscreen FluentWindow across
four languages and two widths, checks binding errors and invalid-save rejection,
and writes PNGs to the temporary directory. Neither writes user settings or calls
an AI service. This does not replace a live deployment smoke test.

## Sources

1. [Microsoft: OpenAI v1 API and non-OpenAI model support](https://learn.microsoft.com/en-us/azure/foundry/openai/api-version-lifecycle)
2. [Microsoft: Foundry Models sold by Azure](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure)
3. [Microsoft: Use reasoning models](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/how-to/use-chat-reasoning)
4. [Microsoft: Deploy and use Claude in Foundry](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/how-to/use-foundry-models-claude)
5. [Anthropic: Create a Message](https://platform.claude.com/docs/en/api/messages/create)
6. [Anthropic: Streaming messages](https://platform.claude.com/docs/en/build-with-claude/streaming)
7. [Moonshot: Model parameter reference](https://platform.kimi.ai/docs/api/models-overview)
8. [Microsoft: DeepSeek reasoning tutorial](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/tutorials/get-started-deepseek-r1)
9. [Microsoft: Partner model catalog and Claude hosting notes](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-from-partners)