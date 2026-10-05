using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VerbaCore.Models;

namespace VerbaCore.Services;

public interface IOpenAiService
{
    Task<string> GetCompletionAsync(string input, LookupMode mode, string nativeLanguage,
        string foreignLanguage, CancellationToken ct = default);

    IAsyncEnumerable<string> StreamCompletionAsync(string input, LookupMode mode,
        string nativeLanguage, string foreignLanguage, CancellationToken ct = default);
}

public sealed partial class OpenAiService : IOpenAiService
{
    private static readonly Dictionary<ApiProvider, string> ProviderUrls = new()
    {
        [ApiProvider.OpenAI] = "https://api.openai.com/v1/chat/completions",
        [ApiProvider.Anthropic] = "https://api.anthropic.com/v1/messages",
        [ApiProvider.Google] = "https://generativelanguage.googleapis.com/v1beta/chat/completions",
        [ApiProvider.OpenRouter] = "https://openrouter.ai/api/v1/chat/completions",
    };

    private readonly HttpClient _httpClient;
    private readonly SettingsService _settings;
    private readonly PromptBuilder _promptBuilder;

    public OpenAiService(HttpClient httpClient, SettingsService settings, PromptBuilder promptBuilder)
    {
        _httpClient = httpClient;
        _settings = settings;
        _promptBuilder = promptBuilder;
    }

    internal static InferenceProtocol GetProtocol(AppSettings settings) => settings.Provider switch
    {
        ApiProvider.Anthropic => InferenceProtocol.AnthropicMessages,
        ApiProvider.Foundry or ApiProvider.Custom => settings.Protocol,
        _ => InferenceProtocol.ChatCompletions
    };

    private bool IsAnthropicNative => GetProtocol(_settings.Current) == InferenceProtocol.AnthropicMessages;

    internal static ReasoningMode GetReasoningMode(AppSettings settings) => settings.ReasoningMode
        ?? (GetProtocol(settings) == InferenceProtocol.ChatCompletions
            && settings.ReasoningEffort is not (null or "" or "none" or "default")
            ? ReasoningMode.OpenAiEffort : ReasoningMode.ModelDefault);

    internal static void ValidateRequestOptions(AppSettings settings)
    {
        var messagesApi = GetProtocol(settings) == InferenceProtocol.AnthropicMessages;
        var reasoning = GetReasoningMode(settings);
        if (!Enum.IsDefined(settings.Protocol) || !Enum.IsDefined(settings.InstructionRole)
            || !Enum.IsDefined(settings.TokenLimitParameter) || !Enum.IsDefined(reasoning))
            throw new InvalidOperationException("Select valid API request options.");
        if (settings.MaxOutputTokens < 1)
            throw new InvalidOperationException("The output token limit must be positive.");
        if (messagesApi && settings.TokenLimitParameter == OutputTokenParameter.MaxCompletionTokens)
            throw new InvalidOperationException("Messages API requires max_tokens, not max_completion_tokens.");
        if (messagesApi && reasoning is ReasoningMode.OpenAiEffort or ReasoningMode.ThinkingEnabled)
            throw new InvalidOperationException("Messages API uses adaptive or budgeted thinking, not reasoning_effort.");
        if (!messagesApi && reasoning is ReasoningMode.AnthropicAdaptive or ReasoningMode.AnthropicBudgeted)
            throw new InvalidOperationException("Anthropic thinking options require Messages API.");
        if (reasoning == ReasoningMode.AnthropicBudgeted
            && (settings.ThinkingBudgetTokens < 1024 || settings.ThinkingBudgetTokens >= settings.MaxOutputTokens))
            throw new InvalidOperationException("Thinking budget must be at least 1024 and below max_tokens.");
        var effort = settings.ReasoningEffort;
        if (reasoning == ReasoningMode.OpenAiEffort
            && effort is not ("default" or "none" or "minimal" or "low" or "medium" or "high" or "xhigh" or "max"))
            throw new InvalidOperationException("Select a supported reasoning_effort value.");
        if (reasoning == ReasoningMode.AnthropicAdaptive
            && effort is not ("default" or "low" or "medium" or "high" or "xhigh" or "max"))
            throw new InvalidOperationException("Select a supported output_config.effort value.");
    }

    internal static void ValidateConfiguration(AppSettings settings)
    {
        if (!Enum.IsDefined(settings.Provider) || string.IsNullOrWhiteSpace(settings.Model))
            throw new InvalidOperationException("Select a provider and enter a model or deployment name.");
        ValidateRequestOptions(settings);
        _ = GetApiUrl(settings);
    }

    private static string GetApiUrl(AppSettings s)
    {
        return s.Provider switch
        {
            ApiProvider.AzureOpenAI => BuildAzureUrl(s),
            ApiProvider.Foundry => BuildInferenceUrl(s.AzureEndpoint, s.Protocol, foundry: true),
            ApiProvider.Custom => BuildInferenceUrl(s.CustomEndpoint, s.Protocol, foundry: false),
            ApiProvider.Google => "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions",
            _ => ProviderUrls.GetValueOrDefault(s.Provider, ProviderUrls[ApiProvider.OpenAI])
        };
    }

    private static string BuildAzureUrl(AppSettings s)
    {
        var endpoint = ValidateEndpoint(s.AzureEndpoint, allowLocalHttp: false);
        return $"{endpoint.AbsoluteUri.TrimEnd('/')}/openai/deployments/{Uri.EscapeDataString(s.Model)}/chat/completions?api-version={Uri.EscapeDataString(s.AzureApiVersion)}";
    }

    private static Uri ValidateEndpoint(string endpoint, bool allowLocalHttp)
    {
        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && !(allowLocalHttp && uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp))
            || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.Query.Length != 0)
            throw new InvalidOperationException("Enter an HTTPS API endpoint without credentials, query parameters, or a fragment. HTTP is allowed only for a local custom server.");
        return uri;
    }

    private static string BuildInferenceUrl(string endpoint, InferenceProtocol protocol, bool foundry)
    {
        if (!Enum.IsDefined(protocol))
            throw new InvalidOperationException("Select a supported API protocol.");
        var uri = ValidateEndpoint(endpoint, allowLocalHttp: !foundry);
        var path = uri.AbsolutePath.TrimEnd('/');
        var messages = protocol == InferenceProtocol.AnthropicMessages;
        var operation = messages ? "/messages" : "/chat/completions";

        if (foundry)
        {
            var basePath = messages ? "/anthropic/v1" : "/openai/v1";
            if (path.Length != 0 && !path.Equals(basePath, StringComparison.OrdinalIgnoreCase)
                && !path.Equals(basePath + operation, StringComparison.OrdinalIgnoreCase)
                && !(messages && path.Equals("/anthropic", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("The Foundry resource endpoint does not match the selected API protocol. Use the resource root, not a project endpoint.");
            path = basePath + operation;
        }
        else if (!path.EndsWith(operation, StringComparison.OrdinalIgnoreCase))
        {
            if (path.EndsWith("/messages", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The custom endpoint does not match the selected API protocol.");
            path += path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? operation : "/v1" + operation;
        }
        return new UriBuilder(uri) { Path = path }.Uri.AbsoluteUri;
    }

    private void ApplyAuth(HttpRequestMessage httpRequest)
    {
        var s = _settings.Current;
        if (IsAnthropicNative)
        {
            httpRequest.Headers.Add("x-api-key", s.ApiKey);
            httpRequest.Headers.Add("anthropic-version", "2023-06-01");
            return;
        }
        switch (s.Provider)
        {
            case ApiProvider.AzureOpenAI:
            case ApiProvider.Foundry:
                httpRequest.Headers.Add("api-key", s.ApiKey);
                break;
            case ApiProvider.Google:
                httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", s.ApiKey);
                break;
            default:
                httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", s.ApiKey);
                break;
        }
    }

    private static async Task EnsureSuccessOrThrowAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct);
        ApiError? error = null;
        try { error = JsonSerializer.Deserialize(body, ApiJsonContext.Default.ApiErrorEnvelope)?.Error; }
        catch (JsonException) { }
        var detail = error is null ? body : FormatApiError(error);
        var statusCode = (int)response.StatusCode;
        var message = statusCode switch
        {
            400 => $"요청 설정 오류 (400) — 모델의 API 형식과 지원 옵션을 확인해주세요.\n\n{detail}",
            401 => $"인증 실패 (401) — API Key 또는 인증 방식을 확인해주세요.\n\n{detail}",
            403 => $"접근 거부 (403) — 권한과 모델 사용 자격을 확인해주세요.\n\n{detail}",
            404 => $"배포 또는 엔드포인트 오류 (404) — API 형식과 배포 이름을 확인해주세요.\n\n{detail}",
            429 => $"요청 한도 초과 (429) — 잠시 후 다시 시도해주세요.\n\n{detail}",
            >= 500 => $"서버 오류 ({statusCode}) — 잠시 후 다시 시도해주세요.\n\n{detail}",
            _ => $"API 오류 ({statusCode})\n\n{detail}"
        };
        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private static string FormatApiError(ApiError error)
        => $"{error.Message}\n{error.Type} {error.Code}\n{error.Param}".Trim();

    private static void ValidateFinishReason(string? reason)
    {
        if (reason is "length" or "max_tokens" or "model_context_window_exceeded")
            throw new InvalidOperationException("The response reached a token limit. Review the output-token parameter, token limit, and thinking budget; the incomplete response was not saved.");
        if (reason is "tool_calls" or "function_call" or "tool_use" or "pause_turn")
            throw new InvalidOperationException("The model requested tools or another turn instead of completing a text response.");
        if (reason == "content_filter")
            throw new InvalidOperationException("The response was blocked by the model's content filter.");
    }

    private static string RequireText(string? text)
        => !string.IsNullOrWhiteSpace(text) ? text
            : throw new InvalidOperationException("The API returned no final text. Check the API protocol and output/thinking token limits.");

    public async Task<string> GetCompletionAsync(string input, LookupMode mode,
        string nativeLanguage, string foreignLanguage, CancellationToken ct = default)
    {
        var messagesApi = IsAnthropicNative;
        var request = CreateRequest(input, mode, nativeLanguage, foreignLanguage, stream: false);
        var json = JsonSerializer.Serialize(request, ApiJsonContext.Default.ChatCompletionRequest);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, GetApiUrl(_settings.Current));
        ApplyAuth(httpRequest);
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, ct);
        await EnsureSuccessOrThrowAsync(response, ct);

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        if (messagesApi)
        {
            var message = JsonSerializer.Deserialize(responseJson, ApiJsonContext.Default.AnthropicResponse);
            if (message?.Error is { } error) throw new HttpRequestException(FormatApiError(error));
            ValidateFinishReason(message?.StopReason);
            return RequireText(string.Concat(message?.Content?.Where(block => block.Type == "text").Select(block => block.Text) ?? []));
        }
        var result = JsonSerializer.Deserialize(responseJson, ApiJsonContext.Default.ChatCompletionResponse);
        if (result?.Error is { } chatError) throw new HttpRequestException(FormatApiError(chatError));
        var choice = result?.Choices?.FirstOrDefault();
        ValidateFinishReason(choice?.FinishReason);
        return RequireText(choice?.Message?.Content);
    }

    public async IAsyncEnumerable<string> StreamCompletionAsync(string input, LookupMode mode,
        string nativeLanguage, string foreignLanguage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var messagesApi = IsAnthropicNative;
        var request = CreateRequest(input, mode, nativeLanguage, foreignLanguage, stream: true);
        var json = JsonSerializer.Serialize(request, ApiJsonContext.Default.ChatCompletionRequest);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, GetApiUrl(_settings.Current));
        httpRequest.Version = new Version(1, 1); // Force HTTP/1.1 for reliable SSE streaming
        ApplyAuth(httpRequest);
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest,
            HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessOrThrowAsync(response, ct);

        await using var responseStream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(responseStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096);

        var sawText = false;
        var completed = false;
        await foreach (var data in ReadEventDataAsync(reader, ct))
        {
            if (!messagesApi && data.Trim() == "[DONE]")
            {
                completed = true;
                break;
            }
            var part = ExtractStreamContent(Encoding.UTF8.GetBytes(data), messagesApi);
            ValidateFinishReason(part.FinishReason);
            completed |= part.Completed || (!messagesApi && part.FinishReason is not null);
            if (!string.IsNullOrEmpty(part.Text))
            {
                sawText |= !string.IsNullOrWhiteSpace(part.Text);
                yield return part.Text;
            }
            if (part.Completed) break;
        }
        ct.ThrowIfCancellationRequested();
        if (!completed)
            throw new HttpRequestException("The stream ended before its completion event. The incomplete response was not saved.");
        if (!sawText) _ = RequireText(null);
    }

    private static async IAsyncEnumerable<string> ReadEventDataAsync(System.IO.StreamReader reader,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0)
            {
                if (data.Length == 0) continue;
                yield return data.ToString();
                data.Clear();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0) data.Append('\n');
                data.Append(line.AsSpan(line.Length > 5 && line[5] == ' ' ? 6 : 5));
            }
        }
        if (data.Length > 0) yield return data.ToString();
    }

    private ChatCompletionRequest CreateRequest(string input, LookupMode mode,
        string nativeLanguage, string foreignLanguage, bool stream)
    {
        var settings = _settings.Current;
        ValidateConfiguration(settings);
        var reasoning = GetReasoningMode(settings);
        var effort = settings.ReasoningEffort;
        var messagesApi = GetProtocol(settings) == InferenceProtocol.AnthropicMessages;

        var request = new ChatCompletionRequest
        {
            Model = settings.Model,
            Stream = stream,
            Messages =
            [
                new ChatMessage
                {
                    Role = settings.InstructionRole.ToString().ToLowerInvariant(),
                    Content = _promptBuilder.GetSystemMessage(mode, nativeLanguage, foreignLanguage)
                },
                new ChatMessage
                {
                    Role = "user",
                    Content = _promptBuilder.Build(input, mode, nativeLanguage, foreignLanguage)
                }
            ]
        };

        if (messagesApi)
        {
            request.System = request.Messages[0].Content;
            request.Messages.RemoveAt(0);
            request.MaxTokens = settings.MaxOutputTokens;
        }
        else
        {
            if (settings.InstructionRole == InstructionRole.User)
            {
                request.Messages[1].Content = request.Messages[0].Content + "\n\n" + request.Messages[1].Content;
                request.Messages.RemoveAt(0);
            }
            if (settings.TokenLimitParameter == OutputTokenParameter.MaxCompletionTokens)
                request.MaxCompletionTokens = settings.MaxOutputTokens;
            else if (settings.TokenLimitParameter == OutputTokenParameter.MaxTokens)
                request.MaxTokens = settings.MaxOutputTokens;
        }

        switch (reasoning)
        {
            case ReasoningMode.OpenAiEffort when effort != "default":
                request.ReasoningEffort = effort;
                break;
            case ReasoningMode.ThinkingEnabled:
                request.Thinking = new ThinkingOptions { Type = "enabled" };
                break;
            case ReasoningMode.ThinkingDisabled:
                request.Thinking = new ThinkingOptions { Type = "disabled" };
                break;
            case ReasoningMode.AnthropicAdaptive:
                request.Thinking = new ThinkingOptions { Type = "adaptive" };
                if (effort != "default") request.OutputConfig = new OutputOptions { Effort = effort };
                break;
            case ReasoningMode.AnthropicBudgeted:
                request.Thinking = new ThinkingOptions { Type = "enabled", BudgetTokens = settings.ThinkingBudgetTokens };
                break;
        }

        return request;
    }

    // --- SSE content extraction ---

    /// <summary>
    /// Reads only the final-text paths for the selected protocol; other content blocks are skipped.
    /// </summary>
    private static (string? Text, string? FinishReason, bool Completed) ExtractStreamContent(ReadOnlySpan<byte> utf8Json, bool messagesApi)
    {
        var reader = new Utf8JsonReader(utf8Json);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected an SSE object.");
        string? eventType = null;
        string? text = null;
        string? deltaType = null;
        string? finishReason = null;
        ApiError? error = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var isType = reader.ValueTextEquals("type"u8);
            var isError = reader.ValueTextEquals("error"u8);
            var isDelta = reader.ValueTextEquals("delta"u8) || reader.ValueTextEquals("content_block"u8);
            var isChoices = reader.ValueTextEquals("choices"u8);
            if (!reader.Read()) throw new JsonException("Incomplete SSE object.");
            if (isType) eventType = reader.GetString();
            else if (isError) error = JsonSerializer.Deserialize(ref reader, ApiJsonContext.Default.ApiError);
            else if (isDelta && messagesApi) (text, deltaType, finishReason) = ReadDelta(ref reader, messagesApi: true);
            else if (isChoices && !messagesApi) (text, finishReason) = ReadChatChoices(ref reader);
            else reader.Skip();
        }
        if (reader.Read()) throw new JsonException("Unexpected data after the SSE object.");
        if (error is not null || eventType == "error")
            throw new HttpRequestException(error is null ? "The API reported a streaming error." : FormatApiError(error));
        if (messagesApi)
            text = (eventType == "content_block_delta" && deltaType == "text_delta")
                || (eventType == "content_block_start" && deltaType == "text") ? text : null;
        return (text, finishReason, messagesApi && eventType == "message_stop");
    }

    private static (string? Text, string? Type, string? FinishReason) ReadDelta(ref Utf8JsonReader reader, bool messagesApi)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected a delta object.");
        string? text = null;
        string? type = null;
        string? finishReason = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var isText = messagesApi ? reader.ValueTextEquals("text"u8) : reader.ValueTextEquals("content"u8);
            var isType = reader.ValueTextEquals("type"u8);
            var isStopReason = reader.ValueTextEquals("stop_reason"u8);
            if (!reader.Read()) throw new JsonException("Incomplete delta.");
            if (isText && reader.TokenType == JsonTokenType.String)
                text = reader.GetString();
            else if (isType && messagesApi) type = reader.GetString();
            else if (isStopReason && messagesApi) finishReason = reader.GetString();
            else reader.Skip();
        }
        return (text, type, finishReason);
    }

    private static (string? Text, string? FinishReason) ReadChatChoices(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected choices.");
        string? text = null;
        string? finishReason = null;
        if (!reader.Read()) throw new JsonException("Incomplete choices.");
        if (reader.TokenType == JsonTokenType.EndArray) return (null, null);
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected a choice.");
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var isDelta = reader.ValueTextEquals("delta"u8);
            var isFinishReason = reader.ValueTextEquals("finish_reason"u8);
            if (!reader.Read()) throw new JsonException("Incomplete choice.");
            if (isDelta) (text, _, _) = ReadDelta(ref reader, messagesApi: false);
            else if (isFinishReason) finishReason = reader.GetString();
            else reader.Skip();
        }
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) reader.Skip();
        return (text, finishReason);
    }

    // --- Request/Response DTOs ---

    private sealed class ChatCompletionRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "gpt-4o-mini";
        [JsonPropertyName("messages")] public List<ChatMessage> Messages { get; set; } = [];
        [JsonPropertyName("stream")] public bool Stream { get; set; }
        [JsonPropertyName("system")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? System { get; set; }
        [JsonPropertyName("reasoning_effort")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ReasoningEffort { get; set; }
        [JsonPropertyName("max_completion_tokens")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? MaxCompletionTokens { get; set; }
        [JsonPropertyName("max_tokens")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? MaxTokens { get; set; }
        [JsonPropertyName("thinking")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ThinkingOptions? Thinking { get; set; }
        [JsonPropertyName("output_config")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public OutputOptions? OutputConfig { get; set; }
    }

    private sealed class ThinkingOptions
    {
        [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
        [JsonPropertyName("budget_tokens")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? BudgetTokens { get; set; }
    }

    private sealed class OutputOptions
    {
        [JsonPropertyName("effort")] public string Effort { get; set; } = string.Empty;
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
        [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    }

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
        [JsonPropertyName("error")] public ApiError? Error { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
        [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
    }

    private sealed class AnthropicResponse
    {
        [JsonPropertyName("content")] public List<ContentBlock>? Content { get; set; }
        [JsonPropertyName("stop_reason")] public string? StopReason { get; set; }
        [JsonPropertyName("error")] public ApiError? Error { get; set; }
    }

    private sealed class ContentBlock
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
    }

    private sealed class ApiErrorEnvelope
    {
        [JsonPropertyName("error")] public ApiError? Error { get; set; }
    }

    private sealed class ApiError
    {
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("param")] public string? Param { get; set; }
        [JsonPropertyName("code")] public JsonElement Code { get; set; }
    }

    // Source-generated JSON context for API DTOs — eliminates reflection overhead
    [JsonSerializable(typeof(ChatCompletionRequest))]
    [JsonSerializable(typeof(ChatCompletionResponse))]
    [JsonSerializable(typeof(AnthropicResponse))]
    [JsonSerializable(typeof(ApiErrorEnvelope))]
    [JsonSerializable(typeof(ApiError))]
    private sealed partial class ApiJsonContext : JsonSerializerContext;
}
