using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
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
    // Windows, X11 and XWayland. Feedback while carrying (David, 0.9.0: "a little hard to visualize
    // what is happening"): a ghost of the button rides under the pointer in the overlay canvas,
    // the original dims to a hole, and whatever the drop would land on is outlined.
    private const double DragThreshold = 8;
    private const double GhostScale = 0.6;   // a tile in hand, small enough that the outlined target shows around it
    private Button? _dragSource;
    private Point _dragStart;
    private Point _grabOffset;
    private bool _dragging;
    private Cursor? _savedCursor;
    private Border? _ghost;
    private Button? _targetButton;
    private ItemsControl? _targetRow;
    private IBrush? _targetRowBackground;
    private readonly Canvas _layer;   // the overlay canvas; our InitializeComponent bypasses the generated field lookup

    public MainWindow()
    {
        InitializeComponent();
        _layer = this.FindControl<Canvas>("DragLayer") ?? throw new InvalidOperationException("DragLayer missing from MainWindow.axaml");
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


    /// <summary>Topmost visual under the point, ignoring the overlay: the ghost rides under the pointer.</summary>
    private Visual? HitBelowGhost(Point p) =>
        this.GetVisualsAt(p).FirstOrDefault(v => !ReferenceEquals(v, _layer) && !_layer.IsVisualAncestorOf(v));

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
        _grabOffset = e.GetPosition(button);
        _dragging = false;
    }

    private void OnDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragSource is null) return;
        var pos = e.GetPosition(this);
        if (!_dragging)
        {
            var d = pos - _dragStart;
            if (Math.Abs(d.X) < DragThreshold && Math.Abs(d.Y) < DragThreshold) return;
            BeginDrag();
        }
        MoveGhost(e.GetPosition(_layer));
        Highlight(HitBelowGhost(pos));
    }

    private void BeginDrag()
    {
        var source = _dragSource!;
        _dragging = true;
        _savedCursor = Cursor;
        Cursor = new Cursor(StandardCursorType.DragMove);

        // The ghost: same size and colours as the button, riding under the pointer where it was grabbed.
        var label = (source.DataContext as ButtonViewModel)?.Label ?? "";
        _ghost = new Border
        {
            Width = source.Bounds.Width * GhostScale,
            Height = source.Bounds.Height * GhostScale,
            Background = source.Background,
            BorderBrush = Palette.CyanBrush,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(5),
            Opacity = 0.9,
            IsHitTestVisible = false,
            BoxShadow = new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = 6, Blur = 18, Color = Color.FromArgb(160, 0, 0, 0) }),
            Child = new TextBlock
            {
                Text = label,
                Foreground = source.Foreground,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        _layer.Children.Add(_ghost);
        source.Opacity = 0.25;   // the hole it came from
    }

    private void MoveGhost(Point p)
    {
        if (_ghost is null) return;
        Canvas.SetLeft(_ghost, p.X - _grabOffset.X * GhostScale);
        Canvas.SetTop(_ghost, p.Y - _grabOffset.Y * GhostScale);
    }

    /// <summary>Outline the button the drop would take the place of, or tint the row it would join.</summary>
    private void Highlight(Visual? hit)
    {
        var button = GridButtonOf(hit);
        if (button is not null && ReferenceEquals(button, _dragSource)) button = null;
        var row = button is null ? RowOf(hit) : null;
        if (!ReferenceEquals(button, _targetButton))
        {
            if (_targetButton is not null) _targetButton.BorderThickness = new Thickness(0);
            _targetButton = button;
            if (_targetButton is not null)
            {
                _targetButton.BorderBrush = Palette.CyanBrush;
                _targetButton.BorderThickness = new Thickness(2);
            }
        }
        if (!ReferenceEquals(row, _targetRow))
        {
            if (_targetRow is not null) _targetRow.Background = _targetRowBackground;
            _targetRow = row;
            if (_targetRow is not null)
            {
                _targetRowBackground = _targetRow.Background;
                _targetRow.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            }
        }
    }

    private void OnDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var source = _dragSource;
        if (source is null) return;
        if (!_dragging) { _dragSource = null; return; }   // a click: let the button fire

        e.Handled = true;   // the drop is not a click
        if (DataContext is MainWindowViewModel vm && source.DataContext is ButtonViewModel from)
        {
            var hit = HitBelowGhost(e.GetPosition(this));
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
        Highlight(null);
        if (_ghost is not null) { _layer.Children.Remove(_ghost); _ghost = null; }
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
