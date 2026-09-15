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
            _mainVm.EditRequested += (b, isNew) => _ = EditButtonAsync(b, isNew);
            _mainVm.Changed += () => { SaveConfig(); RebuildButtons(); };
            _setupVm = new SetupViewModel(_config, () => _config.Buttons.Select(b => b.Label));
            _setupVm.ReloadRequested += ReloadConfig;

            _main = new MainWindow { DataContext = _mainVm };
            RestoreBounds(_main, _config.Window.X, _config.Window.Y);
            if (_config.Window is { Width: > 100, Height: > 80 } w) { _main.Width = w.Width.Value; _main.Height = w.Height.Value; }
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
        var saved = await win.ShowDialog<bool>(_main);
        if (!saved) return;
        vm.ApplyTo(button);
        if (isNew) _config.Buttons.Add(button);
        SaveConfig();
        RebuildButtons();
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
            RestoreBounds(_console, _config.Window.ConsoleX, _config.Window.ConsoleY);
            _console.Show();
        }
        else _console.Show();
        _console.Activate();
    }

    public void NotifyConsoleClosing(ConsoleWindow w)
    {
        _config.Window.ConsoleX = w.Position.X;
        _config.Window.ConsoleY = w.Position.Y;
        (w.DataContext as ConsoleViewModel)?.Dispose();
        _console = null;
    }

    public void ShowReference(Window owner)
    {
        var w = new ReferenceWindow(_radio);
        w.Show(owner);
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
            RestoreBounds(_setup, _config.Window.SetupX, _config.Window.SetupY);
            _setup.Show();
        }
        else _setup.Show();
        _setup.Activate();
    }

    /// <summary>Setup applies its edits on close: connection, grid, knob.</summary>
    public void NotifySetupClosing(SetupWindow w)
    {
        _config.Window.SetupX = w.Position.X;
        _config.Window.SetupY = w.Position.Y;
        _setup = null;
        if (_uninstalling) return;
        _setupVm.ApplyTo(_config);
        SaveConfig();
        _radio.Apply(_config);
        RebuildButtons();
    }

    public void NotifyMainWindowClosing(MainWindow w)
    {
        _config.Window.X = w.Position.X;
        _config.Window.Y = w.Position.Y;
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

    private static void RestoreBounds(Window w, double? x, double? y)
    {
        if (x is not null && y is not null)
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Position = new PixelPoint((int)x.Value, (int)y.Value);
        }
        else
        {
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void SaveConfig()
    {
        try
        {
            // Only a window that is still open has a position worth recording; each Notify*Closing
            // records its own bounds and drops the reference, so a closed one is never read here.
            if (_main is { IsVisible: true })
            {
                _config.Window.X = _main.Position.X; _config.Window.Y = _main.Position.Y;
                _config.Window.Width = _main.Width; _config.Window.Height = _main.Height;
            }
            if (_console is { IsVisible: true }) { _config.Window.ConsoleX = _console.Position.X; _config.Window.ConsoleY = _console.Position.Y; }
            if (_setup is { IsVisible: true }) { _config.Window.SetupX = _setup.Position.X; _config.Window.SetupY = _setup.Position.Y; }
            if (_main is not null) _config.Window.ConsoleOpen = _console is not null;
            _config.CheckUpdatesAtStartup = _setupVm.CheckUpdatesAtStartup;
            _config.Window.SetupTab = _setupVm.SelectedTabIndex;
            ConfigStore.Save(_config);
        }
        catch { /* best effort */ }
    }
}
