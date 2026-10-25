using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Chrono.Net.Avalonia;
using Chrono.Net.Rendering;
using Chrono.Net.Scenes;

namespace Chrono.Net.Simulator;

/// <summary>Scene authoring and playback window. Made by Gravicode Studios, led by Kang Fadhil.</summary>
public partial class SimulatorWindow : Window
{
    private SceneDefinition _definition;
    private LoadedScene? _scene;
    private RenderWorld? _world;
    private SimulationHost? _host;
    private ChBody? _selected;
    private PlotSeries? _heightSeries, _speedSeries, _energySeries;
    private readonly StringBuilder _csv = new();
    private string? _path;
    private int _frame;

    public SimulatorWindow() : this(null) { }

    public SimulatorWindow(string? path)
    {
        InitializeComponent();
        _definition = DefaultScene();

        NewButton.Click += (_, _) => { _path = null; Load(DefaultScene()); };
        OpenButton.Click += async (_, _) => await OpenAsync();
        SaveButton.Click += async (_, _) => await SaveAsync();
        CsvButton.Click += async (_, _) => await ExportCsvAsync();
        PngButton.Click += async (_, _) => await SnapshotAsync();
        RunButton.Click += (_, _) => ToggleRun();
        StepButton.Click += (_, _) => { if (_host is { IsRunning: false }) _host.StepOnce(); };
        ResetButton.Click += (_, _) => Load(_definition);
        ApplyButton.Click += (_, _) => ApplyJson();
        ContactsCheck.IsCheckedChanged += (_, _) => { if (_world is not null) _world.ShowContacts = ContactsCheck.IsChecked == true; Viewport.Refresh(); };
        ShadowsCheck.IsCheckedChanged += (_, _) => { Viewport.Options.DrawShadows = ShadowsCheck.IsChecked == true; Viewport.Refresh(); };
        BodyList.SelectionChanged += (_, _) => SelectBody(BodyList.SelectedItem is ListBoxItem { Tag: ChBody b } ? b : null);
        AddHandler(DragDrop.DropEvent, OnDrop);

        if (path is not null && File.Exists(path)) LoadFile(path);
        else Load(_definition);
    }

    // ------------------------------------------------------------------ scene lifecycle

    private void Load(SceneDefinition def)
    {
        Stop();
        _definition = def;
        JsonEditor.Text = def.ToJson();
        JsonError.Text = "";
        FileLabel.Text = _path is null ? $"{def.Name} (unsaved)" : Path.GetFileName(_path);

        if (!ChronoEngine.IsAvailable)
        {
            ShowError("Native engine not found", "Build chrono_native with ./build/build-native.ps1. You can still edit and save scene files.");
            return;
        }
        try
        {
            _scene = SceneLoader.Load(def);
        }
        catch (Exception ex) when (ex is ChronoException or InvalidDataException or ArgumentException)
        {
            ShowError("Scene could not be loaded", ex.Message);
            return;
        }
        ErrorPanel.IsVisible = false;

        bool upIsZ = string.Equals(def.Settings.UpAxis, "Z", StringComparison.OrdinalIgnoreCase);
        _world = new RenderWorld(_scene.System, upIsZ) { ShowContacts = ContactsCheck.IsChecked == true };
        Viewport.World = _world;
        Viewport.FitToScene();

        BodyList.Items.Clear();
        foreach (var b in _scene.System.Bodies)
        {
            if (b.Name.StartsWith("__")) continue;
            BodyList.Items.Add(new ListBoxItem { Content = $"{b.Name}{(b.IsFixed ? "  ·  fixed" : "")}", Tag = b });
        }
        var firstMoving = _scene.System.Bodies.FirstOrDefault(b => !b.IsFixed && !b.Name.StartsWith("__"));
        SelectBody(firstMoving);

        _csv.Clear();
        _csv.Append("time");
        foreach (var b in _scene.System.Bodies) _csv.Append($",{b.Name}.x,{b.Name}.y,{b.Name}.z");
        _csv.AppendLine();

        _host = new SimulationHost(_scene.System.Step, def.Settings.TimeStep);
        _host.Frame += OnFrame;
        OnFrame();
        UpdateRunButton();
    }

    private void LoadFile(string path)
    {
        try
        {
            _path = path;
            Load(SceneDefinition.FromFile(path));
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            ShowError("Could not open file", ex.Message);
        }
    }

    private void ApplyJson()
    {
        try
        {
            var def = SceneDefinition.FromJson(JsonEditor.Text ?? "");
            Load(def);
        }
        catch (System.Text.Json.JsonException ex)
        {
            JsonError.Text = ex.Message;
        }
    }

    private void Stop()
    {
        _host?.Dispose();
        _host = null;
        Viewport.World = null;
        _scene?.Dispose();
        _scene = null;
        _world = null;
        _selected = null;
    }

    private void ShowError(string title, string body)
    {
        ErrorPanel.IsVisible = true;
        ErrorTitle.Text = title;
        ErrorBody.Text = body;
        UpdateRunButton();
    }

    // ------------------------------------------------------------------ frame updates

    private void OnFrame()
    {
        if (_scene is null || _world is null || _host is null) return;
        _world.Update();
        double t = _scene.System.Time;
        if (_selected is not null)
        {
            var s = _selected.State;
            _heightSeries?.Add(t, _world.UpIsZ ? s.Position.Z : s.Position.Y);
            _speedSeries?.Add(t, s.LinearVelocity.Length);
        }
        _energySeries?.Add(t, _scene.System.KineticEnergy);

        if (_host.IsRunning && _frame % 2 == 0)
        {
            _csv.Append(t.ToString("0.#####", CultureInfo.InvariantCulture));
            foreach (ref readonly var s in _scene.System.GetBodyStates())
                _csv.Append(CultureInfo.InvariantCulture, $",{s.Position.X:0.######},{s.Position.Y:0.######},{s.Position.Z:0.######}");
            _csv.AppendLine();
        }

        Viewport.Refresh();
        Plot.Refresh();
        if (_frame++ % 6 == 0)
        {
            StatusText.Text = $"t {t:0.000} s   dt {_definition.Settings.TimeStep * 1000:0.0##} ms   {_scene.System.Bodies.Count} bodies   " +
                              $"{_scene.System.Links.Count} links   {_scene.System.ContactCount} contacts   {_host.RealTimeFactor:0.00}× real time";
            UpdateInspector();
        }
    }

    private void SelectBody(ChBody? body)
    {
        _selected = body;
        Plot.Clear();
        string name = body?.Name ?? "—";
        _heightSeries = body is null ? null : Plot.AddSeries($"{name} height", "m");
        _speedSeries = body is null ? null : Plot.AddSeries($"{name} speed", "m/s");
        _energySeries = Plot.AddSeries("Kinetic energy", "J");
        UpdateInspector();
    }

    private void UpdateInspector()
    {
        Inspector.Children.Clear();
        if (_selected is null)
        {
            Inspector.Children.Add(new TextBlock { Text = "Select a body to inspect it.", Classes = { "muted" } });
            return;
        }
        var b = _selected;
        var s = b.State;
        void Row(string label, string value)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*") };
            g.Children.Add(new TextBlock { Text = label, Classes = { "muted" }, FontSize = 12 });
            var v = new TextBlock { Text = value, Classes = { "mono" }, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            Inspector.Children.Add(g);
        }
        Inspector.Children.Add(new TextBlock { Text = b.Name, Classes = { "display" }, FontSize = 24 });
        Row("Fixed", b.IsFixed ? "yes" : "no");
        Row("Mass", $"{b.Mass:0.###} kg");
        Row("Inertia (xx,yy,zz)", F(b.Inertia));
        Row("Position", F(s.Position));
        var e = s.Rotation.ToEuler();
        Row("Rotation (deg)", F(new Vec3(e.X * 180 / Math.PI, e.Y * 180 / Math.PI, e.Z * 180 / Math.PI)));
        Row("Velocity", F(s.LinearVelocity));
        Row("Angular velocity", F(s.AngularVelocity));
        Row("Contact force", F(b.ContactForce));
        Row("Kinetic energy", $"{b.KineticEnergy:0.###} J");
    }

    private static string F(Vec3 v) => string.Create(CultureInfo.InvariantCulture, $"{v.X,8:0.000} {v.Y,8:0.000} {v.Z,8:0.000}");

    private void ToggleRun()
    {
        if (_host is null) return;
        if (_host.IsRunning) _host.Pause(); else _host.Start();
        UpdateRunButton();
    }

    private void UpdateRunButton()
    {
        RunButton.Content = _host?.IsRunning == true ? "Pause" : "Run";
        RunButton.IsEnabled = StepButton.IsEnabled = _host is not null;
    }

    // ------------------------------------------------------------------ files

    private static readonly FilePickerFileType SceneType = new("Chrono.Net scene") { Patterns = ["*.json"] };

    private async Task OpenAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open scene", FileTypeFilter = [SceneType] });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } p) LoadFile(p);
    }

    private async Task SaveAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save scene", SuggestedFileName = (_definition.Name.Replace(' ', '-')) + ".json", FileTypeChoices = [SceneType],
        });
        if (file?.TryGetLocalPath() is not { } p) return;
        File.WriteAllText(p, JsonEditor.Text ?? _definition.ToJson());
        _path = p;
        FileLabel.Text = Path.GetFileName(p);
    }

    private async Task ExportCsvAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export trajectories", SuggestedFileName = "trajectories.csv",
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
        });
        if (file?.TryGetLocalPath() is { } p) File.WriteAllText(p, _csv.ToString());
    }

    private async Task SnapshotAsync()
    {
        if (_world is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save snapshot", SuggestedFileName = "snapshot.png",
            FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }],
        });
        if (file?.TryGetLocalPath() is { } p) Snapshot.SavePng(_world, Viewport.Camera, p, 1920, 1080, Viewport.Options);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files?.FirstOrDefault()?.TryGetLocalPath() is { } p && p.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) LoadFile(p);
    }

    protected override void OnClosed(EventArgs e)
    {
        Stop();
        base.OnClosed(e);
    }

    private static SceneDefinition DefaultScene()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Scenes", "pendulum-wave.json");
        return File.Exists(path) ? SceneDefinition.FromFile(path) : new SceneDefinition { Name = "Empty scene" };
    }
}
