using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FlexPad.App.Services;
using FlexPad.App.ViewModels;
using FlexPad.App.Views;
using FlexPad.Core;

namespace FlexPad.App;

public partial class App : Application
{
    private AppConfig _config = new();
    private RadioService _radio = null!;
    private MainWindowViewModel _mainVm = null!;
    private SetupViewModel _setupVm = null!;
    private MainWindow? _main;
    private ConsoleWindow? _console;
    private SetupWindow? _setup;
    private bool _uninstalling;   // set once Uninstall() has run: the exit handler must then guarantee the process ends
    private readonly Dictionary<Window, WindowMemory> _memory = new();

    private WindowMemory Track(Window w) => _memory[w] = new WindowMemory(w);

    /// <summary>
    /// Run a fire-and-forget UI task so that a failure is written to crash.log and the console
    /// instead of vanishing. A dialog that silently never opens is worse than an error line.
    /// </summary>
    private void Fire(Func<Task> work, string what)
    {
        _ = work().ContinueWith(t =>
        {
            if (t.Exception is null) return;
            var ex = t.Exception.GetBaseException();
            CrashLog.Write(what, ex);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _radio.Fail($"{what} failed: {ex.Message}"));
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>Where the window is, from its own move events; the raw property only as a last resort.</summary>
    private PixelPoint Pos(Window w) => _memory.TryGetValue(w, out var m) && m.SavePosition(w) is { } p ? p : w.Position;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _config = ConfigStore.Load();

            // A copy installed by hand is adopted where it stands, and its shortcuts are re-asserted
            // every start. There is no installed-apps entry to maintain — see InstallService.
            try { InstallService.EnsureRegistered(); } catch { /* never block startup over this */ }

            _radio = new RadioService(_config);
            _mainVm = new MainWindowViewModel(_radio, () => _config);
            _mainVm.EditRequested += (b, isNew) => Fire(() => EditButtonAsync(b, isNew), "button editor");
            _mainVm.Changed += () => { SaveConfig(); RebuildButtons(); };
            _setupVm = new SetupViewModel(_config, () => _config.Buttons.Select(b => b.Label));
            _setupVm.ReloadRequested += ReloadConfig;

            _main = new MainWindow { DataContext = _mainVm };
            WindowMemory.Restore(_main, _config.Window.X, _config.Window.Y, _config.Window.Width, _config.Window.Height);
            Track(_main);
            _main.RebuildHotkeys(_mainVm, _radio.Fail);

            desktop.MainWindow = _main;
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
            desktop.Exit += (_, _) =>
            {
                SaveConfig();
                _radio.Dispose();
                // An uninstall run must end whatever the UI framework does next; see the W2/LP-100A
                // history in InstallService. Give normal shutdown a moment, then leave regardless.
                if (_uninstalling) _ = Task.Delay(3000).ContinueWith(_ => Environment.Exit(0));
            };

            var updateFailed = UpdateService.ConsumeUpdateFailed();
            if (updateFailed) _setupVm.NoteUpdateFailed();

            var openSetup = Environment.GetCommandLineArgs().Any(a => a.Equals("--setup", StringComparison.OrdinalIgnoreCase));
            _main.Opened += async (_, _) =>
            {
                if (Program.PendingUninstall)
                {
                    await RunUninstallAsync();
                    return;
                }

                if (_config.Window.ConsoleOpen) ShowConsole();

                if (InstallService.Mode == InstallMode.Loose && await OfferInstallAsync())
                    return;

                if (updateFailed) ShowSetup(SetupViewModel.UpdatesTab);
                else if (openSetup) ShowSetup();
                else if (Environment.GetCommandLineArgs().Any(a => a.Equals("--edit", StringComparison.OrdinalIgnoreCase)))
                    _mainVm.Add();   // debug: open the new-button editor straight away
                else if (Environment.GetCommandLineArgs().Any(a => a.Equals("--bands", StringComparison.OrdinalIgnoreCase)))
                    Fire(MakeBandSetAsync, "band set");   // debug: open the band-set generator straight away

                // Debug: exercise the update-restart exit path (every window closed by the app) without
                // an update. Found the double-close that zeroed Setup's saved position, 2026-09-14.
                if (Environment.GetCommandLineArgs().Any(a => a.Equals("--exit-for-update", StringComparison.OrdinalIgnoreCase)))
                    _ = Task.Delay(4000).ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.Post(ExitForUpdate));

                if (_config.CheckUpdatesAtStartup)
                {
                    await _setupVm.CheckUpdatesAsync();
                    if (_setupVm.UpdateAvailable)
                    {
                        _mainVm.UpdateAvailable = _setupVm.LatestTag;
                        _radio.Note($"update available: {_setupVm.LatestTag} (you have {UpdateService.CurrentVersion}) - Setup, Updates");
                    }
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    // --- buttons ---

    private void RebuildButtons()
    {
        _mainVm.Rebuild();
        _main?.RebuildHotkeys(_mainVm, _radio.Fail);
    }

    private async Task EditButtonAsync(ButtonConfig button, bool isNew)
    {
        if (_main is null) return;
        var vm = new ButtonEditorViewModel(button, _radio, isNew);
        var win = new ButtonEditorWindow { DataContext = vm };
        var w = _config.Window;
        WindowMemory.Restore(win, w.EditorX, w.EditorY, w.EditorWidth, w.EditorHeight);
        Track(win);
        var saved = await win.ShowDialog<bool>(_main);
        if (!saved) return;
        vm.ApplyTo(button);
        if (isNew) _config.Buttons.Add(button);
        SaveConfig();
        RebuildButtons();
    }

    /// <summary>The band-set generator: a dialog, then a batch of ordinary buttons.</summary>
    public async Task MakeBandSetAsync()
    {
        if (_main is null) return;
        var vm = new BandSetViewModel(_radio);
        var win = new BandSetWindow { DataContext = vm };
        var w = _config.Window;
        WindowMemory.Restore(win, w.BandSetX, w.BandSetY);
        Track(win);
        if (!await win.ShowDialog<bool>(_main)) return;

        var made = vm.Generate();
        var added = 0; var replaced = 0;
        foreach (var g in made)
        {
            var group = vm.BandRow ? ButtonConfig.BandGroup : null;
            var button = new ButtonConfig { Label = g.Label, Key = g.Key, Color = g.Color, Commands = g.Lines, Group = group };
            var existing = vm.ReplaceSameLabel ? _config.Buttons.FirstOrDefault(b => b.Label == g.Label) : null;
            if (existing is not null)
            {
                existing.Key = g.Key ?? existing.Key;
                existing.Color = g.Color ?? existing.Color;
                existing.Commands = g.Lines;
                existing.Group = group;
                replaced++;
            }
            else
            {
                _config.Buttons.Add(button);
                added++;
            }
        }
        SaveConfig();
        RebuildButtons();
        _radio.Note($"band set: {added} button(s) added, {replaced} replaced");
    }

    public void OpenBandSet() => Fire(MakeBandSetAsync, "band set");

    public void NotifyBandSetClosing(Window w)
    {
        if (!_memory.ContainsKey(w)) return;   // already recorded
        var p = Pos(w);
        _config.Window.BandSetX = p.X; _config.Window.BandSetY = p.Y;
        _memory.Remove(w);
    }

    private void ReloadConfig()
    {
        _config = ConfigStore.Load();
        _radio.Apply(_config);
        _setupVm.LoadFrom(_config, _config.Buttons.Select(b => b.Label));
        RebuildButtons();
        _radio.Note("config reloaded from disk");
    }

    // --- windows ---

    public void ShowConsole()
    {
        if (_console is null)
        {
            _console = new ConsoleWindow { DataContext = new ConsoleViewModel(_radio) };
            WindowMemory.Restore(_console, _config.Window.ConsoleX, _config.Window.ConsoleY);
            Track(_console);
            _console.Show();
        }
        else _console.Show();
        _console.Activate();
    }

    public void NotifyConsoleClosing(ConsoleWindow w)
    {
        if (!ReferenceEquals(_console, w)) return;   // already recorded: see NotifyMainWindowClosing
        var p = Pos(w);
        _config.Window.ConsoleX = p.X;
        _config.Window.ConsoleY = p.Y;
        _memory.Remove(w);
        (w.DataContext as ConsoleViewModel)?.Dispose();
        _console = null;
    }

    public void ShowReference(Window owner)
    {
        var win = new ReferenceWindow(_radio);
        var w = _config.Window;
        WindowMemory.Restore(win, w.ReferenceX, w.ReferenceY, w.ReferenceWidth, w.ReferenceHeight);
        Track(win);
        win.Show(owner);
    }

    /// <summary>Dialogs record their own bounds on the way out, while the position is still real.</summary>
    public void NotifyEditorClosing(Window w)
    {
        if (!_memory.ContainsKey(w)) return;   // already recorded
        var p = Pos(w);
        _config.Window.EditorX = p.X; _config.Window.EditorY = p.Y;
        _config.Window.EditorWidth = w.Width; _config.Window.EditorHeight = w.Height;
        _memory.Remove(w);
        SaveConfig();
    }

    public void NotifyReferenceClosing(Window w)
    {
        if (!_memory.ContainsKey(w)) return;   // already recorded
        var p = Pos(w);
        _config.Window.ReferenceX = p.X; _config.Window.ReferenceY = p.Y;
        _config.Window.ReferenceWidth = w.Width; _config.Window.ReferenceHeight = w.Height;
        _memory.Remove(w);
        SaveConfig();
    }

    /// <param name="tab">Tab to select first, for when Setup is opened to show something specific.</param>
    public void ShowSetup(int? tab = null)
    {
        if (tab is not null) _setupVm.SelectedTabIndex = tab.Value;
        if (_setup is null)
        {
            _setupVm.LoadFrom(_config, _config.Buttons.Select(b => b.Label));
            if (tab is not null) _setupVm.SelectedTabIndex = tab.Value;
            _setup = new SetupWindow { DataContext = _setupVm };
            WindowMemory.Restore(_setup, _config.Window.SetupX, _config.Window.SetupY);
            Track(_setup);
            _setup.Show();
        }
        else _setup.Show();
        _setup.Activate();
    }

    /// <summary>Setup applies its edits on close: connection, grid, knob.</summary>
    public void NotifySetupClosing(SetupWindow w)
    {
        if (!ReferenceEquals(_setup, w)) return;   // already recorded: see NotifyMainWindowClosing
        var p = Pos(w);
        _config.Window.SetupX = p.X;
        _config.Window.SetupY = p.Y;
        _memory.Remove(w);
        _setup = null;
        if (_uninstalling) return;
        _setupVm.ApplyTo(_config);
        SaveConfig();
        _radio.Apply(_config);
        RebuildButtons();
    }

    public void NotifyMainWindowClosing(MainWindow w)
    {
        // Each of these can run twice: CloseAllWindows closes every window in a loop, but closing
        // the main window already cascades to the others, so the second Close finds a window whose
        // tracker is gone and whose Position reads (0,0) — which then overwrote the good value on
        // every update restart (Setup came back at the top left after each update, 2026-09-14).
        // A window we no longer hold has already been recorded; do nothing.
        if (!ReferenceEquals(_main, w)) return;
        var p = Pos(w);
        _config.Window.X = p.X;
        _config.Window.Y = p.Y;
        _config.Window.Width = w.Width;
        _config.Window.Height = w.Height;
        _config.Window.ConsoleOpen = _console is not null;
        // Forget the window now that its bounds are recorded. A closed window reports its position
        // as (0,0), and the Exit handler saves once more after every window has gone; with the
        // reference still set, that final save overwrote the real position and every relaunch
        // — after an update, or a plain close and reopen — came back at the top left (0.4.1-beta,
        // seen on Windows and Fedora alike). W2 nulls its reference here for the same reason.
        _main = null;
        // The other windows are top-level, not owned; under OnLastWindowClose they would hold the
        // app open after the panel is gone. Close them with it.
        _console?.Close();
        _setup?.Close();
        SaveConfig();
    }

    /// <summary>Close every window so the staged update helper can swap the executable and relaunch.</summary>
    public void ExitForUpdate() => CloseAllWindows();

    private void CloseAllWindows()
    {
        foreach (var w in AllWindows().ToList()) w.Close();
    }

    /// <summary>
    /// Close every window from a later dispatcher frame, never from inside the input event still
    /// being delivered. Every caller runs as the continuation of a dialog answer, i.e. inside the
    /// mouse-release that clicked the button; closing the window tree from in there left a
    /// windowless process that never exited (LP-100A, 2026-09-04, real mouse only).
    /// </summary>
    private void CloseAllWindowsWhenIdle() =>
        Avalonia.Threading.Dispatcher.UIThread.Post(CloseAllWindows, Avalonia.Threading.DispatcherPriority.Background);

    private IEnumerable<Window> AllWindows()
    {
        if (_main is not null) yield return _main;
        if (_console is not null) yield return _console;
        if (_setup is not null) yield return _setup;
    }

    /// <summary>Modal confirmation, owned by whatever window is available.</summary>
    /// <param name="negative">Null for a one-button message that only reports an outcome.</param>
    public Task<bool> ConfirmAsync(string title, string message,
        string affirmative = "Continue", string? negative = "Cancel", string? detail = null)
    {
        var owner = (Window?)_setup ?? _main;
        var dlg = new ConfirmWindow(title, message, affirmative, negative, detail);
        return owner is not null ? dlg.ShowDialog<bool>(owner) : Task.FromResult(false);
    }

    private Task NotifyAsync(string title, string message, string? detail = null) =>
        ConfirmAsync(title, message, affirmative: "OK", negative: null, detail: detail);

    // --- install / uninstall (the family's shared flow) ---

    private async Task<bool> OfferInstallAsync()
    {
        var accepted = await ConfirmAsync(
            $"Install {InstallService.DisplayName}",
            $"Install {InstallService.DisplayName} on this computer?",
            affirmative: "Install",
            negative: "Not now",
            detail: $"Copies the program to {InstallService.InstallDirectory} and adds "
                  + (OperatingSystem.IsWindows()
                      ? "Start Menu and desktop shortcuts. It will not appear in Settings → Apps; to "
                        + "remove it later, use Setup → Updates → Remove."
                      : "it to your applications menu, with a desktop launcher and a flexpad command.")
                  + " Your buttons and settings are untouched either way.\n\n"
                  + "To run from here permanently without being asked again, put a file named "
                  + $"{InstallLayout.PortableMarker} beside the program.");

        if (!accepted) return false;

        try
        {
            var installed = InstallService.Install();
            if (!installed.Registered)
            {
                await NotifyAsync(
                    "Installed, but no shortcut",
                    $"{InstallService.DisplayName} was installed to {InstallService.InstallDirectory}, "
                    + (OperatingSystem.IsWindows()
                        ? "but a Start Menu shortcut could not be created."
                        : "but its applications-menu entry could not be written."),
                    "The program itself works normally, and you can remove it from Setup → Updates at any time.");
            }
            InstallService.LaunchDetached(installed.ExePath);
            CloseAllWindowsWhenIdle();
            return true;
        }
        catch (InstallBlockedException ex)
        {
            await NotifyAsync("Could not install", ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            await NotifyAsync("Could not install", $"The install did not complete: {ex.Message}");
            return false;
        }
    }

    internal async Task RunUninstallAsync()
    {
        var confirmed = await ConfirmAsync(
            $"Remove {InstallService.DisplayName}",
            $"Remove {InstallService.DisplayName} from this computer?",
            affirmative: "Remove",
            negative: "Cancel",
            detail: $"Deletes the program from {InstallService.InstallDirectory}.");

        if (!confirmed) { CloseAllWindowsWhenIdle(); return; }

        var removeSettings = await ConfirmAsync(
            "Remove settings too?",
            "Also delete your saved settings?",
            affirmative: "Delete settings",
            negative: "Keep settings",
            detail: "Every button you wrote, the radio address and the knob bindings, "
                  + $"in {ConfigStore.DataDir}. Keeping them means a later install picks up where you left off.");

        _uninstalling = true;
        try { InstallService.Uninstall(new UninstallOptions(removeSettings)); }
        catch (Exception ex) { await NotifyAsync("Could not uninstall", ex.Message); }

        CloseAllWindowsWhenIdle();
    }

    // --- config ---

    private void SaveConfig()
    {
        try
        {
            // Only a window that is still open has a position worth recording; each Notify*Closing
            // records its own bounds and drops the reference, so a closed one is never read here.
            if (_main is { IsVisible: true })
            {
                var p = Pos(_main); _config.Window.X = p.X; _config.Window.Y = p.Y;
                _config.Window.Width = _main.Width; _config.Window.Height = _main.Height;
            }
            if (_console is { IsVisible: true }) { var c = Pos(_console); _config.Window.ConsoleX = c.X; _config.Window.ConsoleY = c.Y; }
            if (_setup is { IsVisible: true }) { var st = Pos(_setup); _config.Window.SetupX = st.X; _config.Window.SetupY = st.Y; }
            if (_main is not null) _config.Window.ConsoleOpen = _console is not null;
            _config.CheckUpdatesAtStartup = _setupVm.CheckUpdatesAtStartup;
            _config.Window.SetupTab = _setupVm.SelectedTabIndex;
            ConfigStore.Save(_config);
        }
        catch { /* best effort */ }
    }
}
