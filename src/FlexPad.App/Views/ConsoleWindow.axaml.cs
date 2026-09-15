using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using FlexPad.App.ViewModels;

namespace FlexPad.App.Views;

public partial class ConsoleWindow : Window
{
    private ConsoleViewModel? _vm;

    public ConsoleWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.Appended -= ScrollToEnd;
            _vm = DataContext as ConsoleViewModel;
            if (_vm is not null) _vm.Appended += ScrollToEnd;
            ScrollToEnd();
        };
        Opened += (_, _) => ScrollToEnd();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Follow the newest line, after layout has placed it.</summary>
    private void ScrollToEnd() =>
        Dispatcher.UIThread.Post(() => this.FindControl<ScrollViewer>("Scroller")?.ScrollToEnd(), DispatcherPriority.Background);

    private void OnCommandKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        switch (e.Key)
        {
            case Key.Enter:
                _vm.SendCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Up:
                _vm.HistoryStep(-1);
                e.Handled = true;
                break;
            case Key.Down:
                _vm.HistoryStep(+1);
                e.Handled = true;
                break;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        (Avalonia.Application.Current as App)?.NotifyConsoleClosing(this);
        base.OnClosing(e);
    }
}
