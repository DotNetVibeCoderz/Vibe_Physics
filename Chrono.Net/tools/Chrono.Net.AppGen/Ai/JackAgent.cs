using System.Diagnostics;
using System.Runtime.CompilerServices;
using Chrono.Net.AppGen.Ai.Plugins;
using Chrono.Net.AppGen.Projects;
using Chrono.Net.AppGen.Settings;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;

namespace Chrono.Net.AppGen.Ai;

/// <summary>An image attached to a chat message.</summary>
public sealed record ChatAttachment(string FileName, byte[] Data, string MediaType);

/// <summary>One kernel function call made by Jack, reported to the UI as it starts and finishes.</summary>
public sealed class ToolCall
{
    public required string Plugin { get; init; }
    public required string Function { get; init; }
    public required string Arguments { get; init; }
    public string? Result { get; set; }
    public bool Failed { get; set; }
    public TimeSpan Elapsed { get; set; }
    public bool Done { get; set; }
}

/// <summary>
/// Jack — The Code Bender. Holds the chat thread and runs each turn: the provider's <see cref="IChatClient"/> wrapped
/// with Semantic Kernel's kernel-function invocation, so the model can call the plugins (workspace, build, Chrono
/// knowledge, web, math, time) as many times as it needs before answering.
/// </summary>
public sealed class JackAgent
{
    private readonly AppSettings _settings;
    private readonly Kernel _kernel;
    private readonly List<ChatMessage> _history = [];

    public JackAgent(AppSettings settings, IIdeHost host)
    {
        _settings = settings;
        var builder = Kernel.CreateBuilder();
        _kernel = builder.Build();
        _kernel.Plugins.AddFromObject(new WorkspacePlugin(host), "workspace");
        _kernel.Plugins.AddFromObject(new BuildPlugin(host, () => settings.BuildConfiguration), "build");
        _kernel.Plugins.AddFromObject(new ChronoPlugin(host, () => settings.ProjectsRoot,
            () => new GeneratorOptions(settings.ChronoNetVersion, settings.AvaloniaVersion, settings.LocalPackageFeed)), "chrono");
        _kernel.Plugins.AddFromObject(new WebPlugin(() => settings.TavilyApiKey), "web");
        _kernel.Plugins.AddFromObject(new MathPlugin(), "math");
        _kernel.Plugins.AddFromObject(new DateTimePlugin(), "time");
        _kernel.FunctionInvocationFilters.Add(new ToolReporter(this));
    }

    /// <summary>Raised (on the calling thread's context) when a tool call starts and again when it finishes.</summary>
    public event Action<ToolCall>? ToolCallStarted;
    public event Action<ToolCall>? ToolCallFinished;

    public int MessageCount => _history.Count;
    public IReadOnlyList<KernelFunction> Functions => _kernel.Plugins.SelectMany(p => p).ToList();

    public void Clear() => _history.Clear();

    /// <summary>Sends a user message and streams Jack's reply text. Tool calls are reported through the events.</summary>
    public async IAsyncEnumerable<string> SendAsync(string text, IReadOnlyList<ChatAttachment> attachments, ProviderKind provider, string model,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var p = _settings.Providers[provider];
        var userContent = new List<AIContent> { new Microsoft.Extensions.AI.TextContent(text) };
        userContent.AddRange(attachments.Select(a => new DataContent(a.Data, a.MediaType) { Name = a.FileName }));
        _history.Add(new ChatMessage(ChatRole.User, userContent));

        string system = FirstNonEmpty(p.SystemPrompt, _settings.SystemPrompt, JackPrompt.Default);
        // Bind each kernel function to this kernel so the invocation filter (tool reporting) and services apply.
        // AsAIFunction is marked obsolete in SK 1.81, but the suggested replacements do not carry the kernel.
#pragma warning disable CS0618
        var tools = _kernel.Plugins.SelectMany(pl => pl).Select(f => (AITool)f.AsAIFunction(_kernel)).ToList();
#pragma warning restore CS0618
        var options = new ChatOptions
        {
            Tools = tools,
            ToolMode = ChatToolMode.Auto,
            Temperature = p.Temperature is { } t ? (float)t : null,
            MaxOutputTokens = p.MaxTokens > 0 ? p.MaxTokens : null,
        };

        bool retriedWithoutTemperature = false;
        while (true)
        {
            using var client = BuildClient(p, model);
            var messages = new List<ChatMessage> { new(ChatRole.System, system) };
            messages.AddRange(_history);
            var updates = new List<ChatResponseUpdate>();
            bool streamedText = false;
            Exception? failure = null;

            var stream = client.GetStreamingResponseAsync(messages, options, ct).GetAsyncEnumerator(ct);
            try
            {
                while (true)
                {
                    ChatResponseUpdate update;
                    try
                    {
                        if (!await stream.MoveNextAsync()) break;
                        update = stream.Current;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failure = ex;
                        break;
                    }
                    updates.Add(update);
                    if (!string.IsNullOrEmpty(update.Text))
                    {
                        streamedText = true;
                        yield return update.Text;
                    }
                }
            }
            finally { await stream.DisposeAsync(); }

            if (failure is null)
            {
                // Keep the whole exchange (assistant text, tool calls and tool results) for the next turn.
                _history.AddMessages(updates);
                yield break;
            }

            // Reasoning models (gpt-5, o-series) reject a custom temperature: retry once without it.
            if (!retriedWithoutTemperature && !streamedText && options.Temperature is not null &&
                failure.Message.Contains("temperature", StringComparison.OrdinalIgnoreCase))
            {
                retriedWithoutTemperature = true;
                options.Temperature = null;
                continue;
            }
            if (updates.Count > 0) _history.AddMessages(updates);
            throw new JackException(Describe(failure, provider), failure);
        }
    }

    private IChatClient BuildClient(ProviderSettings p, string model)
    {
        var client = ProviderCatalog.CreateClient(p, model).AsBuilder().UseKernelFunctionInvocation().Build();
        if (client.GetService<FunctionInvokingChatClient>() is { } invoker)
        {
            invoker.MaximumIterationsPerRequest = Math.Max(4, _settings.MaxToolRounds);
            invoker.MaximumConsecutiveErrorsPerRequest = 4;
        }
        return client;
    }

    private static string Describe(Exception ex, ProviderKind provider)
    {
        string m = ex.Message;
        string name = ProviderCatalog.DisplayName(provider);
        if (m.Contains("401") || m.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) || m.Contains("invalid x-api-key", StringComparison.OrdinalIgnoreCase))
            return $"{name} rejected the API key. Check it in Settings → Models.";
        if (m.Contains("workspace", StringComparison.OrdinalIgnoreCase))
            return $"{name}: this key needs a workspace ID. Add it in Settings → Models. ({m})";
        if (m.Contains("404") || m.Contains("model", StringComparison.OrdinalIgnoreCase) && m.Contains("not", StringComparison.OrdinalIgnoreCase))
            return $"{name} could not use this model. Pick another model or check the model list in Settings. ({Trim(m)})";
        if (ex is HttpRequestException) return $"Could not reach {name}: {Trim(m)}. Check the endpoint and your connection.";
        return $"{name} returned an error: {Trim(m)}";
    }

    private static string Trim(string s) => s.Length > 400 ? s[..400] + "…" : s;

    private static string FirstNonEmpty(params string[] values) => values.First(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>Reports every kernel function invocation to the UI.</summary>
    private sealed class ToolReporter(JackAgent agent) : IFunctionInvocationFilter
    {
        public async Task OnFunctionInvocationAsync(Microsoft.SemanticKernel.FunctionInvocationContext context, Func<Microsoft.SemanticKernel.FunctionInvocationContext, Task> next)
        {
            var call = new ToolCall
            {
                Plugin = context.Function.PluginName ?? "",
                Function = context.Function.Name,
                Arguments = string.Join(", ", context.Arguments.Where(a => a.Value is not CancellationToken)
                    .Select(a => $"{a.Key}={Short(a.Value?.ToString())}")),
            };
            agent.ToolCallStarted?.Invoke(call);
            var sw = Stopwatch.StartNew();
            try
            {
                await next(context);
                call.Result = context.Result.GetValue<object>()?.ToString();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Hand the error to the model as the tool result so it can recover, instead of ending the turn.
                call.Failed = true;
                call.Result = ex.Message;
                context.Result = new FunctionResult(context.Function, $"Error: {ex.Message}");
            }
            finally
            {
                call.Elapsed = sw.Elapsed;
                call.Done = true;
                agent.ToolCallFinished?.Invoke(call);
            }
        }

        private static string Short(string? s) => s is null ? "null" : s.Length > 60 ? s[..60].ReplaceLineEndings(" ") + "…" : s.ReplaceLineEndings(" ");
    }
}

public sealed class JackException(string message, Exception inner) : Exception(message, inner);
