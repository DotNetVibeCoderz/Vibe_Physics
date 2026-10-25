using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Chrono.Net.Avalonia;
using Chrono.Net.Rendering;
using Chrono.Net.Samples;

namespace Chrono.Net.Gallery;

using Color = global::Avalonia.Media.Color;

public partial class MainWindow : Window
{
    private readonly bool _engine = ChronoEngine.IsAvailable;

    /// <summary>Start the first sample automatically (disabled by the headless screenshot tool).</summary>
    public static bool AutoRunOnStartup { get; set; } = true;
    private Sample? _sample;
    private SampleRun? _run;
    private RenderWorld? _world;
    private SimulationHost? _host;
    private readonly List<PlotSeries> _series = [];
    private bool _indonesian;
    private int _frame;

    private Strings S => _indonesian ? Strings.Id : Strings.En;

    public MainWindow()
    {
        InitializeComponent();
        try
        {
            using var s = AssetLoader.Open(new Uri("avares://Chrono.Net.Gallery.Avalonia/Assets/icon.png"));
            Logo.Source = new Bitmap(s);
            Icon = new WindowIcon(new Bitmap(AssetLoader.Open(new Uri("avares://Chrono.Net.Gallery.Avalonia/Assets/icon.png"))));
        }
        catch (Exception) { Logo.IsVisible = false; }

        EngineInfo.Text = _engine
            ? $"Chrono {ChronoEngine.ChronoVersion} · Chrono.Net {ChronoEngine.Version}"
            : $"Chrono.Net {ChronoEngine.Version} · native engine missing";

        SearchBox.TextChanged += (_, _) => BuildSampleList();
        LangToggle.IsCheckedChanged += (_, _) => { _indonesian = LangToggle.IsChecked == true; ApplyLanguage(); };
        RunButton.Click += (_, _) => ToggleRun();
        StepButton.Click += (_, _) => { if (_host is { IsRunning: false }) _host.StepOnce(); };
        ResetButton.Click += (_, _) => { if (_sample is not null) Select(_sample, autoRun: _host?.IsRunning ?? false); };
        SpeedSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            SpeedValue.Text = $"{SpeedSlider.Value:0.0}×";
            if (_host is not null) _host.Speed = SpeedSlider.Value;
        };
        CopyButton.Click += async (_, _) =>
        {
            if (_sample is null || Clipboard is null) return;
            await Clipboard.SetTextAsync(_sample.SourceCode);
            CopyButton.Content = S.Copied;
            await Task.Delay(1400);
            CopyButton.Content = S.CopyCode;
        };

        ApplyLanguage();
        var first = SampleCatalog.All.FirstOrDefault(s => s.Id == "mixer") ?? SampleCatalog.All[0];
        Select(first, autoRun: _engine && AutoRunOnStartup);
    }

    // ------------------------------------------------------------------ sample index

    private void BuildSampleList()
    {
        SampleList.Children.Clear();
        string q = SearchBox.Text?.Trim() ?? "";
        foreach (var group in SampleCatalog.All.GroupBy(s => s.Category))
        {
            var matches = group.Where(s => q.Length == 0
                || s.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || s.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
            if (matches.Count == 0) continue;

            SampleList.Children.Add(new TextBlock
            {
                Classes = { "eyebrow" },
                Text = Strings.Category(group.Key, _indonesian).ToUpperInvariant() + $"  ·  {matches.Count}",
                Margin = new Thickness(12, 16, 0, 6),
            });
            foreach (var s in matches)
            {
                var row = new StackPanel { Spacing = 2 };
                row.Children.Add(new TextBlock { Text = s.Title, FontSize = 14, FontWeight = FontWeight.Medium });
                row.Children.Add(new TextBlock
                {
                    Text = string.Join(" · ", s.Tags.Take(2)),
                    Classes = { "muted" },
                    FontSize = 11.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                var button = new Button { Content = row, Classes = { "sample" }, Tag = s };
                if (s == _sample) button.Classes.Add("active");
                button.Click += (_, _) => Select(s, autoRun: _engine);
                SampleList.Children.Add(button);
            }
        }
    }

    // ------------------------------------------------------------------ selection & simulation

    private void Select(Sample sample, bool autoRun)
    {
        StopSimulation();
        _sample = sample;
        BuildSampleList();
        UpdateDetails();

        bool vehicleMissing = sample.RequiresVehicle && _engine && !ChronoEngine.VehicleSupported;
        if (!_engine || vehicleMissing)
        {
            EmptyState.IsVisible = true;
            EmptyTitle.Text = _engine ? S.NoVehicle : S.NoEngineTitle;
            EmptyBody.Text = _engine ? "" : S.NoEngineBody;
            Viewport.World = null;
            Readout.Text = "—";
            SetTransportEnabled(false);
            return;
        }

        try
        {
            _run = sample.Create();
            _world = new RenderWorld(_run.System, sample.UpIsZ) { ShowContacts = _run.ShowContacts };
            _run.DecorateWorld?.Invoke(_world);
        }
        catch (ChronoException ex)
        {
            EmptyState.IsVisible = true;
            EmptyTitle.Text = sample.Title;
            EmptyBody.Text = ex.Message;
            SetTransportEnabled(false);
            return;
        }

        EmptyState.IsVisible = false;
        SetTransportEnabled(true);
        Viewport.World = _world;
        Viewport.FollowBody = _run.Follow;
        _run.Camera.Apply(Viewport.Camera);

        Plot.Clear();
        _series.Clear();
        foreach (var p in _run.Probes) _series.Add(Plot.AddSeries(p.Name, p.Unit));
        UpdateSignals();

        _host = new SimulationHost(_run.Step, sample.TimeStep) { Speed = SpeedSlider.Value };
        _host.Frame += OnFrame;
        OnFrame();
        if (autoRun) _host.Start();
        UpdateRunButton();
    }

    private void OnFrame()
    {
        if (_run is null || _world is null || _host is null) return;
        _run.UpdateWorld?.Invoke(_world);
        _world.Update();
        double t = _run.System.Time;
        for (int i = 0; i < _series.Count; i++) _series[i].Add(t, _run.Probes[i].Read());
        Viewport.Refresh();
        Plot.Refresh();
        if (_frame++ % 6 == 0)
        {
            Readout.Text = $"{S.Time} {t,7:0.00} s   dt {_sample!.TimeStep * 1000:0.0##} ms   " +
                           $"{_run.System.Bodies.Count} {S.Bodies}   {_run.System.Links.Count} {S.Links}   " +
                           $"{_run.System.ContactCount} {S.Contacts}   {(_host.IsRunning ? $"{_host.RealTimeFactor:0.00}×" : "–")} {S.RealTime}";
            if (_frame % 30 == 1) UpdateSignals();
        }
    }

    /// <summary>Selects a sample paused and advances it to <paramref name="seconds"/> (used for screenshots).</summary>
    public void ShowSample(string id, double seconds, bool indonesian = false)
    {
        if (indonesian != _indonesian) LangToggle.IsChecked = indonesian;
        var sample = SampleCatalog.Find(id) ?? throw new ArgumentException($"Unknown sample '{id}'.");
        Select(sample, autoRun: false);
        if (_run is null || _host is null) return;
        int steps = (int)(seconds / sample.TimeStep);
        double sampleEvery = Math.Max(1, steps / 400);
        for (int i = 0; i < steps; i++)
        {
            _run.Step(sample.TimeStep);
            if (i % sampleEvery == 0)
                for (int k = 0; k < _series.Count; k++) _series[k].Add(_run.System.Time, _run.Probes[k].Read());
        }
        _frame = 0;
        OnFrame();
    }

    private void ToggleRun()
    {
        if (_host is null) return;
        if (_host.IsRunning) _host.Pause();
        else _host.Start();
        UpdateRunButton();
    }

    private void StopSimulation()
    {
        _host?.Dispose();
        _host = null;
        Viewport.World = null;
        _run?.Dispose();
        _run = null;
        _world = null;
    }

    private void SetTransportEnabled(bool enabled) => RunButton.IsEnabled = StepButton.IsEnabled = ResetButton.IsEnabled = enabled;

    private void UpdateRunButton() => RunButton.Content = _host?.IsRunning == true ? S.Pause : S.Run;

    // ------------------------------------------------------------------ inspector

    private void UpdateDetails()
    {
        if (_sample is null) return;
        CategoryLabel.Text = Strings.Category(_sample.Category, _indonesian).ToUpperInvariant();
        TitleText.Text = _sample.Title;
        DescriptionText.Text = _indonesian ? _sample.DescriptionId : _sample.Description;
        TagPanel.Children.Clear();
        foreach (var tag in _sample.Tags)
            TagPanel.Children.Add(new Border { Classes = { "tag" }, Child = new TextBlock { Text = tag, FontSize = 12 } });
        CodeText.Text = _sample.SourceCode;
        UpdateSignals();
    }

    private void UpdateSignals()
    {
        SignalPanel.Children.Clear();
        if (_run is null) return;
        for (int i = 0; i < _run.Probes.Count; i++)
        {
            var p = _run.Probes[i];
            var color = i < _series.Count ? _series[i].Color : Color.Parse("#8391A8");
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("12,*,Auto") };
            row.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center });
            var name = new TextBlock { Text = p.Name, Margin = new Thickness(6, 0, 0, 0), FontSize = 13 };
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            var value = new TextBlock { Text = $"{p.Read():0.###} {p.Unit}", Classes = { "mono" } };
            Grid.SetColumn(value, 2);
            row.Children.Add(value);
            SignalPanel.Children.Add(row);
        }
    }

    private void ApplyLanguage()
    {
        var s = S;
        LangToggle.Content = _indonesian ? "ID" : "EN";
        SearchBox.PlaceholderText = s.Search;
        StepButton.Content = s.Step;
        ResetButton.Content = s.Reset;
        SpeedLabel.Text = s.Speed;
        AboutTab.Header = s.About;
        CodeTab.Header = s.Code;
        CopyButton.Content = s.CopyCode;
        TagsLabel.Text = s.Tags.ToUpperInvariant();
        SignalsLabel.Text = s.Signals.ToUpperInvariant();
        Credits.Text = s.Credits;
        HintText.Text = s.Hint;
        if (EmptyState.IsVisible && !_engine) { EmptyTitle.Text = s.NoEngineTitle; EmptyBody.Text = s.NoEngineBody; }
        UpdateRunButton();
        BuildSampleList();
        UpdateDetails();
    }

    protected override void OnClosed(EventArgs e)
    {
        StopSimulation();
        base.OnClosed(e);
    }
}
