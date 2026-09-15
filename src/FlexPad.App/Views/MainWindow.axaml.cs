using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FlexPad.App.ViewModels;

namespace FlexPad.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private App? AppRef => Avalonia.Application.Current as App;

    private void OnAddClick(object? sender, RoutedEventArgs e) => (DataContext as MainWindowViewModel)?.Add();
    private void OnBandSetClick(object? sender, RoutedEventArgs e) => AppRef?.OpenBandSet();
    private void OnConsoleClick(object? sender, RoutedEventArgs e) => AppRef?.ShowConsole();
    private void OnReferenceClick(object? sender, RoutedEventArgs e) => AppRef?.ShowReference(this);
    private void OnSetupClick(object? sender, RoutedEventArgs e) => AppRef?.ShowSetup();

    /// <summary>
    /// Hotkeys are window-level key bindings, rebuilt whenever the button list changes. A gesture
    /// that doesn't parse is skipped and reported, never fatal.
    /// </summary>
    public void RebuildHotkeys(MainWindowViewModel vm, Action<string> report)
    {
        KeyBindings.Clear();
        foreach (var b in vm.Buttons)
        {
            if (!b.HasHotkey) continue;
            try
            {
                KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse(b.Hotkey), Command = b.FireCommand });
            }
            catch (Exception)
            {
                report($"hotkey '{b.Hotkey}' on button '{b.Label}' is not a key gesture I understand");
            }
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        AppRef?.NotifyMainWindowClosing(this);
        base.OnClosing(e);
    }
}
