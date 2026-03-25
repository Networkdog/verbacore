using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VerbaCore.Models;

namespace VerbaCore.Services;

public interface IOpenAiService
{
    Task<string> GetCompletionAsync(string input, LookupMode mode, string sourceLanguage,
        string targetLanguage, CancellationToken ct = default);

    IAsyncEnumerable<string> StreamCompletionAsync(string input, LookupMode mode,
        string sourceLanguage, string targetLanguage, CancellationToken ct = default);
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
            ApiProvider.AzureOpenAI => $"{s.AzureEndpoint.TrimEnd('/')}/openai/deployments/{s.Model}/chat/completions?api-version={s.AzureApiVersion}",
            ApiProvider.Custom => $"{s.CustomEndpoint.TrimEnd('/')}/v1/chat/completions",
            ApiProvider.Google => $"https://generativelanguage.googleapis.com/v1beta/openai/chat/completions",
            _ => ProviderUrls.GetValueOrDefault(s.Provider, ProviderUrls[ApiProvider.OpenAI])
        };
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
        string sourceLanguage, string targetLanguage, CancellationToken ct = default)
    {
        var request = CreateRequest(input, mode, sourceLanguage, targetLanguage, stream: false);
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
        string sourceLanguage, string targetLanguage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = CreateRequest(input, mode, sourceLanguage, targetLanguage, stream: true);
        var json = JsonSerializer.Serialize(request);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, GetApiUrl());
        httpRequest.Version = new Version(1, 1); // Force HTTP/1.1 for reliable SSE streaming
        ApplyAuth(httpRequest);
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest,
            HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessOrThrowAsync(response, ct);

        await using var responseStream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(responseStream, Encoding.UTF8, false, bufferSize: 64);

        var lineBuffer = new StringBuilder();
        var charBuffer = new char[1];

        while (!ct.IsCancellationRequested)
        {
            var bytesRead = await reader.ReadAsync(charBuffer, 0, 1);
            if (bytesRead == 0) yield break; // End of stream

            var ch = charBuffer[0];
            if (ch == '\n')
            {
                var line = lineBuffer.ToString();
                lineBuffer.Clear();

                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.StartsWith("data: ")) continue;

                var data = line["data: ".Length..];
                if (data == "[DONE]") yield break;

                var chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(data);
                var content = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
                if (!string.IsNullOrEmpty(content))
                {
                    yield return content;
                }
            }
            else if (ch != '\r')
            {
                lineBuffer.Append(ch);
            }
        }
    }

    private ChatCompletionRequest CreateRequest(string input, LookupMode mode,
        string sourceLanguage, string targetLanguage, bool stream)
    {
        var effort = _settings.Current.ReasoningEffort;
        var isReasoning = !string.IsNullOrEmpty(effort) && effort != "none";

        var request = new ChatCompletionRequest
        {
            Model = _settings.Current.Model,
            Stream = stream,
            Messages =
            [
                new ChatMessage
                {
                    // Reasoning models use "developer" role instead of "system"
                    Role = isReasoning ? "developer" : "system",
                    Content = _promptBuilder.GetSystemMessage(mode)
                },
                new ChatMessage
                {
                    Role = "user",
                    Content = _promptBuilder.Build(input, mode, sourceLanguage, targetLanguage)
                }
            ],
            // Reasoning models don't support temperature
            Temperature = isReasoning ? null : 0.3,
            ReasoningEffort = isReasoning ? effort : null,
            MaxTokens = isReasoning ? null : (mode == LookupMode.Dictionary ? 2048 : 4096)
        };

        // Anthropic uses "max_tokens" (required) and doesn't support "system" role in messages
        if (IsAnthropicNative)
        {
            request.System = request.Messages[0].Content;
            request.Messages.RemoveAt(0);
            request.MaxTokens = mode == LookupMode.Dictionary ? 2048 : 4096;
            request.Temperature = null; // Anthropic handles temperature differently
            request.ReasoningEffort = null;
        }

        return request;
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

    private sealed class ChatCompletionChunk
    {
        [JsonPropertyName("choices")] public List<StreamChoice>? Choices { get; set; }
    }

    private sealed class StreamChoice
    {
        [JsonPropertyName("delta")] public DeltaContent? Delta { get; set; }
    }

    private sealed class DeltaContent
    {
        [JsonPropertyName("content")] public string? Content { get; set; }
    }
}
