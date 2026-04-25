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

public sealed class OpenAiService : IOpenAiService
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

    private bool IsAnthropicNative => _settings.Current.Provider == ApiProvider.Anthropic;

    private string GetApiUrl()
    {
        var s = _settings.Current;
        return s.Provider switch
        {
            ApiProvider.AzureOpenAI => BuildAzureUrl(s),
            ApiProvider.Custom => BuildCustomUrl(s.CustomEndpoint),
            ApiProvider.Google => "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions",
            _ => ProviderUrls.GetValueOrDefault(s.Provider, ProviderUrls[ApiProvider.OpenAI])
        };
    }

    private static string BuildAzureUrl(AppSettings s)
    {
        // Use AzureDeploymentName if set, otherwise fall back to Model
        var deployment = !string.IsNullOrWhiteSpace(s.AzureDeploymentName)
            ? s.AzureDeploymentName
            : s.Model;
        return $"{s.AzureEndpoint.TrimEnd('/')}/openai/deployments/{deployment}/chat/completions?api-version={s.AzureApiVersion}";
    }

    private static string BuildCustomUrl(string endpoint)
    {
        var trimmed = endpoint.TrimEnd('/');
        // If user already included /v1 or a versioned path, append directly
        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("/v1/", StringComparison.OrdinalIgnoreCase))
        {
            return $"{trimmed}/chat/completions";
        }
        return $"{trimmed}/v1/chat/completions";
    }

    private void ApplyAuth(HttpRequestMessage httpRequest)
    {
        var s = _settings.Current;
        switch (s.Provider)
        {
            case ApiProvider.AzureOpenAI:
                httpRequest.Headers.Add("api-key", s.ApiKey);
                break;
            case ApiProvider.Anthropic:
                httpRequest.Headers.Add("x-api-key", s.ApiKey);
                httpRequest.Headers.Add("anthropic-version", "2023-06-01");
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
        var statusCode = (int)response.StatusCode;
        var message = statusCode switch
        {
            401 => $"인증 실패 (401) — API Key가 올바른지 확인해주세요.\n\n{body}",
            403 => $"접근 거부 (403) — API Key의 권한을 확인해주세요.\n\n{body}",
            429 => $"요청 한도 초과 (429) — 잠시 후 다시 시도해주세요.\n\n{body}",
            >= 500 => $"서버 오류 ({statusCode}) — 잠시 후 다시 시도해주세요.\n\n{body}",
            _ => $"API 오류 ({statusCode})\n\n{body}"
        };
        throw new HttpRequestException(message, null, response.StatusCode);
    }

    public async Task<string> GetCompletionAsync(string input, LookupMode mode,
        string nativeLanguage, string foreignLanguage, CancellationToken ct = default)
    {
        var request = CreateRequest(input, mode, nativeLanguage, foreignLanguage, stream: false);
        var json = JsonSerializer.Serialize(request);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, GetApiUrl());
        ApplyAuth(httpRequest);
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, ct);
        await EnsureSuccessOrThrowAsync(response, ct);

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<ChatCompletionResponse>(responseJson);

        return result?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
    }

    public async IAsyncEnumerable<string> StreamCompletionAsync(string input, LookupMode mode,
        string nativeLanguage, string foreignLanguage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = CreateRequest(input, mode, nativeLanguage, foreignLanguage, stream: true);
        var json = JsonSerializer.Serialize(request);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, GetApiUrl());
        httpRequest.Version = new Version(1, 1); // Force HTTP/1.1 for reliable SSE streaming
        ApplyAuth(httpRequest);
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest,
            HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessOrThrowAsync(response, ct);

        await using var responseStream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(responseStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) yield break; // End of stream

            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") yield break;

            // Anthropic SSE uses content_block_delta; OpenAI-compatible uses choices[].delta
            string? content = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;

                if (root.TryGetProperty("choices", out var choices)
                    && choices.GetArrayLength() > 0
                    && choices[0].TryGetProperty("delta", out var delta)
                    && delta.TryGetProperty("content", out var contentProp))
                {
                    content = contentProp.GetString();
                }
                else if (root.TryGetProperty("type", out var typeProp)
                         && typeProp.GetString() == "content_block_delta"
                         && root.TryGetProperty("delta", out var anthropicDelta)
                         && anthropicDelta.TryGetProperty("text", out var textProp))
                {
                    content = textProp.GetString();
                }
            }
            catch (JsonException) { continue; }

            if (!string.IsNullOrEmpty(content))
            {
                yield return content;
            }
        }
    }

    private ChatCompletionRequest CreateRequest(string input, LookupMode mode,
        string nativeLanguage, string foreignLanguage, bool stream)
    {
        var effort = _settings.Current.ReasoningEffort;
        var isReasoning = !string.IsNullOrEmpty(effort) && effort != "none";
        var model = _settings.Current.Model;

        // Reasoning-capable models (o-series, gpt-5.x) don't support temperature
        var isReasoningModel = isReasoning || IsReasoningCapableModel(model);

        var request = new ChatCompletionRequest
        {
            Model = model,
            Stream = stream,
            Messages =
            [
                new ChatMessage
                {
                    // Reasoning models use "developer" role instead of "system"
                    Role = isReasoning ? "developer" : "system",
                    Content = _promptBuilder.GetSystemMessage(mode, nativeLanguage, foreignLanguage)
                },
                new ChatMessage
                {
                    Role = "user",
                    Content = _promptBuilder.Build(input, mode, nativeLanguage, foreignLanguage)
                }
            ],
            // Reasoning-capable models don't support temperature
            Temperature = isReasoningModel ? null : 0.3,
            ReasoningEffort = isReasoning ? effort : null,
            MaxCompletionTokens = isReasoning ? null : (mode == LookupMode.Dictionary ? 2048 : 4096)
        };

        // Anthropic uses "max_tokens" (required) and doesn't support "system" role in messages
        if (IsAnthropicNative)
        {
            request.System = request.Messages[0].Content;
            request.Messages.RemoveAt(0);
            // Anthropic requires max_tokens, not max_completion_tokens
            request.MaxCompletionTokens = null;
            request.MaxTokens = mode == LookupMode.Dictionary ? 2048 : 4096;
            request.Temperature = null; // Anthropic handles temperature differently
            request.ReasoningEffort = null;
        }

        return request;
    }

    /// <summary>
    /// Detects reasoning-capable models that don't support the temperature parameter.
    /// Covers o-series (o1, o3, o4-mini) and gpt-5.x reasoning models.
    /// </summary>
    private static bool IsReasoningCapableModel(string model)
    {
        // o1, o1-mini, o3, o3-mini, o4-mini, etc.
        if (model.StartsWith("o", StringComparison.OrdinalIgnoreCase)
            && model.Length >= 2
            && char.IsDigit(model[1]))
            return true;

        // gpt-5.x models are reasoning-capable
        if (model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
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
        [JsonPropertyName("temperature")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Temperature { get; set; } = 0.3;
        [JsonPropertyName("reasoning_effort")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ReasoningEffort { get; set; }
        [JsonPropertyName("max_completion_tokens")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? MaxCompletionTokens { get; set; }
        [JsonPropertyName("max_tokens")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? MaxTokens { get; set; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
        [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    }

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
    }
}
