using System.ClientModel;
using Anthropic;
using Chrono.Net.AppGen.Settings;
using Microsoft.Extensions.AI;

namespace Chrono.Net.AppGen.Ai;

public enum ProviderKind { OpenAI, AzureOpenAI, Claude, Gemini, Ollama, DeepSeek }

/// <summary>Display names, defaults and <see cref="IChatClient"/> construction for every supported LLM provider.</summary>
public static class ProviderCatalog
{
    public sealed record ProviderDefaults(string DisplayName, string Endpoint, string Models, string KeyHint);

    public static ProviderDefaults Defaults(ProviderKind kind) => kind switch
    {
        ProviderKind.OpenAI => new("OpenAI", "https://api.openai.com/v1", "gpt-5-mini,gpt-5", "sk-…"),
        ProviderKind.AzureOpenAI => new("Azure OpenAI", "", "gpt-5-mini", "Azure resource key"),
        ProviderKind.Claude => new("Claude", "https://api.anthropic.com", "claude-sonnet-5-5,claude-opus-5-5,claude-haiku-5-5", "sk-ant-…"),
        ProviderKind.Gemini => new("Gemini", "https://generativelanguage.googleapis.com/v1beta/openai/", "gemini-2.5-flash,gemini-2.5-pro", "AIza…"),
        ProviderKind.Ollama => new("Ollama", "http://localhost:11434/v1", "qwen3-coder,llama3.2", "not needed for a local server"),
        ProviderKind.DeepSeek => new("DeepSeek", "https://api.deepseek.com/v1", "deepseek-v4-flash,deepseek-v4-flash-vision-exp", "sk-…"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string DisplayName(ProviderKind kind) => Defaults(kind).DisplayName;

    /// <summary>
    /// Creates the raw chat client for a provider. Claude uses Anthropic's SDK; every other provider speaks the
    /// OpenAI chat-completions protocol (Azure through its <c>/openai/v1/</c> endpoint, Gemini and Ollama through
    /// their OpenAI-compatible endpoints).
    /// </summary>
    public static IChatClient CreateClient(ProviderSettings p, string model)
    {
        if (!p.IsConfigured) throw new InvalidOperationException($"{DisplayName(p.Kind)} has no API key. Add it in Settings → Models.");
        if (p.Kind == ProviderKind.Claude)
        {
            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            if (!string.IsNullOrWhiteSpace(p.WorkspaceId)) http.DefaultRequestHeaders.Add("anthropic-workspace-id", p.WorkspaceId.Trim());
            string baseUrl = string.IsNullOrWhiteSpace(p.Endpoint) ? Defaults(ProviderKind.Claude).Endpoint : p.Endpoint.Trim().TrimEnd('/');
            var client = new AnthropicClient { ApiKey = p.ApiKey.Trim(), HttpClient = http, BaseUrl = baseUrl };
            return client.AsIChatClient(model, p.MaxTokens);
        }

        string endpoint = p.Endpoint.Trim();
        if (p.Kind == ProviderKind.AzureOpenAI)
        {
            if (endpoint.Length == 0) throw new InvalidOperationException("Azure OpenAI needs the resource endpoint (https://<name>.openai.azure.com/).");
            if (!endpoint.Contains("/openai/", StringComparison.OrdinalIgnoreCase)) endpoint = endpoint.TrimEnd('/') + "/openai/v1/";
        }
        if (endpoint.Length == 0) endpoint = Defaults(p.Kind).Endpoint;
        var options = new OpenAI.OpenAIClientOptions { Endpoint = new Uri(endpoint), NetworkTimeout = TimeSpan.FromMinutes(10) };
        // Ollama ignores the key but the SDK requires a non-empty one.
        string key = string.IsNullOrWhiteSpace(p.ApiKey) ? "ollama" : p.ApiKey.Trim();
        return new OpenAI.Chat.ChatClient(model, new ApiKeyCredential(key), options).AsIChatClient();
    }

    /// <summary>Models that accept image input (best effort, by name).</summary>
    public static bool LikelySupportsImages(ProviderKind kind, string model) => kind switch
    {
        ProviderKind.DeepSeek => model.Contains("vision", StringComparison.OrdinalIgnoreCase),
        ProviderKind.Ollama => model.Contains("vl", StringComparison.OrdinalIgnoreCase) || model.Contains("llava", StringComparison.OrdinalIgnoreCase),
        _ => true,
    };
}
