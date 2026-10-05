using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using VerbaCore.Models;
using VerbaCore.Services;
using VerbaCore.ViewModels;
using VerbaCore.Views;

namespace VerbaCore.PopupTests;

internal static class Program
{
    internal const uint BlockMessage = 0x8007;
    internal const uint LockForegroundMessage = 0x8008;
    internal const uint UnlockForegroundMessage = 0x8009;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--api-requests"))
            return RunApiRequestTestsAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (args.Contains("--input-hooks")) return RunInputHookTests();

        if (args.Length == 2 && args[0] == "--host")
        {
            using var blocked = EventWaitHandle.OpenExisting(args[1] + ".blocked");
            using var release = EventWaitHandle.OpenExisting(args[1] + ".release");
            var host = new Window
            {
                Title = "VerbaCore popup test foreground",
                Width = 360,
                Height = 180,
                Content = new TextBox()
            };
            host.SourceInitialized += (_, _) =>
            {
                var source = (HwndSource)PresentationSource.FromVisual(host);
                source.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                {
                    if (message == LockForegroundMessage)
                    {
                        if (Native.LockSetForegroundWindow(1)) blocked.Set();
                        handled = true;
                    }
                    else if (message == UnlockForegroundMessage)
                    {
                        Native.LockSetForegroundWindow(2);
                        handled = true;
                    }
                    if (message == BlockMessage)
                    {
                        blocked.Set();
                        release.WaitOne(TimeSpan.FromSeconds(3));
                        handled = true;
                    }
                    return IntPtr.Zero;
                });
            };
            host.Loaded += (_, _) => Console.WriteLine(new WindowInteropHelper(host).Handle.ToInt64());
            return new Application().Run(host);
        }

        var app = new PopupTestApplication(args.Contains("--offscreen"), args.Contains("--settings"), args.Contains("--focus"),
            args.Contains("--selection"), args.Contains("--selection-office"), args.Contains("--selection-vscode"));
        app.LoadResources();
        return app.Run();
    }

    private static async Task<int> RunApiRequestTestsAsync(CancellationToken ct)
    {
        (string Model, bool SupportsTemperature)[] models =
        [
            ("gpt-6", false),
            ("gpt-6-mini", false),
            ("gpt-6.1", false),
            ("GPT-6", false),
            ("gpt-5.2", false),
            ("o3-mini", false),
            ("gpt-4o-mini", false),
            ("custom-model", false),
            ("production-deployment", false),
            ("DeepSeek-R1", false),
            ("Kimi-K2.5", false)
        ];
        var checkedRequests = 0;
        try
        {
            foreach (var (model, supportsTemperature) in models)
            foreach (var effort in new[] { "none", "", "low" })
            foreach (var stream in new[] { false, true })
            {
                var settings = new SettingsService();
                settings.Current.Model = model;
                settings.Current.ReasoningEffort = effort;
                using var handler = new CompletionRequestHandler();
                using var client = new HttpClient(handler);
                var service = new OpenAiService(client, settings, new PromptBuilder());
                var result = new StringBuilder();
                if (stream)
                {
                    await foreach (var chunk in service.StreamCompletionAsync(
                        "hello", LookupMode.Dictionary, "Korean", "English", ct))
                        result.Append(chunk);
                }
                else
                {
                    result.Append(await service.GetCompletionAsync(
                        "hello", LookupMode.Dictionary, "Korean", "English", ct));
                }

                using var payload = JsonDocument.Parse(handler.RequestJson!);
                var root = payload.RootElement;
                var context = $"model={model}, effort='{effort}', stream={stream}";
                var isReasoning = !string.IsNullOrEmpty(effort) && effort != "none";
                var hasTemperature = root.TryGetProperty("temperature", out var temperature);
                if (hasTemperature != (supportsTemperature && !isReasoning)
                    || (hasTemperature && temperature.GetDouble() != 0.3))
                    throw new InvalidOperationException($"Incorrect temperature payload: {context}");
                if (root.GetProperty("model").GetString() != model
                    || root.GetProperty("stream").GetBoolean() != stream
                    || root.GetProperty("messages")[0].GetProperty("role").GetString() != "system"
                    || root.TryGetProperty("max_tokens", out _) || root.TryGetProperty("max_completion_tokens", out _))
                    throw new InvalidOperationException($"Unrelated request fields changed: {context}");
                var hasEffort = root.TryGetProperty("reasoning_effort", out var sentEffort);
                if (hasEffort != isReasoning || (hasEffort && sentEffort.GetString() != effort))
                    throw new InvalidOperationException($"Reasoning effort changed: {context}");
                if (result.ToString() != "ok" || handler.RequestCount != 1)
                    throw new InvalidOperationException($"Response handling changed: {context}");
                checkedRequests++;
            }
            await RunProtocolRequestTestsAsync(ct);
            await RunRequestOptionTestsAsync(ct);
            await RunResponseContractTestsAsync(ct);
            RunSettingsContractTests();
            Console.WriteLine($"PASS: {checkedRequests} serialized completion/streaming requests use model-default temperature without model-name heuristics. No network used.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private sealed class CompletionRequestHandler : HttpMessageHandler
    {
        public string? RequestJson { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? KeyHeader { get; private set; }
        public string? ApiVersion { get; private set; }
        public string? ResponseBody { get; init; }
        public HttpStatusCode ResponseStatus { get; init; } = HttpStatusCode.OK;
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestJson = await request.Content!.ReadAsStringAsync(ct);
            RequestUri = request.RequestUri;
            KeyHeader = request.Headers.Contains("x-api-key") ? "x-api-key"
                : request.Headers.Contains("api-key") ? "api-key" : "Authorization";
            ApiVersion = request.Headers.TryGetValues("anthropic-version", out var versions) ? versions.Single() : null;
            RequestCount++;
            using var payload = JsonDocument.Parse(RequestJson);
            var stream = payload.RootElement.GetProperty("stream").GetBoolean();
            var body = stream
                ? "data: {\"choices\":[{\"delta\":{\"content\":\"ok\"}}]}\n\ndata: [DONE]\n\n"
                : """{"choices":[{"message":{"content":"ok"}}]}""";
            if (request.RequestUri!.AbsolutePath.EndsWith("/messages", StringComparison.Ordinal))
                body = stream
                    ? "data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"ok\"}}\n\ndata: {\"type\":\"message_stop\"}\n\n"
                    : """{"type":"message","content":[{"type":"thinking","thinking":"not displayed"},{"type":"text","text":"o"},{"type":"text","text":"k"}]}""";
            return new HttpResponseMessage(ResponseStatus)
            {
                Content = new StringContent(ResponseBody ?? body, Encoding.UTF8, stream ? "text/event-stream" : "application/json")
            };
        }
    }

    private static async Task RunProtocolRequestTestsAsync(CancellationToken ct)
    {
        (ApiProvider Provider, InferenceProtocol Protocol, string Endpoint, string ExpectedUrl, string KeyHeader)[] cases =
        [
            (ApiProvider.OpenAI, InferenceProtocol.ChatCompletions, "", "https://api.openai.com/v1/chat/completions", "Authorization"),
            (ApiProvider.Google, InferenceProtocol.ChatCompletions, "", "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", "Authorization"),
            (ApiProvider.OpenRouter, InferenceProtocol.ChatCompletions, "", "https://openrouter.ai/api/v1/chat/completions", "Authorization"),
            (ApiProvider.AzureOpenAI, InferenceProtocol.ChatCompletions, "https://sample.openai.azure.com", "https://sample.openai.azure.com/openai/deployments/production/chat/completions?api-version=2024-10-21", "api-key"),
            (ApiProvider.Anthropic, InferenceProtocol.ChatCompletions, "", "https://api.anthropic.com/v1/messages", "x-api-key"),
            (ApiProvider.Foundry, InferenceProtocol.ChatCompletions, "https://sample.services.ai.azure.com/", "https://sample.services.ai.azure.com/openai/v1/chat/completions", "api-key"),
            (ApiProvider.Foundry, InferenceProtocol.ChatCompletions, "https://sample.openai.azure.com/openai/v1/", "https://sample.openai.azure.com/openai/v1/chat/completions", "api-key"),
            (ApiProvider.Foundry, InferenceProtocol.AnthropicMessages, "https://sample.services.ai.azure.com", "https://sample.services.ai.azure.com/anthropic/v1/messages", "x-api-key"),
            (ApiProvider.Foundry, InferenceProtocol.AnthropicMessages, "https://sample.services.ai.azure.com/anthropic", "https://sample.services.ai.azure.com/anthropic/v1/messages", "x-api-key"),
            (ApiProvider.Custom, InferenceProtocol.ChatCompletions, "http://localhost:8080/v1/chat/completions", "http://localhost:8080/v1/chat/completions", "Authorization"),
            (ApiProvider.Custom, InferenceProtocol.AnthropicMessages, "https://proxy.example.com/anthropic/v1/messages", "https://proxy.example.com/anthropic/v1/messages", "x-api-key")
        ];
        foreach (var test in cases)
        foreach (var stream in new[] { false, true })
        {
            var settings = new SettingsService();
            settings.Current.Provider = test.Provider;
            settings.Current.Protocol = test.Protocol;
            settings.Current.AzureEndpoint = test.Endpoint;
            settings.Current.CustomEndpoint = test.Endpoint;
            settings.Current.Model = "production";
            settings.Current.ApiKey = "test-key";
            using var handler = new CompletionRequestHandler();
            using var client = new HttpClient(handler);
            var service = new OpenAiService(client, settings, new PromptBuilder());
            var content = new StringBuilder();
            if (stream)
            {
                await foreach (var chunk in service.StreamCompletionAsync("hello", LookupMode.Dictionary, "Korean", "English", ct))
                    content.Append(chunk);
            }
            else
                content.Append(await service.GetCompletionAsync("hello", LookupMode.Dictionary, "Korean", "English", ct));

            using var payload = JsonDocument.Parse(handler.RequestJson!);
            var root = payload.RootElement;
            var messagesApi = test.ExpectedUrl.EndsWith("/messages", StringComparison.Ordinal);
            if (handler.RequestUri?.AbsoluteUri != test.ExpectedUrl || handler.KeyHeader != test.KeyHeader
                || content.ToString() != "ok" || root.GetProperty("model").GetString() != "production")
                throw new InvalidOperationException($"Protocol mismatch: {test.Provider}/{test.Protocol}, stream={stream}");
            if (messagesApi && (handler.ApiVersion != "2023-06-01" || !root.TryGetProperty("system", out _)
                || !root.TryGetProperty("max_tokens", out _) || root.TryGetProperty("max_completion_tokens", out _)
                || root.GetProperty("messages").GetArrayLength() != 1))
                throw new InvalidOperationException("Messages API used a Chat Completions payload.");
        }
        Console.WriteLine($"PASS: {cases.Length * 2} protocol, endpoint, authentication, and response cases");
    }

    private static async Task RunRequestOptionTestsAsync(CancellationToken ct)
    {
        (InferenceProtocol Protocol, ReasoningMode Mode, string Effort, OutputTokenParameter Limit, InstructionRole Role)[] cases =
        [
            (InferenceProtocol.ChatCompletions, ReasoningMode.ModelDefault, "high", OutputTokenParameter.ModelDefault, InstructionRole.System),
            (InferenceProtocol.ChatCompletions, ReasoningMode.OpenAiEffort, "none", OutputTokenParameter.MaxCompletionTokens, InstructionRole.Developer),
            (InferenceProtocol.ChatCompletions, ReasoningMode.OpenAiEffort, "low", OutputTokenParameter.MaxCompletionTokens, InstructionRole.System),
            (InferenceProtocol.ChatCompletions, ReasoningMode.ThinkingEnabled, "default", OutputTokenParameter.MaxTokens, InstructionRole.User),
            (InferenceProtocol.ChatCompletions, ReasoningMode.ThinkingDisabled, "default", OutputTokenParameter.MaxTokens, InstructionRole.System),
            (InferenceProtocol.AnthropicMessages, ReasoningMode.ModelDefault, "default", OutputTokenParameter.ModelDefault, InstructionRole.System),
            (InferenceProtocol.AnthropicMessages, ReasoningMode.AnthropicAdaptive, "high", OutputTokenParameter.MaxTokens, InstructionRole.System),
            (InferenceProtocol.AnthropicMessages, ReasoningMode.AnthropicBudgeted, "default", OutputTokenParameter.MaxTokens, InstructionRole.System),
            (InferenceProtocol.AnthropicMessages, ReasoningMode.ThinkingDisabled, "default", OutputTokenParameter.ModelDefault, InstructionRole.System)
        ];
        foreach (var test in cases)
        foreach (var stream in new[] { false, true })
        {
            var settings = new SettingsService();
            var current = settings.Current;
            current.Provider = ApiProvider.Foundry;
            current.AzureEndpoint = "https://sample.services.ai.azure.com";
            current.Protocol = test.Protocol;
            current.ReasoningMode = test.Mode;
            current.ReasoningEffort = test.Effort;
            current.TokenLimitParameter = test.Limit;
            current.InstructionRole = test.Role;
            using var handler = new CompletionRequestHandler();
            using var client = new HttpClient(handler);
            var service = new OpenAiService(client, settings, new PromptBuilder());
            if (stream)
            {
                await foreach (var chunk in service.StreamCompletionAsync("hello", LookupMode.Dictionary, "Korean", "English", ct))
                    _ = chunk;
            }
            else
                _ = await service.GetCompletionAsync("hello", LookupMode.Dictionary, "Korean", "English", ct);
            using var payload = JsonDocument.Parse(handler.RequestJson!);
            var root = payload.RootElement;
            var messagesApi = test.Protocol == InferenceProtocol.AnthropicMessages;
            if (root.TryGetProperty("temperature", out _)
                || root.TryGetProperty("max_tokens", out _) != (messagesApi || test.Limit == OutputTokenParameter.MaxTokens)
                || root.TryGetProperty("max_completion_tokens", out _) != (!messagesApi && test.Limit == OutputTokenParameter.MaxCompletionTokens)
                || root.TryGetProperty("reasoning_effort", out _) != (test.Mode == ReasoningMode.OpenAiEffort)
                || root.TryGetProperty("output_config", out _) != (test.Mode == ReasoningMode.AnthropicAdaptive))
                throw new InvalidOperationException($"Incorrect optional fields: {test}");
            if (!messagesApi && root.GetProperty("messages")[0].GetProperty("role").GetString() != test.Role.ToString().ToLowerInvariant())
                throw new InvalidOperationException("Instruction role was inferred instead of explicitly selected.");
            if (test.Role == InstructionRole.User && root.GetProperty("messages").GetArrayLength() != 1)
                throw new InvalidOperationException("User-only instructions were not merged.");
            var thinkingType = test.Mode switch
            {
                ReasoningMode.ThinkingEnabled or ReasoningMode.AnthropicBudgeted => "enabled",
                ReasoningMode.ThinkingDisabled => "disabled",
                ReasoningMode.AnthropicAdaptive => "adaptive",
                _ => null
            };
            if (root.TryGetProperty("thinking", out var thinking) != (thinkingType is not null))
                throw new InvalidOperationException("Incorrect thinking option presence.");
            if (thinkingType is not null && (thinking.GetProperty("type").GetString() != thinkingType
                || thinking.TryGetProperty("budget_tokens", out _) != (test.Mode == ReasoningMode.AnthropicBudgeted)))
                throw new InvalidOperationException("Thinking schema did not match the chosen mode.");
            if (test.Mode == ReasoningMode.OpenAiEffort && root.GetProperty("reasoning_effort").GetString() != test.Effort)
                throw new InvalidOperationException("Explicit reasoning effort was not preserved.");
        }

        Action<AppSettings>[] invalidCases =
        [
            settings => { settings.Provider = ApiProvider.Anthropic; settings.ReasoningMode = ReasoningMode.OpenAiEffort; },
            settings => { settings.Provider = ApiProvider.Anthropic; settings.ReasoningMode = ReasoningMode.ThinkingEnabled; },
            settings => settings.ReasoningMode = ReasoningMode.AnthropicAdaptive,
            settings => settings.ReasoningMode = ReasoningMode.AnthropicBudgeted,
            settings => { settings.Provider = ApiProvider.Anthropic; settings.TokenLimitParameter = OutputTokenParameter.MaxCompletionTokens; },
            settings => { settings.Provider = ApiProvider.Anthropic; settings.ReasoningMode = ReasoningMode.AnthropicBudgeted; settings.ThinkingBudgetTokens = 10; },
            settings => { settings.Provider = ApiProvider.Anthropic; settings.ReasoningMode = ReasoningMode.AnthropicBudgeted; settings.ThinkingBudgetTokens = settings.MaxOutputTokens; },
            settings => settings.MaxOutputTokens = 0,
            settings => settings.Protocol = (InferenceProtocol)999,
            settings => { settings.Provider = ApiProvider.Foundry; settings.AzureEndpoint = "https://sample.services.ai.azure.com/api/projects/project"; },
            settings => { settings.Provider = ApiProvider.Foundry; settings.AzureEndpoint = "https://sample.services.ai.azure.com/anthropic/v1/messages"; },
            settings => { settings.Provider = ApiProvider.Custom; settings.CustomEndpoint = "http://example.com"; },
            settings => { settings.Provider = ApiProvider.Custom; settings.CustomEndpoint = "https://user:secret@example.com"; },
            settings => { settings.Provider = ApiProvider.Custom; settings.CustomEndpoint = "https://example.com?api-key=secret"; },
            settings => { settings.Provider = ApiProvider.Custom; settings.CustomEndpoint = "https://example.com#fragment"; },
            settings => settings.Model = " "
        ];
        foreach (var configure in invalidCases)
        {
            var settings = new SettingsService();
            configure(settings.Current);
            using var handler = new CompletionRequestHandler();
            using var client = new HttpClient(handler);
            var service = new OpenAiService(client, settings, new PromptBuilder());
            var rejected = false;
            try { await service.GetCompletionAsync("hello", LookupMode.Dictionary, "Korean", "English", ct); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected || handler.RequestCount != 0)
                throw new InvalidOperationException("An invalid option combination reached the server.");
        }
        Console.WriteLine($"PASS: {cases.Length * 2} explicit request-option cases and {invalidCases.Length} preflight rejections");
    }

    private static async Task RunResponseContractTestsAsync(CancellationToken ct)
    {
        (InferenceProtocol Protocol, bool Stream, string Body, string? Expected)[] cases =
        [
            (InferenceProtocol.ChatCompletions, true,
                "data:{\"choices\":[{\"delta\":{\"reasoning_content\":\"hidden\"}}]}\n\ndata: {\"metadata\":{\"content\":\"not an answer\"},\n" +
                "data: \"choices\":[{\"delta\":{\"content\":\"ok\"}}]}\n\ndata: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata:[DONE]\n\n", "ok"),
            (InferenceProtocol.AnthropicMessages, true,
                "event: content_block_delta\ndata: {\"delta\":{\"text\":\"o\",\"type\":\"text_delta\"},\"type\":\"content_block_delta\"}\n\n" +
                "data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"thinking_delta\",\"thinking\":\"hidden\"}}\n\n" +
                "data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{}\",\"text\":\"not an answer\"}}\n\n" +
                "data: {\"type\":\"ping\"}\n\ndata: {\"type\":\"future_event\",\"content\":\"not an answer\"}\n\n" +
                "data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"k\"}}\n\n" +
                "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"}}\n\ndata: {\"type\":\"message_stop\"}\n\n", "ok"),
            (InferenceProtocol.ChatCompletions, false, """{"choices":[{"message":{"content":"","reasoning_content":"hidden"},"finish_reason":"stop"}]}""", null),
            (InferenceProtocol.AnthropicMessages, false, """{"type":"message","content":[{"type":"thinking","thinking":"hidden"}],"stop_reason":"end_turn"}""", null),
            (InferenceProtocol.ChatCompletions, false, """{"choices":[{"message":{"content":"partial"},"finish_reason":"length"}]}""", null),
            (InferenceProtocol.AnthropicMessages, false, """{"content":[{"type":"text","text":"partial"}],"stop_reason":"max_tokens"}""", null),
            (InferenceProtocol.AnthropicMessages, false, """{"content":[{"type":"text","text":"partial"}],"stop_reason":"tool_use"}""", null),
            (InferenceProtocol.ChatCompletions, true, "data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n", null),
            (InferenceProtocol.ChatCompletions, true, "data: {\"choices\":[{\"delta\":{\"content\":\"partial\"},\"finish_reason\":\"length\"}]}\n\ndata: [DONE]\n\n", null),
            (InferenceProtocol.AnthropicMessages, true, "data: {\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"Overloaded\"}}\n\n", null),
            (InferenceProtocol.ChatCompletions, true, "data: {\"error\":{\"code\":400,\"message\":\"unsupported parameter\",\"param\":\"thinking\"}}\n\n", null),
            (InferenceProtocol.ChatCompletions, true, "data: [DONE]\n\n", null),
            (InferenceProtocol.AnthropicMessages, true, "data: {\"type\":\"message_stop\"}\n\n", null),
            (InferenceProtocol.ChatCompletions, true, "data: {invalid}\n\ndata: [DONE]\n\n", null)
        ];
        foreach (var test in cases)
        {
            var settings = new SettingsService();
            settings.Current.Provider = ApiProvider.Foundry;
            settings.Current.AzureEndpoint = "https://sample.services.ai.azure.com";
            settings.Current.Protocol = test.Protocol;
            using var handler = new CompletionRequestHandler { ResponseBody = test.Body };
            using var client = new HttpClient(handler);
            var service = new OpenAiService(client, settings, new PromptBuilder());
            var result = new StringBuilder();
            var failed = false;
            try
            {
                if (test.Stream)
                {
                    await foreach (var chunk in service.StreamCompletionAsync("hello", LookupMode.Dictionary, "Korean", "English", ct))
                        result.Append(chunk);
                }
                else
                    result.Append(await service.GetCompletionAsync("hello", LookupMode.Dictionary, "Korean", "English", ct));
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException)
            {
                failed = true;
            }
            if (failed != (test.Expected is null) || (!failed && result.ToString() != test.Expected) || handler.RequestCount != 1)
                throw new InvalidOperationException($"Response contract mismatch: {test.Protocol}, stream={test.Stream}, expected={test.Expected ?? "error"}");
        }

        using var errorHandler = new CompletionRequestHandler
        {
            ResponseStatus = HttpStatusCode.BadRequest,
            ResponseBody = """{"error":{"message":"Unsupported value","type":"invalid_request_error","param":"thinking","code":"unsupported_value"}}"""
        };
        using var errorClient = new HttpClient(errorHandler);
        var errorService = new OpenAiService(errorClient, new SettingsService(), new PromptBuilder());
        try
        {
            await errorService.GetCompletionAsync("hello", LookupMode.Dictionary, "Korean", "English", ct);
            throw new InvalidOperationException("HTTP 400 was accepted.");
        }
        catch (HttpRequestException exception)
        {
            if (exception.StatusCode != HttpStatusCode.BadRequest || !exception.Message.Contains("thinking")
                || exception.Message.Contains("API Key") || errorHandler.RequestCount != 1)
                throw new InvalidOperationException("HTTP parameter errors lost their context or were retried.");
        }
        foreach (var stream in new[] { false, true })
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            using var handler = new CompletionRequestHandler();
            using var client = new HttpClient(handler);
            var service = new OpenAiService(client, new SettingsService(), new PromptBuilder());
            var cancelled = false;
            try
            {
                if (stream)
                {
                    await foreach (var chunk in service.StreamCompletionAsync("hello", LookupMode.Dictionary, "Korean", "English", cancellation.Token))
                        _ = chunk;
                }
                else
                    await service.GetCompletionAsync("hello", LookupMode.Dictionary, "Korean", "English", cancellation.Token);
            }
            catch (OperationCanceledException) { cancelled = true; }
            if (!cancelled) throw new InvalidOperationException("Cancellation was converted to a successful response.");
        }
        Console.WriteLine($"PASS: {cases.Length} response/SSE contracts, structured HTTP 400 handling, and both cancellation paths");
    }

    private static void RunSettingsContractTests()
    {
        var settings = new SettingsService();
        var current = settings.Current;
        current.Provider = ApiProvider.Foundry;
        current.Protocol = InferenceProtocol.AnthropicMessages;
        current.ReasoningMode = ReasoningMode.AnthropicBudgeted;
        current.ReasoningEffort = "default";
        current.MaxOutputTokens = 16384;
        current.ThinkingBudgetTokens = 2048;
        current.TokenLimitParameter = OutputTokenParameter.MaxTokens;
        current.InstructionRole = InstructionRole.User;
        current.Model = "production";
        current.AzureEndpoint = "https://sample.services.ai.azure.com";
        current.ApiKey = "never-persist-this";
        var snapshot = (AppSettings)typeof(SettingsService).GetMethod("CreateSaveSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(settings, null)!;
        var contextType = typeof(AppSettings).Assembly.GetType("VerbaCore.Models.SettingsJsonContext")!;
        var context = contextType.GetProperty("Default")!.GetValue(null);
        var typeInfo = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<AppSettings>)contextType.GetProperty("AppSettings")!.GetValue(context)!;
        var json = JsonSerializer.Serialize(snapshot, typeInfo);
        var restored = JsonSerializer.Deserialize(json, typeInfo)!;
        if (json.Contains("never-persist-this") || restored.Protocol != current.Protocol || restored.Provider != current.Provider
            || restored.ReasoningMode != current.ReasoningMode || restored.TokenLimitParameter != current.TokenLimitParameter
            || restored.ThinkingBudgetTokens != 2048 || restored.MaxOutputTokens != 16384
            || restored.InstructionRole != current.InstructionRole || restored.Model != "production")
            throw new InvalidOperationException("Saved API options were lost or exposed the plaintext key.");
        var legacy = JsonSerializer.Deserialize("""{"provider":1,"model":"existing-deployment","reasoningEffort":"low"}""", typeInfo)!;
        if (legacy.Provider != ApiProvider.AzureOpenAI || legacy.ReasoningMode is not null
            || legacy.Protocol != InferenceProtocol.ChatCompletions)
            throw new InvalidOperationException("Legacy settings were not preserved.");

        var originalKey = LookupCacheService.MakeKey(current, LookupMode.Dictionary, "Korean", "English", "Hello");
        if (originalKey != LookupCacheService.MakeKey(restored, LookupMode.Dictionary, "Korean", "English", " hello "))
            throw new InvalidOperationException("Cache identity changed after settings serialization or normalization.");
        Action<AppSettings>[] changes =
        [
            options => options.AzureEndpoint = "https://another.services.ai.azure.com",
            options => options.Protocol = InferenceProtocol.ChatCompletions,
            options => options.InstructionRole = InstructionRole.Developer,
            options => options.ReasoningMode = ReasoningMode.ModelDefault,
            options => options.ReasoningEffort = "high",
            options => options.TokenLimitParameter = OutputTokenParameter.MaxCompletionTokens,
            options => options.MaxOutputTokens = 32768,
            options => options.ThinkingBudgetTokens = 4096
        ];
        foreach (var change in changes)
        {
            var changed = JsonSerializer.Deserialize(json, typeInfo)!;
            change(changed);
            if (LookupCacheService.MakeKey(changed, LookupMode.Dictionary, "Korean", "English", "Hello") == originalKey)
                throw new InvalidOperationException("Different endpoint/protocol/request settings reused cached output.");
        }
        Console.WriteLine("PASS: source-generated settings round trip, legacy settings, key secrecy, and 8 cache boundaries");
    }

    private static int RunInputHookTests()
    {
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        using var capsLock = new CapsLockService();
        var hook = typeof(CapsLockService).GetMethod("HookCallbackCore", privateInstance)!;
        var data = Marshal.AllocHGlobal(32);
        try
        {
            Marshal.Copy(new byte[32], 0, data, 32);
            Marshal.WriteInt32(data, 0x14);
            var pressed = 0;
            var released = 0;
            var cancelled = 0;
            capsLock.CapsLockPressed += (_, _) => pressed++;
            capsLock.QuickTapReleased += (_, _) => released++;
            capsLock.LongPressReleased += (_, _) => released++;
            capsLock.HoldCancelled += (_, _) => cancelled++;
            for (var repeat = 0; repeat < 200; repeat++)
                hook.Invoke(capsLock, [0, new IntPtr(0x0100), data]);
            if (pressed != 1 || !capsLock.IsCapsDown)
                throw new InvalidOperationException("CapsLock auto-repeat reopened the popup.");
            hook.Invoke(capsLock, [0, new IntPtr(0x0101), data]);
            if (released != 1 || capsLock.IsCapsDown)
                throw new InvalidOperationException("CapsLock release was not handled exactly once.");

            hook.Invoke(capsLock, [0, new IntPtr(0x0100), data]);
            Marshal.WriteInt32(data, 0x1B);
            hook.Invoke(capsLock, [0, new IntPtr(0x0100), data]);
            Marshal.WriteInt32(data, 0x14);
            for (var repeat = 0; repeat < 200; repeat++)
                hook.Invoke(capsLock, [0, new IntPtr(0x0100), data]);
            hook.Invoke(capsLock, [0, new IntPtr(0x0101), data]);
            if (pressed != 2 || released != 1 || cancelled != 1 || capsLock.IsCapsDown)
                throw new InvalidOperationException("A cancelled hold reopened or released twice during key repeat.");

            hook.Invoke(capsLock, [0, new IntPtr(0x0100), data]);
            capsLock.TextInputWindow = Native.GetForegroundWindow();
            if (capsLock.TextInputWindow == IntPtr.Zero)
                throw new InvalidOperationException("Foreground window is unavailable for the text input routing check.");
            foreach (var key in new[] { 0x12, 0xA4, 0xA5, 0x15, 0x41, 0xE5 })
            {
                Marshal.WriteInt32(data, key);
                var handled = (IntPtr)hook.Invoke(capsLock, [0, new IntPtr(0x0100), data])!;
                if (handled != IntPtr.Zero || capsLock.Buffer.Length != 0)
                    throw new InvalidOperationException($"IME key {key:X2} was intercepted instead of reaching the focused TextBox.");
            }
            capsLock.UpdateTextInput("\uD55C\uAE00");
            if (capsLock.Buffer != "\uD55C\uAE00" || !capsLock.TypedWhileHeld)
                throw new InvalidOperationException("Composed text was not retained for hold release.");
            Marshal.WriteInt32(data, 0x14);
            hook.Invoke(capsLock, [0, new IntPtr(0x0101), data]);
            capsLock.TextInputWindow = IntPtr.Zero;

            capsLock.Install();
            var hookThread = (Thread)typeof(CapsLockService).GetField("_hookThread", privateInstance)!.GetValue(capsLock)!;
            if (hookThread.ManagedThreadId == Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Hooks run on the caller's UI thread.");
            var mouseHandle = typeof(CapsLockService).GetField("_mouseHookId", privateInstance)!;
            using var mouseProbe = new MouseInputProbe();
            mouseProbe.CheckMovement();
            var started = Stopwatch.GetTimestamp();
            capsLock.SetMouseMonitoring(true);
            if (!SpinWait.SpinUntil(() => (IntPtr)mouseHandle.GetValue(capsLock)! != IntPtr.Zero, 2000))
                throw new InvalidOperationException("Mouse hook installation needed the blocked UI thread.");
            mouseProbe.CheckMovement();
            capsLock.SetMouseMonitoring(false);
            if (!SpinWait.SpinUntil(() => (IntPtr)mouseHandle.GetValue(capsLock)! == IntPtr.Zero, 2000))
                throw new InvalidOperationException("Mouse hook removal needed the blocked UI thread.");
            Console.WriteLine($"PASS: 400 CapsLock repeats, cancellation latch, and mouse hook lifecycle without a UI message pump: {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms");
            Console.WriteLine("PASS: Alt, Hangul, and IME keys use the native text input path while held");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }
}

internal sealed class PopupTestApplication(bool offscreen, bool settingsOnly, bool focusOnly, bool selectionOnly, bool officeOnly, bool vscodeOnly) : Application
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly bool _offscreen = offscreen;
    private readonly bool _settingsOnly = settingsOnly;
    private readonly bool _focusOnly = focusOnly;
    private readonly bool _selectionOnly = selectionOnly;
    private readonly bool _officeOnly = officeOnly;
    private readonly bool _vscodeOnly = vscodeOnly;
    private readonly List<double> _latencies = [];

    internal void LoadResources()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Assembly.Load("Wpf.Ui");
        Assembly.Load("Markdig.Wpf");
        var definition = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "AppResources.xaml")).Root!;
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary = definition.Element(presentation + "Application.Resources")!.Elements().Single();
        foreach (var attribute in definition.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
        {
            var value = attribute.Value.StartsWith("clr-namespace:VerbaCore.", StringComparison.Ordinal)
                ? attribute.Value + ";assembly=VerbaCore"
                : attribute.Value;
            dictionary.SetAttributeValue(attribute.Name, value);
        }
        foreach (var element in dictionary.DescendantsAndSelf()
                     .Where(element => element.Name.NamespaceName.StartsWith("clr-namespace:VerbaCore.", StringComparison.Ordinal)))
        {
            element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=VerbaCore");
        }
        using var reader = dictionary.CreateReader();
        Resources = (ResourceDictionary)XamlReader.Load(reader);
    }

    protected override async void OnStartup(StartupEventArgs args)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            if (args.Args.Contains("--selection-browser"))
            {
                await RunBrowserSelectionTestsAsync(timeout.Token);
                Console.WriteLine("PASS: browser multi-paragraph selection retains all selected paragraphs without clipboard access");
                Shutdown(0);
                return;
            }
            if (_vscodeOnly)
            {
                await RunVsCodeSelectionTestsAsync(timeout.Token);
                Console.WriteLine("PASS: isolated VS Code selection and collapsed-selection checks; clipboard unchanged");
                Shutdown(0);
                return;
            }
            if (_officeOnly)
            {
                await RunOfficeSelectionTestsAsync(timeout.Token);
                Console.WriteLine("PASS: isolated Word and Excel selection fixtures; no documents saved");
                Shutdown(0);
                return;
            }
            if (_selectionOnly)
            {
                await RunSelectionTestsAsync(timeout.Token);
                Console.WriteLine("PASS: selected text is read through real UI Automation without clipboard access");
                Shutdown(0);
                return;
            }
            if (_focusOnly)
            {
                await RunFocusTestsAsync(timeout.Token);
                Console.WriteLine("PASS: quick-tap foreground recovery, native keyboard input, stale-focus rejection, and focus cancellation; no mouse clicks");
                Shutdown(0);
                return;
            }
            if (_settingsOnly)
            {
                await RunSettingsViewTestsAsync(timeout.Token);
                Console.WriteLine("PASS: localized settings UI, API option bindings, model preservation, and invalid-save rejection; no user settings written");
                Shutdown(0);
                return;
            }
            await RunTestsAsync(timeout.Token);
            Console.WriteLine(_offscreen
                ? "PASS: offscreen rendering and working-set eviction; desktop focus, input, and presentation NOT tested"
                : "PASS: popup rendering, foreground focus, input, and reopen checks");
            Shutdown(0);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            Shutdown(1);
        }
    }

    private async Task RunBrowserSelectionTestsAsync(CancellationToken ct)
    {
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft", "Edge", "Application", "msedge.exe");
        Require(File.Exists(executable), "Microsoft Edge is required for the isolated multi-paragraph fixture.");
        var name = "VerbaCore-paragraphs-" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), name);
        Directory.CreateDirectory(directory);
        var page = Path.Combine(directory, "selection.html");
        await File.WriteAllTextAsync(page, $$"""
            <!doctype html><html><head><meta charset="utf-8"><title>{{name}}</title></head>
            <body><div id="editor" contenteditable="true">
            <p id="first">before First selected paragraph.</p>
            <p><br></p>
            <p>Second selected paragraph with <strong>formatted text</strong>.</p>
            <p><br></p>
            <p id="last">Third selected paragraph. after</p>
            </div><p>This paragraph is not selected.</p>
            <script>
            addEventListener('load', () => {
                document.getElementById('editor').focus();
                const range = document.createRange();
                range.setStart(document.getElementById('first').firstChild, 7);
                range.setEnd(document.getElementById('last').firstChild, 25);
                const selection = getSelection();
                selection.removeAllRanges();
                selection.addRange(range);
                document.title = '{{name}} ready';
            });
            </script></body></html>
            """, ct);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var argument in new[] { "--user-data-dir=" + Path.Combine(directory, "profile"),
                     "--no-first-run", "--no-default-browser-check", "--disable-extensions",
                     "--force-renderer-accessibility", "--app=" + new Uri(page).AbsoluteUri })
            start.ArgumentList.Add(argument);
        using var launched = Process.Start(start)!;
        Process? host = null;
        using var service = new CursorTextService();
        try
        {
            await WaitUntilAsync(() =>
            {
                foreach (var process in Process.GetProcessesByName("msedge"))
                {
                    if (process.MainWindowTitle.Contains(name + " ready", StringComparison.Ordinal))
                    {
                        host = process;
                        return true;
                    }
                    process.Dispose();
                }
                return false;
            }, ct, TimeSpan.FromSeconds(30));
            var clipboardSequence = Native.GetClipboardSequenceNumber();
            var text = await service.GetSelectedTextAsync(host!.MainWindowHandle, ct).WaitAsync(ct);
            Console.WriteLine($"Browser selection length={text?.Length ?? 0}; second={text?.Contains("Second selected paragraph with formatted text.")}; third={text?.Contains("Third selected paragraph.")}");
            Require(text is not null && text.StartsWith("First selected paragraph.\n", StringComparison.Ordinal)
                && text.Contains("\nSecond selected paragraph with formatted text.\n", StringComparison.Ordinal)
                && text.EndsWith("\nThird selected paragraph.", StringComparison.Ordinal)
                && !text.Contains("before", StringComparison.Ordinal) && !text.Contains("after", StringComparison.Ordinal)
                && !text.Contains("not selected", StringComparison.Ordinal),
                "Browser selection did not include exactly all three selected paragraphs.");
            Require(Native.GetClipboardSequenceNumber() == clipboardSequence, "The multi-paragraph lookup changed the clipboard.");
        }
        finally
        {
            if (host != null)
            {
                if (!host.HasExited) host.Kill(entireProcessTree: true);
                host.Dispose();
            }
            if (!launched.HasExited) launched.Kill(entireProcessTree: true);
        }
    }

    private async Task RunVsCodeSelectionTestsAsync(CancellationToken ct)
    {
        var executable = Environment.GetEnvironmentVariable("VERBACORE_TEST_VSCODE_PATH")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Microsoft VS Code", "Code.exe");
        Require(File.Exists(executable), "Set VERBACORE_TEST_VSCODE_PATH to a VS Code executable to run this fixture.");
        var name = "VerbaCore-selection-" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), name);
        var extension = Path.Combine(directory, "extension");
        Directory.CreateDirectory(extension);
        var userDirectory = Path.Combine(directory, "profile", "User");
        Directory.CreateDirectory(userDirectory);
        var screenReaderMode = Environment.GetEnvironmentVariable("VERBACORE_TEST_VSCODE_ACCESSIBILITY") != "auto";
        await File.WriteAllTextAsync(Path.Combine(userDirectory, "settings.json"), screenReaderMode
            ? """{"editor.accessibilitySupport":"on","telemetry.telemetryLevel":"off","update.mode":"none","workbench.startupEditor":"none"}"""
            : """{"editor.accessibilitySupport":"auto","telemetry.telemetryLevel":"off","update.mode":"none","workbench.startupEditor":"none"}""", ct);
        Console.WriteLine("VS Code accessibility mode: " + (screenReaderMode ? "on" : "auto"));
        var statusFile = Path.Combine(directory, "status.txt");
        var controlFile = Path.Combine(directory, "control.txt");
        const string expected = "selected VS Code text";
        await File.WriteAllTextAsync(Path.Combine(directory, name + ".txt"), "before " + expected + " after", ct);
        await File.WriteAllTextAsync(controlFile, "selected", ct);
        await File.WriteAllTextAsync(Path.Combine(extension, "package.json"), """
            {"name":"verbacore-selection-fixture","publisher":"verbacore-test","version":"0.0.1",
             "engines":{"vscode":"^1.85.0"},"activationEvents":["*"],"main":"./extension.js"}
            """, ct);
        await File.WriteAllTextAsync(Path.Combine(extension, "extension.js"), $$"""
            const vscode = require('vscode');
            const fs = require('fs');
            const path = require('path');
            exports.activate = async context => {
                const root = path.join(__dirname, '..');
                const document = await vscode.workspace.openTextDocument(vscode.Uri.file(path.join(root, '{{name}}.txt')));
                const editor = await vscode.window.showTextDocument(document);
                const control = path.join(root, 'control.txt');
                const select = async () => {
                    const state = fs.readFileSync(control, 'utf8');
                    editor.selection = new vscode.Selection(0, 7, 0, state === 'clear' ? 7 : 28);
                    await vscode.commands.executeCommand('workbench.action.focusActiveEditorGroup');
                    fs.writeFileSync(path.join(root, 'status.txt'), state);
                };
                fs.watchFile(control, { interval: 50 }, select);
                context.subscriptions.push({ dispose: () => fs.unwatchFile(control) });
                await select();
            };
            """, ct);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        start.Environment.Remove("ELECTRON_RUN_AS_NODE");
        foreach (var argument in new[] { "--new-window", "--user-data-dir", Path.Combine(directory, "profile"),
                     "--extensions-dir", Path.Combine(directory, "extensions"), "--extensionDevelopmentPath=" + extension,
                     "--disable-extensions", "--disable-workspace-trust", "--skip-welcome", "--skip-release-notes" })
            start.ArgumentList.Add(argument);
        using var launched = Process.Start(start)!;
        Process? host = null;
        using var service = new CursorTextService();
        try
        {
            await WaitUntilAsync(() => File.Exists(statusFile), ct, TimeSpan.FromSeconds(50));
            await WaitUntilAsync(() =>
            {
                foreach (var process in Process.GetProcessesByName("Code"))
                {
                    if (process.MainWindowTitle.Contains(name, StringComparison.Ordinal))
                    {
                        host = process;
                        return true;
                    }
                    process.Dispose();
                }
                return false;
            }, ct);
            var handle = host!.MainWindowHandle;
            if (!Native.SetForegroundWindow(handle))
            {
                typeof(OverlayWindow).Assembly.GetType("VerbaCore.Helpers.NativeMethods")!
                    .GetMethod("TryUnlockForegroundForQuickTap", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
                Native.SetForegroundWindow(handle);
            }
            await WaitUntilAsync(() => Native.GetForegroundWindow() == handle, ct);
            await File.WriteAllTextAsync(controlFile, "selected-focused", ct);
            await WaitUntilAsync(() => File.ReadAllText(statusFile) == "selected-focused", ct);
            var clipboardSequence = Native.GetClipboardSequenceNumber();
            var started = Stopwatch.GetTimestamp();
            var text = await service.GetSelectedTextAsync(handle, ct).WaitAsync(ct);
            Console.WriteLine($"VS Code cold selection: {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms; matched={text == expected}; length={text?.Length ?? 0}; objectMarkers={text?.Count(character => character == '\uFFFC') ?? 0}");
            if (text != expected) await DiagnoseSelectionAsync(service, host.MainWindowHandle, ct);
            Require(text == expected, "VS Code did not expose the exact selected substring with the configured accessibility setting.");
            var popupInput = new TextBox { Text = "unrelated popup text" };
            var popup = new Window
            {
                Content = popupInput, Width = 300, Height = 120, Left = -32000, Top = -32000,
                ShowInTaskbar = false
            };
            var queue = (System.Collections.Concurrent.BlockingCollection<Action>)typeof(CursorTextService)
                .GetField("_queue", PrivateInstance)!.GetValue(service)!;
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Add(() => { entered.TrySetResult(); release.Wait(); }, ct);
            try
            {
                await entered.Task.WaitAsync(ct);
                var captured = service.GetSelectedTextAsync(handle, ct);
                popup.Show();
                popup.Activate();
                popupInput.Focus();
                popupInput.SelectAll();
                Require(popupInput.IsKeyboardFocused, "The popup fixture did not acquire keyboard focus.");
                release.Set();
                text = await captured.WaitAsync(ct);
                Require(text == expected, "VS Code selection was lost after the popup took keyboard focus.");
                Console.WriteLine("VS Code: captured-window selection survived popup focus");
            }
            finally
            {
                release.Set();
                popup.Close();
            }
            await File.WriteAllTextAsync(controlFile, "clear", ct);
            await WaitUntilAsync(() => File.ReadAllText(statusFile) == "clear", ct);
            text = await service.GetSelectedTextAsync(host.MainWindowHandle, ct).WaitAsync(ct);
            Require(text == null, "VS Code returned unrelated text for a collapsed selection.");
            Require(Native.GetClipboardSequenceNumber() == clipboardSequence, "The clipboard changed during the selection test.");
        }
        finally
        {
            if (host != null)
            {
                if (!host.HasExited) host.Kill(entireProcessTree: true);
                host.Dispose();
            }
            if (!launched.HasExited) launched.Kill(entireProcessTree: true);
        }
    }

    private static Task DiagnoseSelectionAsync(CursorTextService service, IntPtr window, CancellationToken ct)
    {
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = (System.Collections.Concurrent.BlockingCollection<Action>)typeof(CursorTextService)
            .GetField("_queue", PrivateInstance)!.GetValue(service)!;
        queue.Add(() =>
        {
            try
            {
                var assembly = typeof(CursorTextService).Assembly;
                var automation = typeof(CursorTextService).GetField("_uia", PrivateInstance)!.GetValue(service)!;
                var automationType = assembly.GetType("VerbaCore.Helpers.IUIAutomation")!;
                var elementType = assembly.GetType("VerbaCore.Helpers.IUIAutomationElement")!;
                var walkerType = assembly.GetType("VerbaCore.Helpers.IUIAutomationTreeWalker")!;
                var property = elementType.GetMethod("GetCurrentPropertyValue")!;
                var walker = automationType.GetProperty("RawViewWalker")!.GetValue(automation)!;
                var root = automationType.GetMethod("ElementFromHandle")!.Invoke(automation, [window])!;
                var pending = new Queue<(object Element, int Depth)>();
                pending.Enqueue((root, 0));
                var visited = 0;
                while (pending.TryDequeue(out var node) && visited++ < 256 && !ct.IsCancellationRequested)
                {
                    var supportsText = property.Invoke(node.Element, [30040]);
                    var focused = property.Invoke(node.Element, [30008]);
                    if (node.Depth < 4 || Equals(supportsText, true) || Equals(focused, true))
                    {
                        var selected = typeof(CursorTextService).GetMethod("TryGetSelectionText", BindingFlags.Static | BindingFlags.NonPublic)!
                            .Invoke(null, [node.Element]) as string;
                        Console.WriteLine($"UIA depth={node.Depth}; type={property.Invoke(node.Element, [30003])}; class={property.Invoke(node.Element, [30012])}; textPattern={supportsText}; focused={focused}; selectedLength={selected?.Length ?? 0}");
                    }
                    if (node.Depth >= 24) continue;
                    var child = walkerType.GetMethod("GetFirstChildElement")!.Invoke(walker, [node.Element]);
                    while (child != null && pending.Count + visited < 256)
                    {
                        pending.Enqueue((child, node.Depth + 1));
                        child = walkerType.GetMethod("GetNextSiblingElement")!.Invoke(walker, [child]);
                    }
                }
                var targetType = assembly.GetType("VerbaCore.Helpers.SelectionTarget")!;
                var target = targetType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [window]);
                var interop = assembly.GetType("VerbaCore.Helpers.SelectionInterop")!;
                var windows = (List<IntPtr>)interop.GetMethod("CandidateWindows", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [target])!;
                var serviceType = assembly.GetType("VerbaCore.Helpers.IAccessibleServiceProvider")!;
                foreach (var handle in windows.Take(8))
                {
                    object?[] arguments = [handle, 0xFFFFFFFCU, new Guid("618736E0-3C3D-11CF-810C-00AA00389B71"), null];
                    var result = interop.GetMethod("AccessibleObjectFromWindow", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, arguments);
                    var native = arguments[3];
                    Console.WriteLine($"MSAA hwnd={handle}; class={interop.GetMethod("ClassName", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [handle])}; hr={result}; serviceProvider={native != null && serviceType.IsInstanceOfType(native)}");
                    if (native == null || !serviceType.IsInstanceOfType(native)) continue;
                    foreach (var interfaceId in new[] { "E89F726E-C4F4-4C19-BB19-B647D7FA8478", "24FD2FFB-3AAD-4A08-8335-A3AD89C0FB4B" })
                    {
                        object[] query = [arguments[2]!, new Guid(interfaceId), IntPtr.Zero];
                        var status = serviceType.GetMethod("QueryService")!.Invoke(native, query);
                        Console.WriteLine($"MSAA interface={interfaceId}; hr={status}");
                        if ((IntPtr)query[2] != IntPtr.Zero) Marshal.Release((IntPtr)query[2]);
                    }
                }
                complete.TrySetResult();
            }
            catch (Exception exception)
            {
                Console.WriteLine("Selection diagnostics interrupted: " + exception.GetType().Name);
                complete.TrySetResult();
            }
        }, ct);
        return complete.Task.WaitAsync(ct);
    }

    private async Task RunOfficeSelectionTestsAsync(CancellationToken ct)
    {
        Require(Process.GetProcessesByName("WINWORD").Length == 0 && Process.GetProcessesByName("EXCEL").Length == 0,
            "Close Word and Excel before running the isolated Office fixtures.");
        using var service = new CursorTextService();
        dynamic? word = null;
        dynamic? document = null;
        dynamic? excel = null;
        dynamic? workbook = null;
        var popupInput = new TextBox { Text = "unrelated popup selection" };
        var popup = new Window
        {
            Content = popupInput, Width = 300, Height = 120, Left = -32000, Top = -32000,
            ShowInTaskbar = false
        };
        try
        {
            word = Activator.CreateInstance(Type.GetTypeFromProgID("Word.Application", true)!);
            word!.Visible = true;
            document = word.Documents.Add();
            const string selectedWord = "Selected Word paragraph.\n\nSecond paragraph with a blank line.\n\nThird paragraph \uD55C\uAE00.";
            document.Content.Text = "before " + selectedWord.Replace('\n', '\r') + " after";
            document.Range(7, 7 + selectedWord.Length).Select();
            var handle = new IntPtr((int)word.ActiveWindow.Hwnd);
            popup.Show();
            popup.Activate();
            popupInput.Focus();
            popupInput.SelectAll();
            var clipboardSequence = Native.GetClipboardSequenceNumber();
            var text = await service.GetSelectedTextAsync(handle, ct).WaitAsync(ct);
            Require(text == selectedWord, "Word did not return every selected paragraph with its blank lines.");
            Require(Native.GetClipboardSequenceNumber() == clipboardSequence, "Word selection lookup changed the clipboard.");
            Console.WriteLine("Word: all selected paragraphs and blank lines passed");
            document.Range(7, 7).Select();
            text = await service.GetSelectedTextAsync(handle, ct).WaitAsync(ct);
            Require(text == null, "Word returned document text for a collapsed selection.");
            document.Close(0);
            document = null;
            word.Quit(0);
            word = null;

            excel = Activator.CreateInstance(Type.GetTypeFromProgID("Excel.Application", true)!);
            excel!.Visible = true;
            workbook = excel.Workbooks.Add();
            dynamic sheet = workbook.Worksheets[1];
            sheet.Range["A1:B2"].Value2 = new object[,] { { "first", "second" }, { "third", "fourth" } };
            sheet.Range["A1:B2"].Select();
            handle = new IntPtr((int)excel.ActiveWindow.Hwnd);
            popup.Activate();
            popupInput.Focus();
            popupInput.SelectAll();
            clipboardSequence = Native.GetClipboardSequenceNumber();
            text = await service.GetSelectedTextAsync(handle, ct).WaitAsync(ct);
            Require(text == "first\tsecond\nthird\tfourth", "Excel did not return the selected rectangle as formatted TSV.");
            Require(Native.GetClipboardSequenceNumber() == clipboardSequence, "Excel selection lookup changed the clipboard.");
            Console.WriteLine("Excel: selected cell rectangle passed");
            sheet.Range["D1"].Select();
            text = await service.GetSelectedTextAsync(handle, ct).WaitAsync(ct);
            Require(text == null, "An empty Excel cell returned unrelated text.");
        }
        finally
        {
            popup.Close();
            if (document != null) document.Close(0);
            if (word != null) word.Quit(0);
            if (workbook != null) workbook.Close(false);
            if (excel != null) excel.Quit();
        }
    }

    private async Task RunSelectionTestsAsync(CancellationToken ct)
    {
        const string selected = "selected text \uD55C\uAE00\n\nSecond paragraph with multiple words.\n\nThird paragraph.";
        var input = new TextBox { Text = "before " + selected + " after" };
        var host = new Window
        {
            Title = "VerbaCore selection fixture", Content = input,
            Width = 360, Height = 180, Left = -32000, Top = -32000,
            ShowActivated = false, ShowInTaskbar = false
        };
        var unrelatedInput = new TextBox { Text = "selection in another window" };
        var unrelated = new Window
        {
            Title = "VerbaCore unrelated selection fixture", Content = unrelatedInput,
            Width = 360, Height = 180, Left = -32000, Top = -32000, ShowInTaskbar = false
        };
        using var service = new CursorTextService();
        try
        {
            host.Show();
            input.Select("before ".Length, selected.Length);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, ct);
            var handle = new WindowInteropHelper(host).Handle;
            var text = await service.GetSelectedTextAsync(handle, ct).WaitAsync(ct);
            Require(text == selected, "UI Automation did not return exactly the highlighted text from the requested window.");
            unrelated.Show();
            unrelated.Activate();
            unrelatedInput.Focus();
            unrelatedInput.SelectAll();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, ct);
            Require(unrelatedInput.IsKeyboardFocused, "The unrelated selection fixture could not take keyboard focus.");
            text = await service.GetSelectedTextAsync(handle, ct).WaitAsync(ct);
            Require(text == selected, "Selection lookup followed focus into a different window.");
            input.Select(0, 0);
            text = await service.GetSelectedTextAsync(handle, ct).WaitAsync(ct);
            Require(text == null, "A caret-only range must not return document text.");

            input.Text = new string('x', 1999) + "\uD83D\uDE00tail";
            input.SelectAll();
            text = await service.GetSelectedTextAsync(handle, ct).WaitAsync(ct);
            Require(text == new string('x', 1999), "Selection length limiting split a Unicode surrogate pair.");

            host.Content = new PasswordBox { Password = "never-extract-this" };
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, ct);
            text = await service.GetSelectedTextAsync(handle, ct).WaitAsync(ct);
            Require(text == null, "A password control exposed selected text.");
            host.Content = input;
            input.Text = selected;
            input.SelectAll();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, ct);

            var reader = typeof(CursorTextService).Assembly.GetType("VerbaCore.Services.OfficeSelectionReader")!;
            using (var scope = (IDisposable)Activator.CreateInstance(reader.GetNestedType("ComScope", BindingFlags.NonPublic)!, true)!)
            {
                var read = reader.GetMethod("ReadPowerPoint", BindingFlags.NonPublic | BindingFlags.Static)!;
                Require((string?)read.Invoke(null, [new { Selection = new { Type = 3, TextRange = new { Text = selected } } }, scope]) == selected,
                    "PowerPoint text selection contract failed.");
                Require(read.Invoke(null, [new { Selection = new { Type = 2 } }, scope]) == null,
                    "A PowerPoint shape-only selection was treated as highlighted text.");
            }

            var queue = (System.Collections.Concurrent.BlockingCollection<Action>)typeof(CursorTextService)
                .GetField("_queue", PrivateInstance)!.GetValue(service)!;
            using var release = new ManualResetEventSlim();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Add(() => { started.TrySetResult(); release.Wait(); }, ct);
            try
            {
                await started.Task.WaitAsync(ct);
                using var canceled = new CancellationTokenSource();
                var pending = service.GetSelectedTextAsync(handle, canceled.Token);
                canceled.Cancel();
                Require(await pending.WaitAsync(TimeSpan.FromSeconds(1), ct) == null, "Cancellation waited for a blocked provider.");
                var stale = service.GetSelectedTextAsync(handle, ct);
                var latest = service.GetSelectedTextAsync(handle, ct);
                Require(await stale.WaitAsync(TimeSpan.FromSeconds(1), ct) == null, "A newer request did not cancel the stale lookup.");
                Require(await latest.WaitAsync(TimeSpan.FromMilliseconds(CursorTextService.SelectionTimeoutMs + 1000), ct) == null,
                    "A blocked provider did not respect the public request deadline.");
                pending = service.GetSelectedTextAsync(handle, ct);
                var timer = Stopwatch.StartNew();
                service.Dispose();
                Require(timer.ElapsedMilliseconds < 250, "Disposal blocked the caller on a provider thread.");
                Require(await pending.WaitAsync(TimeSpan.FromSeconds(1), ct) == null, "Disposal left a selection request pending.");
                Require(await service.GetSelectedTextAsync(handle, ct) == null, "A disposed selection service accepted work.");
            }
            finally { release.Set(); }
            var worker = (Thread)typeof(CursorTextService).GetField("_worker", PrivateInstance)!.GetValue(service)!;
            Require(await Task.Run(() => worker.Join(TimeSpan.FromSeconds(2)), ct), "The selection worker did not stop after its provider returned.");
            Console.WriteLine("Selection safety: password exclusion, Unicode limit, PowerPoint contract, cancellation, supersession, timeout, and shutdown passed");
        }
        finally
        {
            unrelated.Close();
            host.Close();
        }
    }

    private async Task RunSettingsViewTestsAsync(CancellationToken ct)
    {
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
        string[] labelKeys =
        [
            "Settings_ApiProtocol", "Settings_RequestOptions", "Settings_ModelDefaults", "Settings_InstructionRole",
            "Settings_TokenParameter", "Settings_MaxOutputTokens", "Settings_ReasoningMode", "Settings_ThinkingBudget"
        ];
        using var bindingOutput = new StringWriter();
        using var bindingListener = new TextWriterTraceListener(bindingOutput);
        var bindingTrace = PresentationTraceSources.DataBindingSource;
        var priorLevel = bindingTrace.Switch.Level;
        bindingTrace.Switch.Level = SourceLevels.Error;
        bindingTrace.Listeners.Add(bindingListener);
        try
        {
            foreach (var language in new[] { "ko", "en", "ja", "zh" })
            {
                var dictionary = new ResourceDictionary
                {
                    Source = new Uri($"/VerbaCore;component/Resources/Strings.{language}.xaml", UriKind.Relative)
                };
                Resources.MergedDictionaries.Add(dictionary);
                Window? window = null;
                try
                {
                    Require(labelKeys.All(key => dictionary[key] is string { Length: > 0 }), "An API settings translation is missing.");
                    var settings = new SettingsService();
                    settings.Current.Provider = ApiProvider.Foundry;
                    settings.Current.Protocol = InferenceProtocol.AnthropicMessages;
                    settings.Current.Model = "my-production-deployment";
                    settings.Current.AzureEndpoint = "https://sample.services.ai.azure.com";
                    settings.Current.ReasoningMode = ReasoningMode.AnthropicBudgeted;
                    settings.Current.ThinkingBudgetTokens = 2048;
                    var viewModel = new SettingsViewModel(settings, new LocalizationService(), new LookupCacheService(settings));
                    Require(viewModel.IsAzure && viewModel.IsMessages && !viewModel.IsLegacyAzure && viewModel.ShowThinkingBudget,
                        "Foundry Messages settings were not restored.");
                    var view = new SettingsView { DataContext = viewModel };
                    window = new Wpf.Ui.Controls.FluentWindow
                    {
                        Content = view, Width = 620, Height = 900, Left = -32000, Top = -32000,
                        ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None
                    };
                    window.Show();
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, ct);
                    var expander = Descendants<Expander>(view).Single();
                    Require(!expander.IsExpanded, "Advanced settings are not collapsed by default.");
                    expander.IsExpanded = true;
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, ct);

                    var protocolSelector = Descendants<ComboBox>(view).Single(control =>
                        System.Windows.Data.BindingOperations.GetBindingExpression(control,
                            System.Windows.Controls.Primitives.Selector.SelectedValueProperty)?.ParentBinding.Path.Path == "Protocol");
                    Require(Equals(protocolSelector.SelectedValue, InferenceProtocol.AnthropicMessages), "Protocol selector binding failed.");
                    protocolSelector.SelectedValue = InferenceProtocol.ChatCompletions;
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, ct);
                    Require(!viewModel.IsMessages && viewModel.SelectedReasoningMode == ReasoningMode.ModelDefault,
                        "Protocol switching retained incompatible reasoning settings.");
                    viewModel.SelectedReasoningMode = ReasoningMode.OpenAiEffort;
                    viewModel.ReasoningEffort = "high";
                    viewModel.SelectedModel = "another-deployment";
                    Require(viewModel.SelectedReasoningMode == ReasoningMode.ModelDefault && viewModel.ReasoningEffort == "default",
                        "Changing deployment retained model-specific overrides.");
                    viewModel.SelectedProvider = "OpenAI";
                    viewModel.SelectedModel = "custom-model-not-in-the-list";
                    var copied = new AppSettings();
                    typeof(SettingsViewModel).GetMethod("ApplyRequestSettings", PrivateInstance)!.Invoke(viewModel, [copied]);
                    var copiedService = new SettingsService();
                    copiedService.Current.Model = copied.Model;
                    var reopened = new SettingsViewModel(copiedService, new LocalizationService(), new LookupCacheService(copiedService));
                    Require(reopened.SelectedModel == "custom-model-not-in-the-list", "A custom model was replaced when opening settings.");

                    viewModel.SelectedProvider = "Microsoft Foundry";
                    viewModel.SelectedModel = "my-production-deployment";
                    viewModel.Protocol = InferenceProtocol.AnthropicMessages;
                    viewModel.SelectedReasoningMode = ReasoningMode.AnthropicBudgeted;
                    viewModel.MaxOutputTokens = 1000;
                    viewModel.ThinkingBudgetTokens = 2048;
                    await viewModel.SaveSettingsCommand.ExecuteAsync(null);
                    Require(viewModel.StatusMessage.Length > 0 && settings.Current.MaxOutputTokens == 8192,
                        "An invalid thinking budget mutated or saved current settings.");
                    viewModel.StatusMessage = string.Empty;
                    viewModel.MaxOutputTokens = 8192;

                    foreach (var width in new[] { 480.0, 760.0 })
                    {
                        window.Width = width;
                        await Dispatcher.InvokeAsync(() => view.UpdateLayout(), DispatcherPriority.ApplicationIdle, ct);
                        Require(viewModel.SelectedModel == "my-production-deployment"
                            && viewModel.SelectedReasoningMode == ReasoningMode.AnthropicBudgeted, "UI binding changed the selected model/options.");
                        var labels = labelKeys.Select(key => (string)dictionary[key]).ToHashSet();
                        foreach (var label in Descendants<TextBlock>(view).Where(label => label.IsVisible && labels.Contains(label.Text)))
                        {
                            var origin = label.TransformToAncestor(view).Transform(new Point());
                            Require(origin.X >= -1 && origin.X + label.ActualWidth <= view.ActualWidth + 1,
                                $"A settings label overflowed horizontally: {language}/{width}/{label.Text}");
                        }
                        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(window);
                        var backgroundPixel = new byte[4];
                        bitmap.CopyPixels(new Int32Rect(50, 80, 1, 1), backgroundPixel, 4, 0);
                        Require(backgroundPixel[3] == 255, "Settings snapshot omitted the parent window background.");
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        var path = Path.Combine(Path.GetTempPath(), $"VerbaCore-settings-{language}-{width:0}.png");
                        using var output = File.Create(path);
                        encoder.Save(output);
                        Console.WriteLine("Settings snapshot: " + path);
                    }
                }
                finally
                {
                    window?.Close();
                    Resources.MergedDictionaries.Remove(dictionary);
                }
            }
            bindingListener.Flush();
            Require(bindingOutput.ToString().Length == 0, "Settings binding errors: " + bindingOutput);
        }
        finally
        {
            bindingTrace.Listeners.Remove(bindingListener);
            bindingTrace.Switch.Level = priorLevel;
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private async Task RunFocusTestsAsync(CancellationToken ct)
    {
        var nativeMethods = typeof(OverlayWindow).Assembly.GetType("VerbaCore.Helpers.NativeMethods")!;
        var desktopCheck = nativeMethods.GetMethod("IsInputDesktopCurrent", BindingFlags.Static | BindingFlags.NonPublic)!;
        Require((bool)desktopCheck.Invoke(null, null)!, "Focus tests require the current unlocked input desktop.");
        var eventName = "Local\\VerbaCore.FocusTests." + Guid.NewGuid().ToString("N");
        using var locked = new EventWaitHandle(false, EventResetMode.ManualReset, eventName + ".blocked");
        using var release = new EventWaitHandle(false, EventResetMode.ManualReset, eventName + ".release");
        using var host = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
        {
            Arguments = "--host " + eventName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        })!;
        OverlayWindow? overlay = null;
        var hostHandle = IntPtr.Zero;
        using var capsLock = new CapsLockService();
        try
        {
            hostHandle = new IntPtr(long.Parse((await host.StandardOutput.ReadLineAsync(ct))!, CultureInfo.InvariantCulture));
            if (!Native.SetForegroundWindow(hostHandle))
            {
                nativeMethods.GetMethod("TryUnlockForegroundForQuickTap", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
                await WaitUntilAsync(() =>
                {
                    Native.SetForegroundWindow(hostHandle);
                    return Native.GetForegroundWindow() == hostHandle;
                }, ct);
            }
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/VerbaCore;component/Resources/Strings.ko.xaml", UriKind.Relative)
            });
            var settings = new SettingsService();
            var history = new HistoryService();
            var ai = new NoNetworkService();
            overlay = new OverlayWindow(ai, settings, history, capsLock, new CursorTextService(), new LookupCacheService(settings));
            typeof(CapsLockService).GetProperty(nameof(CapsLockService.ForegroundWindowAtPress))!.SetValue(capsLock, hostHandle);
            overlay.PreWarm();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, ct);
            var input = (TextBox)overlay.FindName("InputTextBox");
            var handle = new WindowInteropHelper(overlay).Handle;
            var focusMethod = typeof(OverlayWindow).GetMethod("EnsureQuickTapFocusAsync", PrivateInstance)!;
            var gestureField = typeof(OverlayWindow).GetField("_inputGesture", PrivateInstance)!;

            for (var iteration = 0; iteration < 3; iteration++)
            {
                Native.SetForegroundWindow(hostHandle);
                await WaitUntilAsync(() => Native.GetForegroundWindow() == hostHandle, ct);
                Raise(capsLock, nameof(CapsLockService.CapsLockPressed));
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, ct);
                Native.SetForegroundWindow(hostHandle);
                await WaitUntilAsync(() => Native.GetForegroundWindow() == hostHandle, ct);
                locked.Reset();
                Native.PostMessage(hostHandle, Program.LockForegroundMessage, IntPtr.Zero, IntPtr.Zero);
                Require(await Task.Run(() => locked.WaitOne(TimeSpan.FromSeconds(2)), ct), "Foreground fixture could not lock activation.");
                await WaitUntilAsync(() => Native.GetForegroundWindow() == hostHandle && !input.IsKeyboardFocused, ct);
                typeof(OverlayWindow).GetField("_selectedTextTask", PrivateInstance)!.SetValue(overlay, Task.FromResult<string?>(null));
                var started = Stopwatch.GetTimestamp();
                Raise(capsLock, nameof(CapsLockService.QuickTapReleased));
                await WaitUntilAsync(() => Native.GetForegroundWindow() == handle && Native.GetFocus() == handle && input.IsKeyboardFocused, ct);
                Console.WriteLine($"Quick tap {iteration + 1}: verified foreground and keyboard focus in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms");
                Native.keybd_event(0x31, 0x02, 0, UIntPtr.Zero);
                Native.keybd_event(0x31, 0x02, 2, UIntPtr.Zero);
                await WaitUntilAsync(() => input.Text.Contains('1'), ct);
                Require(capsLock.TextInputWindow == handle, "Native IME input routing was not enabled after activation.");

                var gesture = (long)gestureField.GetValue(overlay)!;
                Native.SetFocus(IntPtr.Zero);
                await (Task)focusMethod.Invoke(overlay, [gesture - 1, ct])!;
                Require(Native.GetFocus() == IntPtr.Zero, "An old focus request took keyboard focus.");
                await (Task)focusMethod.Invoke(overlay, [gesture, ct])!;
                Require(Native.GetFocus() == handle && input.IsKeyboardFocused, "Native focus was not restored in the foreground popup.");
                input.Focusable = false;
                Native.SetFocus(IntPtr.Zero);
                var deferred = (Task)focusMethod.Invoke(overlay, [gesture, ct])!;
                Require(!deferred.IsCompleted, "The delayed-focus check did not start a retry.");
                input.Focusable = true;
                await deferred;
                Require(Native.GetFocus() == handle && input.IsKeyboardFocused, "Input did not get focus after becoming ready.");
                Native.SetFocus(IntPtr.Zero);
                var lookupField = typeof(OverlayWindow).GetField("_isLookupInProgress", PrivateInstance)!;
                lookupField.SetValue(overlay, true);
                try
                {
                    await (Task)focusMethod.Invoke(overlay, [gesture, ct])!;
                    Require(Native.GetFocus() == handle && input.IsKeyboardFocused, "A previous lookup's cancellation blocked new input focus.");
                }
                finally
                {
                    lookupField.SetValue(overlay, false);
                }
                if (iteration < 2) await HideAsync(overlay, ct);
            }

            var unlock = nativeMethods.GetMethod("TryUnlockForegroundForQuickTap", BindingFlags.Static | BindingFlags.NonPublic)!;
            var altDown = 0;
            var altUp = 0;
            KeyEventHandler onAltDown = (_, args) =>
            {
                if ((args.Key == Key.System ? args.SystemKey : args.Key) == Key.LeftAlt) altDown++;
            };
            KeyEventHandler onAltUp = (_, args) =>
            {
                if ((args.Key == Key.System ? args.SystemKey : args.Key) == Key.LeftAlt) altUp++;
            };
            input.AddHandler(Keyboard.PreviewKeyDownEvent, onAltDown, true);
            input.AddHandler(Keyboard.PreviewKeyUpEvent, onAltUp, true);
            try
            {
                Require((bool)unlock.Invoke(null, null)!, "Foreground recovery input could not be sent.");
                await WaitUntilAsync(() => altDown == 1 && altUp == 1, ct);
                Native.keybd_event(0x11, 0x1D, 0, UIntPtr.Zero);
                try
                {
                    await WaitUntilAsync(() => Keyboard.Modifiers.HasFlag(ModifierKeys.Control), ct);
                    Require(!(bool)unlock.Invoke(null, null)!, "Foreground recovery injected Alt while Control was pressed.");
                    Require(altDown == 1 && altUp == 1, "Foreground recovery sent repeated modifier input.");
                }
                finally
                {
                    Native.keybd_event(0x11, 0x1D, 2, UIntPtr.Zero);
                }
                await WaitUntilAsync(() => Keyboard.Modifiers == ModifierKeys.None, ct);
                Console.WriteLine("PASS: one native left-Alt pair, released modifiers, and held-Control guard");
            }
            finally
            {
                input.RemoveHandler(Keyboard.PreviewKeyDownEvent, onAltDown);
                input.RemoveHandler(Keyboard.PreviewKeyUpEvent, onAltUp);
            }

            input.Focusable = false;
            Native.SetFocus(IntPtr.Zero);
            var pending = (Task)focusMethod.Invoke(overlay, [(long)gestureField.GetValue(overlay)!, ct])!;
            Require(!pending.IsCompleted, "The cancellation check did not start a retry.");
            overlay.HideOverlay();
            Native.SetForegroundWindow(hostHandle);
            await pending;
            Require(Native.GetForegroundWindow() == hostHandle
                && typeof(OverlayWindow).GetField("_quickTapFocusCts", PrivateInstance)!.GetValue(overlay) is null,
                "Closing the popup left a foreground retry active.");
            Require(ai.CallCount == 0 && history.Items.Count == 0, "The focus probe triggered an AI lookup.");
        }
        finally
        {
            if (hostHandle != IntPtr.Zero) Native.PostMessage(hostHandle, Program.UnlockForegroundMessage, IntPtr.Zero, IntPtr.Zero);
            release.Set();
            overlay?.CloseForShutdown();
            if (!host.HasExited)
            {
                host.Kill();
                await host.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    private async Task RunTestsAsync(CancellationToken ct)
    {
        var eventName = "Local\\VerbaCore.PopupTests." + Guid.NewGuid().ToString("N");
        using var blocked = new EventWaitHandle(false, EventResetMode.ManualReset, eventName + ".blocked");
        using var release = new EventWaitHandle(false, EventResetMode.ManualReset, eventName + ".release");
        using var host = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
        {
            Arguments = "--host " + eventName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        })!;
        OverlayWindow? overlay = null;
        try
        {
            var handleText = await host.StandardOutput.ReadLineAsync(ct);
            var hostHandle = new IntPtr(long.Parse(handleText!, CultureInfo.InvariantCulture));
            Native.SetForegroundWindow(hostHandle);
            if (!_offscreen)
                await WaitUntilAsync(() => Native.GetForegroundWindow() == hostHandle, ct);

            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/VerbaCore;component/Resources/Strings.ko.xaml", UriKind.Relative)
            });
            var settings = new SettingsService();
            var history = new HistoryService();
            using var capsLock = new CapsLockService();
            var cursorText = new CursorTextService();
            var ai = new NoNetworkService();
            overlay = new OverlayWindow(ai, settings, history, capsLock,
                cursorText, new LookupCacheService(settings));
            typeof(CapsLockService).GetProperty(nameof(CapsLockService.ForegroundWindowAtPress))!
                .SetValue(capsLock, hostHandle);

            overlay.PreWarm();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, ct);
            if (!_offscreen)
                Require(Native.GetForegroundWindow() == hostHandle, "Pre-warm stole foreground focus.");

            await MeasurePopupAsync(overlay, capsLock, "first-after-prewarm", ct);
            var holdInput = (TextBox)overlay.FindName("InputTextBox");
            Require(holdInput.Visibility == Visibility.Visible && InputMethod.GetIsInputMethodEnabled(holdInput),
                "Hold mode did not expose the IME-enabled TextBox.");
            holdInput.Text = "\uD55C\uAE00";
            Require(capsLock.Buffer == holdInput.Text, "Hold-mode composed text was not synchronized.");
            Raise(capsLock, nameof(CapsLockService.QuickTapReleased));
            var input = (TextBox)overlay.FindName("InputTextBox");
            if (!_offscreen)
            {
                await WaitUntilAsync(() => input.IsKeyboardFocused, ct);
                Require(Native.GetForegroundWindow() == new WindowInteropHelper(overlay).Handle,
                    "Popup did not become the foreground window.");
                Require(InputMethod.GetIsInputMethodEnabled(input), "IME support was disabled.");
                TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, input, "\uD55C\uAE00"));
                Require(input.Text == "\uD55C\uAE00", "Korean text input was lost.");
                await WaitUntilAsync(() => overlay.Opacity >= 1, ct);
            }
            else
            {
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, ct);
                input.Text = "\uD55C\uAE00";
                overlay.UpdateLayout();
            }
            SaveSnapshot(overlay);

            var warmup = typeof(OverlayWindow).GetMethod("WarmUpInputSurface", PrivateInstance)!;
            var visibleText = input.Text;
            warmup.Invoke(overlay, null);
            Require(input.Text == visibleText && input.Visibility == Visibility.Visible,
                "Idle preparation changed an active input session.");

            await HideAsync(overlay, ct);
            if (_offscreen)
            {
                overlay.BeginAnimation(UIElement.OpacityProperty, null);
                overlay.Opacity = 0;
                overlay.Left = -9999;
                overlay.Top = -9999;
            }
            var foregroundBeforeWarmup = Native.GetForegroundWindow();
            var positionBeforeWarmup = new Point(overlay.Left, overlay.Top);
            var warmupStarted = Stopwatch.GetTimestamp();
            warmup.Invoke(overlay, null);
            Console.WriteLine($"idle-layout: {Stopwatch.GetElapsedTime(warmupStarted).TotalMilliseconds:F1} ms");
            Require(Native.GetForegroundWindow() == foregroundBeforeWarmup
                && new Point(overlay.Left, overlay.Top) == positionBeforeWarmup,
                "Idle preparation moved or activated the popup.");

            var hiddenCount = 0;
            overlay.IsVisibleChanged += (_, change) =>
            {
                if (!(bool)change.NewValue) hiddenCount++;
            };
            for (var iteration = 0; iteration < 5; iteration++)
            {
                await HideAsync(overlay, ct);
                Native.SetForegroundWindow(hostHandle);
                if (!_offscreen)
                    await WaitUntilAsync(() => Native.GetForegroundWindow() == hostHandle, ct);
                Require(Native.EmptyWorkingSet(Process.GetCurrentProcess().Handle),
                    "Could not simulate working-set eviction.");
                await MeasurePopupAsync(overlay, capsLock, $"trimmed-{iteration + 1}", ct);
            }
            Require(hiddenCount == 0, "Ordinary popup reuse hid the native window.");

            if (!_offscreen)
            {
                await HideAsync(overlay, ct);
                Native.SetForegroundWindow(hostHandle);
                await WaitUntilAsync(() => Native.GetForegroundWindow() == hostHandle, ct);
                Require(Native.PostMessage(hostHandle, Program.BlockMessage, IntPtr.Zero, IntPtr.Zero),
                    "Could not block the foreground fixture.");
                Require(blocked.WaitOne(TimeSpan.FromSeconds(2)), "Foreground fixture did not block.");
                try
                {
                    await MeasurePopupAsync(overlay, capsLock, "unresponsive-foreground", ct);
                }
                finally
                {
                    release.Set();
                }
            }

            overlay.HideOverlay();
            await MeasurePopupAsync(overlay, capsLock, "reopen-during-fade", ct);
            if (!_offscreen)
                await WaitUntilAsync(() => overlay.Opacity >= 1, ct);
            Require(overlay.IsVisible && overlay.Left > -1000, "An old hide completion hid the reopened popup.");
            settings.Current.ApiKey = "popup-test-placeholder";
            input.Text = "previous hold";
            Raise(capsLock, nameof(CapsLockService.LongPressReleased));
            Raise(capsLock, nameof(CapsLockService.CapsLockPressed));
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, ct);
            Require(ai.LastInput is null && input.Visibility == Visibility.Visible,
                "A queued release consumed a newer CapsLock gesture.");

            const string holdSelection = "\uD55C\uAE00 first paragraph.\n\nSecond paragraph.\n\nThird paragraph.";
            input.Text = holdSelection;
            Raise(capsLock, nameof(CapsLockService.LongPressReleased));
            await WaitUntilAsync(() => ai.LastInput == holdSelection, ct);
            Require(capsLock.TextInputWindow == IntPtr.Zero, "Hold release retained native input routing.");

            await HideAsync(overlay, ct);
            await MeasurePopupAsync(overlay, capsLock, "cancel-hold", ct);
            var previousCallCount = ai.CallCount;
            typeof(OverlayWindow).GetField("_grabbedSelectedText", PrivateInstance)!.SetValue(overlay, "selected text");
            Raise(capsLock, nameof(CapsLockService.HoldCancelled));
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, ct);
            Require(!(bool)typeof(OverlayWindow).GetField("_isShown", PrivateInstance)!.GetValue(overlay)!
                && ai.CallCount == previousCallCount, "Cancelling a hold triggered a lookup or left the popup open.");

            await MeasurePopupAsync(overlay, capsLock, "outside-click", ct);
            var mousePressed = (EventHandler<(int X, int Y)>)typeof(CapsLockService)
                .GetField(nameof(CapsLockService.MousePressed), PrivateInstance)!.GetValue(capsLock)!;
            var center = overlay.PointToScreen(new Point(overlay.Width / 2, overlay.Height / 2));
            mousePressed(capsLock, ((int)center.X, (int)center.Y));
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, ct);
            Require((bool)typeof(OverlayWindow).GetField("_isShown", PrivateInstance)!.GetValue(overlay)!,
                "A click inside the popup dismissed it.");
            mousePressed(capsLock, (-32000, -32000));
            Raise(capsLock, nameof(CapsLockService.CapsLockPressed));
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, ct);
            Require((bool)typeof(OverlayWindow).GetField("_isShown", PrivateInstance)!.GetValue(overlay)!,
                "A queued outside click dismissed a newer gesture.");
            mousePressed(capsLock, (-32000, -32000));
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, ct);
            Require(!(bool)typeof(OverlayWindow).GetField("_isShown", PrivateInstance)!.GetValue(overlay)!,
                "An outside click did not dismiss the popup.");
            Console.WriteLine("PASS: hold text submission, cancellation, stale gesture isolation, and outside-click dismissal");
            Require(history.Items.Count == 0, "Popup tests unexpectedly wrote history.");
            if (!_offscreen)
                Require(_latencies.All(latency => latency < 250), "A first-render latency exceeded 250 ms.");
            overlay.CloseForShutdown();
            var idleTimer = (DispatcherTimer)typeof(OverlayWindow).GetField("_idleWarmupTimer", PrivateInstance)!.GetValue(overlay)!;
            Require(!idleTimer.IsEnabled, "Idle preparation timer was not stopped at shutdown.");
            overlay = null;
        }
        finally
        {
            release.Set();
            overlay?.CloseForShutdown();
            if (!host.HasExited)
            {
                host.Kill();
                await host.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    private async Task MeasurePopupAsync(OverlayWindow overlay, CapsLockService capsLock,
        string scenario, CancellationToken ct)
    {
        var rendered = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = Stopwatch.GetTimestamp();
        void OnRendering(object? sender, EventArgs args)
        {
            if (overlay.IsVisible && overlay.Left > -1000 && overlay.Opacity > 0)
                rendered.TrySetResult(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        CompositionTarget.Rendering += OnRendering;
        try
        {
            await Task.Run(() => Raise(capsLock, nameof(CapsLockService.CapsLockPressed)), ct);
            double elapsed;
            if (_offscreen)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    Console.WriteLine($"{scenario}: UI-ready={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms");
                    overlay.UpdateLayout();
                    var target = new RenderTargetBitmap((int)overlay.ActualWidth, (int)overlay.ActualHeight,
                        96, 96, PixelFormats.Pbgra32);
                    target.Render((Visual)overlay.Content);
                }, DispatcherPriority.ApplicationIdle, ct);
                elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            }
            else
            {
                elapsed = await rendered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            }
            _latencies.Add(elapsed);
            Console.WriteLine($"{scenario}: {(_offscreen ? "offscreen-render" : "first-render")}={elapsed:F1} ms");
        }
        finally
        {
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private async Task HideAsync(OverlayWindow overlay, CancellationToken ct)
    {
        overlay.HideOverlay();
        if (!_offscreen)
            await WaitUntilAsync(() => overlay.Opacity == 0 && overlay.Left < -1000, ct);
        else
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, ct);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct, TimeSpan? timeout = null)
    {
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            if (condition()) complete.TrySetResult();
        };
        timer.Start();
        try
        {
            await complete.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(5), ct);
        }
        finally
        {
            timer.Stop();
        }
    }

    private static void Raise(CapsLockService capsLock, string eventName)
    {
        var handler = (EventHandler?)typeof(CapsLockService).GetField(eventName, PrivateInstance)!.GetValue(capsLock);
        handler?.Invoke(capsLock, EventArgs.Empty);
    }

    private static void SaveSnapshot(OverlayWindow overlay)
    {
        var scale = (ScaleTransform)overlay.FindName("ContentScale");
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        scale.ScaleX = scale.ScaleY = 1;
        var translate = (TranslateTransform)overlay.FindName("ContentTranslate");
        translate.BeginAnimation(TranslateTransform.YProperty, null);
        translate.Y = 0;
        var bitmap = new RenderTargetBitmap((int)overlay.ActualWidth, (int)overlay.ActualHeight,
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render((Visual)overlay.Content);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        Require(pixels.Count(channel => channel != 0) > 10000, "Popup rendered a blank surface.");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(Path.GetTempPath(), "VerbaCore-popup-test.png");
        using var output = File.Create(path);
        encoder.Save(output);
        Console.WriteLine("Snapshot: " + path);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal sealed class NoNetworkService : IOpenAiService
{
    public string? LastInput { get; private set; }
    public int CallCount { get; private set; }

    public Task<string> GetCompletionAsync(string input, LookupMode mode, string nativeLanguage,
        string foreignLanguage, CancellationToken ct = default)
    {
        LastInput = input;
        CallCount++;
        throw new InvalidOperationException("Network calls are forbidden in popup tests.");
    }

    public IAsyncEnumerable<string> StreamCompletionAsync(string input, LookupMode mode, string nativeLanguage,
        string foreignLanguage, CancellationToken ct = default)
    {
        LastInput = input;
        CallCount++;
        throw new InvalidOperationException("Network calls are forbidden in popup tests.");
    }
}

internal sealed class MouseInputProbe : IDisposable
{
    private const uint Marker = 0x56434254;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly Native.HookProc _callback;
    private Dispatcher? _dispatcher;
    private IntPtr _hook;
    private int _received;

    internal MouseInputProbe()
    {
        _callback = ObserveMovement;
        _thread = new Thread(() =>
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _hook = Native.SetWindowsHookEx(14, _callback, Native.GetModuleHandle(null), 0);
            _ready.Set();
            try
            {
                Dispatcher.Run();
            }
            finally
            {
                if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook);
            }
        }) { IsBackground = true, Name = "VerbaCore.TestMouseProbe" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(2)) || _hook == IntPtr.Zero)
            throw new InvalidOperationException("Could not install the independent mouse probe.");
    }

    private IntPtr ObserveMovement(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && (int)message == 0x0200
            && unchecked((uint)Marshal.ReadIntPtr(data, IntPtr.Size == 8 ? 24 : 20).ToInt64()) == Marker)
            Interlocked.Increment(ref _received);
        return Native.CallNextHookEx(_hook, code, message, data);
    }

    internal void CheckMovement()
    {
        var started = Stopwatch.GetTimestamp();
        var expected = Volatile.Read(ref _received) + 32;
        for (var index = 0; index < 32; index++)
            Native.mouse_event(0x2001, index % 2 == 0 ? 1u : unchecked((uint)-1), 0, 0, new UIntPtr(Marker));
        if (!SpinWait.SpinUntil(() => Volatile.Read(ref _received) == expected, 2000))
            throw new InvalidOperationException($"Mouse events did not reach the independent probe: {_received}/{expected}; foreground={Native.GetForegroundWindow()}.");
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (elapsed >= 500)
            throw new InvalidOperationException($"Mouse input stalled for {elapsed:F1} ms.");
        Console.WriteLine($"PASS: 32 paired mouse moves crossed the hook chain in {elapsed:F1} ms without a UI message pump");
    }

    public void Dispose()
    {
        _dispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }
}

internal static class Native
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LockSetForegroundWindow(uint lockCode);

    [DllImport("user32.dll")]
    internal static extern IntPtr SetFocus(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    internal static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    internal delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int hook, HookProc callback, IntPtr module, uint thread);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr GetModuleHandle(string? module);

    [DllImport("user32.dll")]
    internal static extern void mouse_event(uint flags, uint deltaX, uint deltaY, uint data, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EmptyWorkingSet(IntPtr process);
}