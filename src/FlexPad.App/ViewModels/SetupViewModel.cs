using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Media;
using FlexPad.App.Services;
using FlexPad.Core;

namespace FlexPad.App.ViewModels;

/// <summary>
/// Setup: the radio connection and grid, the FlexControl knob, and the in-app updater. Edits are
/// applied when the window closes (<see cref="ApplyTo"/>), except Discover and Reload, which act
/// at once. The updater half is the family's shared implementation.
/// </summary>
public sealed class SetupViewModel : ViewModelBase
{
    /// <summary>Number of tabs in <c>SetupWindow.axaml</c>; keep in step if one is added.</summary>
    public const int TabCount = 3;

    /// <summary>Index of the Updates tab, so the app can open Setup straight onto it.</summary>
    public const int UpdatesTab = 2;

    public SetupViewModel(AppConfig config, Func<IEnumerable<string>> buttonLabels)
    {
        LoadFrom(config, buttonLabels());
        DiscoverCommand = new RelayCommand(() => _ = DiscoverAsync(), () => !_discovering);
        RefreshKnobCommand = new RelayCommand(() => KnobDetected = KnobPort.Find() ?? "none");
        UpdateCommand = new RelayCommand(() => _ = UpdateButtonAsync(), () => !_updateBusy);
        OpenReleaseCommand = new RelayCommand(OpenRelease);
        ShowCrashLogCommand = new RelayCommand(ShowCrashLog);
        UninstallCommand = new RelayCommand(() => _ = UninstallAsync(), () => CanUninstall);
        UpdateStatus = $"You have {UpdateService.CurrentVersion}.";
    }

    /// <summary>Raised when the user asks to re-read config.json from disk; the App does it.</summary>
    public event Action? ReloadRequested;
    public RelayCommand ReloadCommand => new(() => ReloadRequested?.Invoke());

    private int _selectedTabIndex;
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, Math.Clamp(value, 0, TabCount - 1));
    }

    // --- Radio tab ---

    private string _host = "";
    public string Host { get => _host; set => SetProperty(ref _host, value); }

    private string _port = "4992";
    public string Port { get => _port; set => SetProperty(ref _port, value); }

    private string _columns = "4";
    public string Columns { get => _columns; set => SetProperty(ref _columns, value); }

    private bool _stopOnError = true;
    public bool StopOnError { get => _stopOnError; set => SetProperty(ref _stopOnError, value); }

    private string _discoverStatus = "";
    public string DiscoverStatus { get => _discoverStatus; private set => SetProperty(ref _discoverStatus, value); }

    public RelayCommand DiscoverCommand { get; }
    private bool _discovering;

    public string ConfigPath => ConfigStore.ConfigFilePath;

    private async Task DiscoverAsync()
    {
        _discovering = true; DiscoverCommand.RaiseCanExecuteChanged();
        DiscoverStatus = "Listening for radios…";
        try
        {
            var radios = await Task.Run(() => Discovery.Listen(TimeSpan.FromSeconds(4)));
            if (radios.Count == 0) DiscoverStatus = "No radio answered within 4 s.";
            else
            {
                Host = radios[0].Ip;
                DiscoverStatus = string.Join("   ", radios.Select(r => $"{r.Model} {r.Nickname} at {r.Ip}"));
            }
        }
        finally { _discovering = false; DiscoverCommand.RaiseCanExecuteChanged(); }
    }

    // --- Knob tab ---

    private bool _knobEnabled = true;
    public bool KnobEnabled { get => _knobEnabled; set => SetProperty(ref _knobEnabled, value); }

    private string _knobPort = "";
    public string KnobPortName { get => _knobPort; set => SetProperty(ref _knobPort, value); }

    private string _knobDetected = "";
    public string KnobDetected { get => _knobDetected; private set => SetProperty(ref _knobDetected, value); }

    private bool _knobInvert;
    public bool KnobInvert { get => _knobInvert; set => SetProperty(ref _knobInvert, value); }

    private string _knobSteps = "";
    public string KnobSteps { get => _knobSteps; set => SetProperty(ref _knobSteps, value); }

    public ObservableCollection<BindingRow> Bindings { get; } = new();
    public RelayCommand RefreshKnobCommand { get; }

    public string KnobHint =>
        "Each function acts on the active slice, or on the radio as a whole (TUNE, MOX, ATU, AMP). " +
        "AMP switches a Power Genius between operate and standby.";

    // --- config in/out ---

    public void LoadFrom(AppConfig cfg, IEnumerable<string> buttonLabels)
    {
        Host = cfg.FlexHost;
        Port = cfg.FlexPort.ToString();
        Columns = cfg.Columns.ToString();
        StopOnError = cfg.StopOnError;
        CheckUpdatesAtStartup = cfg.CheckUpdatesAtStartup;
        SelectedTabIndex = cfg.Window.SetupTab;

        KnobEnabled = cfg.Knob.Enabled;
        KnobPortName = cfg.Knob.Port;
        KnobInvert = cfg.Knob.Invert;
        KnobSteps = string.Join(", ", cfg.Knob.Steps);
        KnobDetected = KnobPort.Find() ?? "none";
        // Functions only: a knob button is for TUNE, the amp, the antenna, not for recalling a band.
        // A binding to a flexpad button from an older config still works and still shows.
        var choices = new List<KnobFunction> { new("", "(not bound)") };
        choices.AddRange(KnobActions.Catalog);
        Bindings.Clear();
        foreach (var code in KnobProtocol.Events)
        {
            var current = cfg.Knob.Bindings.GetValueOrDefault(code) ?? "";
            var list = choices.ToList();
            var selected = list.FirstOrDefault(f => f.Code == current);
            if (selected is null) { selected = new KnobFunction(current, "button: " + current); list.Add(selected); }
            Bindings.Add(new BindingRow(code, KnobProtocol.EventNames[code], list, selected));
        }
    }

    public void ApplyTo(AppConfig cfg)
    {
        cfg.FlexHost = Host.Trim();
        if (int.TryParse(Port, out var p) && p > 0) cfg.FlexPort = p;
        if (int.TryParse(Columns, out var c) && c > 0) cfg.Columns = c;
        cfg.StopOnError = StopOnError;
        cfg.CheckUpdatesAtStartup = CheckUpdatesAtStartup;
        cfg.Window.SetupTab = SelectedTabIndex;

        cfg.Knob.Enabled = KnobEnabled;
        cfg.Knob.Port = KnobPortName.Trim();
        cfg.Knob.Invert = KnobInvert;
        var steps = KnobSteps.Replace(",", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, out var v) ? v : -1).Where(v => v > 0).ToList();
        if (steps.Count > 0) cfg.Knob.Steps = steps;
        foreach (var row in Bindings) cfg.Knob.Bindings[row.Code] = row.Selected?.Code ?? "";
    }

    // --- Updates tab (shared with the other station apps) ---

    private UpdateInfo? _updateInfo;
    private string? _stagedExe;
    private bool _updateBusy;

    private bool _checkUpdatesAtStartup;
    public bool CheckUpdatesAtStartup { get => _checkUpdatesAtStartup; set => SetProperty(ref _checkUpdatesAtStartup, value); }

    private string _updateStatus = "";
    public string UpdateStatus { get => _updateStatus; private set => SetProperty(ref _updateStatus, value); }

    private IBrush _updateStatusBrush = Palette.DimBrush;
    public IBrush UpdateStatusBrush { get => _updateStatusBrush; private set => SetProperty(ref _updateStatusBrush, value); }

    public RelayCommand UpdateCommand { get; }
    public RelayCommand OpenReleaseCommand { get; }
    public RelayCommand ShowCrashLogCommand { get; }
    public RelayCommand UninstallCommand { get; }

    /// <summary>Only an installed copy has anything to remove; a loose or portable one just runs where it sits.</summary>
    public bool CanUninstall => InstallService.Mode == InstallMode.Installed;

    private readonly DateTime? _lastCrashUtc = CrashLog.LastCrashUtc();
    public bool ShowCrashNotice => _lastCrashUtc is not null;
    public string CrashNotice => _lastCrashUtc is { } when
        ? $"A crash was recorded on {when.ToLocalTime():d MMM} at {when.ToLocalTime():HH:mm}. Please attach crash.log to a bug report."
        : "";

    private bool _updateAvailable;
    public bool UpdateAvailable
    {
        get => _updateAvailable;
        private set { if (SetProperty(ref _updateAvailable, value)) OnPropertyChanged(nameof(UpdateButtonLabel)); }
    }

    public string UpdateButtonLabel => UpdateAvailable ? "Update now" : "Check for updates";
    public string? LatestTag => _updateInfo?.LatestTag;

    private Task UpdateButtonAsync() => UpdateAvailable ? UpdateNowAsync() : CheckUpdatesAsync();

    public void NoteUpdateFailed()
    {
        UpdateStatus = $"Last update didn't apply — still on {UpdateService.CurrentVersion}. Try again.";
        UpdateStatusBrush = Palette.AmberBrush;
    }

    public async Task CheckUpdatesAsync()
    {
        _updateBusy = true; UpdateCommand.RaiseCanExecuteChanged();
        UpdateStatus = "Checking for updates…"; UpdateStatusBrush = Palette.DimBrush;

        var info = await UpdateService.CheckAsync();
        _updateInfo = info;
        if (info.Error is not null)
        {
            UpdateStatus = $"Update check failed: {info.Error}"; UpdateStatusBrush = Palette.RedBrush; UpdateAvailable = false;
        }
        else if (info.UpdateAvailable && info.AssetUrl is not null)
        {
            UpdateStatus = $"Update available: {info.LatestTag} (you have {info.CurrentVersion})."; UpdateStatusBrush = Palette.GreenBrush; UpdateAvailable = true;
        }
        else if (info.UpdateAvailable)
        {
            UpdateStatus = $"{info.LatestTag} is available, but has no build for this platform."; UpdateStatusBrush = Palette.AmberBrush; UpdateAvailable = false;
        }
        else
        {
            UpdateStatus = $"Up to date ({info.CurrentVersion})."; UpdateStatusBrush = Palette.GreenBrush; UpdateAvailable = false;
        }

        _updateBusy = false; UpdateCommand.RaiseCanExecuteChanged();
    }

    private async Task UpdateNowAsync()
    {
        if (_updateInfo?.AssetUrl is not { } url) return;
        _updateBusy = true; UpdateCommand.RaiseCanExecuteChanged();
        try
        {
            UpdateStatus = "Downloading update…"; UpdateStatusBrush = Palette.DimBrush;
            _stagedExe = await UpdateService.DownloadAndStageAsync(url);
            UpdateStatus = "Update ready — restarting to apply…"; UpdateStatusBrush = Palette.GreenBrush;
            UpdateService.ApplyAndRestart(_stagedExe);
            (Application.Current as App)?.ExitForUpdate();
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Update failed: {ex.Message}"; UpdateStatusBrush = Palette.RedBrush;
            _updateBusy = false; UpdateCommand.RaiseCanExecuteChanged();
        }
    }

    private void OpenRelease()
    {
        var url = _updateInfo?.ReleaseUrl ?? $"https://github.com/{UpdateService.Repo}/releases/latest";
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private static async Task UninstallAsync()
    {
        if (Application.Current is App app) await app.RunUninstallAsync();
    }

    private void ShowCrashLog()
    {
        try
        {
            var path = CrashLog.FilePath;
            if (OperatingSystem.IsWindows() && File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\""));
            else
                Process.Start(new ProcessStartInfo(Path.GetDirectoryName(path)!) { UseShellExecute = true });
        }
        catch { }
    }
}

/// <summary>One knob event and what it does.</summary>
public sealed class BindingRow : ViewModelBase
{
    public BindingRow(string code, string name, List<KnobFunction> choices, KnobFunction selected)
    {
        Code = code;
        Name = name;
        Choices = choices;
        _selected = selected;
    }

    public string Code { get; }
    public string Name { get; }
    public List<KnobFunction> Choices { get; }

    private KnobFunction? _selected;
    public KnobFunction? Selected { get => _selected; set => SetProperty(ref _selected, value); }
}
