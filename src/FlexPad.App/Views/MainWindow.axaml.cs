using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using FlexPad.App.ViewModels;

namespace FlexPad.App.Views;

public partial class MainWindow : Window
{
    // Drag to rearrange. Press a button and move past the threshold to pick it up; release over
    // another button to take its slot (and its row), or over empty space in the grid or the band
    // row to go to the end of that row. A plain click never moves anything, right-click still opens
    // the menu, and the release that ends a drag is swallowed so no button fires. Done with pointer
    // events and a hit test rather than the platform drag-and-drop, so it behaves the same on
    // Windows, X11 and XWayland and needs no drag image.
    private const double DragThreshold = 8;
    private Button? _dragSource;
    private Point _dragStart;
    private bool _dragging;
    private Cursor? _savedCursor;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnDragPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnDragPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnDragPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerCaptureLostEvent, OnDragCaptureLost, RoutingStrategies.Bubble);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private App? AppRef => Avalonia.Application.Current as App;

    private void OnAddClick(object? sender, RoutedEventArgs e) => (DataContext as MainWindowViewModel)?.Add();
    private void OnBandSetClick(object? sender, RoutedEventArgs e) => AppRef?.OpenBandSet();
    private void OnConsoleClick(object? sender, RoutedEventArgs e) => AppRef?.ShowConsole();
    private void OnReferenceClick(object? sender, RoutedEventArgs e) => AppRef?.ShowReference(this);
    private void OnSetupClick(object? sender, RoutedEventArgs e) => AppRef?.ShowSetup();

    // --- drag to rearrange ---

    private static Button? GridButtonOf(Visual? v) =>
        v?.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault(b => b.DataContext is ButtonViewModel);

    private static ItemsControl? RowOf(Visual? v) =>
        v?.GetSelfAndVisualAncestors().OfType<ItemsControl>().FirstOrDefault(c => c.Name is "MainGrid" or "BandRow");

    private void OnDragPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var button = GridButtonOf(e.Source as Visual);
        if (button is null) return;
        _dragSource = button;
        _dragStart = e.GetPosition(this);
        _dragging = false;
    }

    private void OnDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragSource is null || _dragging) return;
        var d = e.GetPosition(this) - _dragStart;
        if (Math.Abs(d.X) < DragThreshold && Math.Abs(d.Y) < DragThreshold) return;
        _dragging = true;
        _dragSource.Opacity = 0.5;
        _savedCursor = Cursor;
        Cursor = new Cursor(StandardCursorType.DragMove);
    }

    private void OnDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var source = _dragSource;
        if (source is null) return;
        if (!_dragging) { _dragSource = null; return; }   // a click: let the button fire

        e.Handled = true;   // the drop is not a click
        if (DataContext is MainWindowViewModel vm && source.DataContext is ButtonViewModel from)
        {
            var hit = this.GetVisualAt(e.GetPosition(this));
            var target = GridButtonOf(hit);
            if (target is not null && !ReferenceEquals(target, source) && target.DataContext is ButtonViewModel to)
                vm.DropOnto(from.Config, to.Config);
            else if (target is null && RowOf(hit) is { } row)
                vm.DropInto(from.Config, bandRow: row.Name == "BandRow");
        }
        EndDrag();
    }

    private void OnDragCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_dragging) EndDrag();
    }

    private void EndDrag()
    {
        if (_dragSource is { } s) s.Opacity = 1;
        if (_dragging) Cursor = _savedCursor;
        _dragSource = null;
        _dragging = false;
    }

    /// <summary>
    /// Hotkeys are window-level key bindings, rebuilt whenever the button list changes. A gesture
    /// that doesn't parse is skipped and reported, never fatal.
    /// </summary>
    public void RebuildHotkeys(MainWindowViewModel vm, Action<string> report)
    {
        KeyBindings.Clear();
        foreach (var b in vm.Buttons.Concat(vm.BandButtons))
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
