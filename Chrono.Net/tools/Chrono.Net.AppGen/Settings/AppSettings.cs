using System.Configuration;
using System.Globalization;
using Chrono.Net.AppGen.Ai;

namespace Chrono.Net.AppGen.Settings;

/// <summary>LLM connection settings for one provider (persisted as <c>Provider.{Kind}.*</c> app settings).</summary>
public sealed class ProviderSettings
{
    public required ProviderKind Kind { get; init; }
    public List<string> Models { get; set; } = [];
    public string ApiKey { get; set; } = "";
    public string Endpoint { get; set; } = "";
    /// <summary>Anthropic only: workspace for keys that are not scoped to one.</summary>
    public string WorkspaceId { get; set; } = "";
    /// <summary>Null = let the provider decide (reasoning models reject custom values).</summary>
    public double? Temperature { get; set; }
    public int MaxTokens { get; set; } = 8192;
    public string SystemPrompt { get; set; } = "";

    public bool IsConfigured => Kind == ProviderKind.Ollama || !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// Typed view over the app's <c>app.config</c> (<c>ChronoAppGen.dll.config</c> next to the executable).
/// Every value the UI can change lives here and is written back with <see cref="Save"/>.
/// </summary>
public sealed class AppSettings
{
    private Configuration _config;
    private KeyValueConfigurationCollection Values => _config.AppSettings.Settings;

    private AppSettings(Configuration config)
    {
        _config = config;
        foreach (var kind in Enum.GetValues<ProviderKind>()) Providers[kind] = LoadProvider(kind);
    }

    /// <summary>Path of the config file being edited.</summary>
    public string FilePath => _config.FilePath;

    public static AppSettings Load(string? path = null) =>
        new(Open(path ?? Path.Combine(AppContext.BaseDirectory, "ChronoAppGen.dll.config")));

    private static Configuration Open(string path) =>
        ConfigurationManager.OpenMappedExeConfiguration(new ExeConfigurationFileMap { ExeConfigFilename = path }, ConfigurationUserLevel.None);

    /// <summary>Discards unsaved edits by re-reading the file into this same instance.</summary>
    public void Reload()
    {
        _config = Open(FilePath);
        foreach (var kind in Enum.GetValues<ProviderKind>()) Providers[kind] = LoadProvider(kind);
    }

    public Dictionary<ProviderKind, ProviderSettings> Providers { get; } = [];

    public ProviderKind ActiveProvider
    {
        get => Enum.TryParse<ProviderKind>(Get("Ai.ActiveProvider"), out var k) ? k : ProviderKind.DeepSeek;
        set => Set("Ai.ActiveProvider", value.ToString());
    }
    public string ActiveModel { get => Get("Ai.ActiveModel"); set => Set("Ai.ActiveModel", value); }
    public string SystemPrompt { get => Get("Ai.SystemPrompt"); set => Set("Ai.SystemPrompt", value); }
    public int MaxToolRounds { get => GetInt("Ai.MaxToolRounds", 40); set => Set("Ai.MaxToolRounds", value); }
    public string TavilyApiKey { get => Get("Tavily.ApiKey"); set => Set("Tavily.ApiKey", value); }

    public double EditorFontSize { get => GetDouble("Editor.FontSize", 14); set => Set("Editor.FontSize", value); }
    public string EditorFontFamily { get => Get("Editor.FontFamily", "JetBrains Mono"); set => Set("Editor.FontFamily", value); }
    public bool ShowLineNumbers { get => GetBool("Editor.ShowLineNumbers", true); set => Set("Editor.ShowLineNumbers", value); }
    public bool WordWrap { get => GetBool("Editor.WordWrap", false); set => Set("Editor.WordWrap", value); }
    public int TabSize { get => GetInt("Editor.TabSize", 4); set => Set("Editor.TabSize", value); }

    public bool ExplorerVisible { get => GetBool("Layout.ExplorerVisible", true); set => Set("Layout.ExplorerVisible", value); }
    public double ExplorerWidth { get => GetDouble("Layout.ExplorerWidth", 260); set => Set("Layout.ExplorerWidth", value); }
    public bool ChatVisible { get => GetBool("Layout.ChatVisible", true); set => Set("Layout.ChatVisible", value); }
    public double ChatWidth { get => GetDouble("Layout.ChatWidth", 420); set => Set("Layout.ChatWidth", value); }
    public bool LogsVisible { get => GetBool("Layout.LogsVisible", true); set => Set("Layout.LogsVisible", value); }
    public double LogsHeight { get => GetDouble("Layout.LogsHeight", 190); set => Set("Layout.LogsHeight", value); }

    /// <summary>Folder where new projects are created (default: Documents\ChronoAppGen).</summary>
    public string ProjectsRoot
    {
        get
        {
            string v = Get("Projects.Root");
            return string.IsNullOrWhiteSpace(v)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ChronoAppGen")
                : v;
        }
        set => Set("Projects.Root", value);
    }
    public List<string> RecentProjects
    {
        get => Get("Projects.Recent").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        set => Set("Projects.Recent", string.Join('|', value.Distinct(StringComparer.OrdinalIgnoreCase).Take(10)));
    }
    public string ChronoNetVersion { get => Get("Projects.ChronoNetVersion", "0.1.0"); set => Set("Projects.ChronoNetVersion", value); }
    public string AvaloniaVersion { get => Get("Projects.AvaloniaVersion", "12.1.4"); set => Set("Projects.AvaloniaVersion", value); }
    /// <summary>Folder with Chrono.Net .nupkg files; blank = auto-detect a repo build, else nuget.org.</summary>
    public string LocalPackageFeed { get => Get("Projects.LocalPackageFeed"); set => Set("Projects.LocalPackageFeed", value); }

    public string BuildConfiguration { get => Get("Build.Configuration", "Debug"); set => Set("Build.Configuration", value); }
    public string DeployRuntime { get => Get("Deploy.Runtime", "win-x64"); set => Set("Deploy.Runtime", value); }
    public bool DeploySelfContained { get => GetBool("Deploy.SelfContained", false); set => Set("Deploy.SelfContained", value); }
    public bool DeploySingleFile { get => GetBool("Deploy.SingleFile", false); set => Set("Deploy.SingleFile", value); }

    /// <summary>Writes every setting (including provider settings) back to the config file.</summary>
    public void Save()
    {
        foreach (var p in Providers.Values) SaveProvider(p);
        _config.Save(ConfigurationSaveMode.Modified);
        ConfigurationManager.RefreshSection("appSettings");
    }

    private ProviderSettings LoadProvider(ProviderKind kind)
    {
        string k = $"Provider.{kind}.";
        var d = ProviderCatalog.Defaults(kind);
        string models = Get(k + "Models");
        return new ProviderSettings
        {
            Kind = kind,
            Models = (string.IsNullOrWhiteSpace(models) ? d.Models : models)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            ApiKey = Get(k + "ApiKey"),
            Endpoint = Get(k + "Endpoint", d.Endpoint),
            WorkspaceId = Get(k + "WorkspaceId"),
            Temperature = double.TryParse(Get(k + "Temperature"), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : null,
            MaxTokens = GetInt(k + "MaxTokens", 8192),
            SystemPrompt = Get(k + "SystemPrompt"),
        };
    }

    private void SaveProvider(ProviderSettings p)
    {
        string k = $"Provider.{p.Kind}.";
        Set(k + "Models", string.Join(',', p.Models));
        Set(k + "ApiKey", p.ApiKey);
        Set(k + "Endpoint", p.Endpoint);
        if (p.Kind == ProviderKind.Claude) Set(k + "WorkspaceId", p.WorkspaceId);
        Set(k + "Temperature", p.Temperature?.ToString(CultureInfo.InvariantCulture) ?? "");
        Set(k + "MaxTokens", p.MaxTokens);
        Set(k + "SystemPrompt", p.SystemPrompt);
    }

    // ------------------------------------------------------------------ raw access

    public string Get(string key, string fallback = "")
    {
        string? v = Values[key]?.Value;
        return string.IsNullOrEmpty(v) ? fallback : v;
    }

    public void Set(string key, string value)
    {
        if (Values[key] is { } e) e.Value = value;
        else Values.Add(key, value);
    }

    private void Set(string key, bool value) => Set(key, value ? "true" : "false");
    private void Set(string key, int value) => Set(key, value.ToString(CultureInfo.InvariantCulture));
    private void Set(string key, double value) => Set(key, value.ToString("0.##", CultureInfo.InvariantCulture));
    private bool GetBool(string key, bool fallback) => bool.TryParse(Get(key), out var b) ? b : fallback;
    private int GetInt(string key, int fallback) => int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : fallback;
    private double GetDouble(string key, double fallback) =>
        double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback;
}
