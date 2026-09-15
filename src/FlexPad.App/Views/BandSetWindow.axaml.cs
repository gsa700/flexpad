using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FlexPad.App.ViewModels;

namespace FlexPad.App.Views;

/// <summary>Modal band-set generator. Returns true from ShowDialog when the user asked for buttons.</summary>
public partial class BandSetWindow : Window
{
    public BandSetWindow() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnMake(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
    private void OnAll(object? sender, RoutedEventArgs e) => (DataContext as BandSetViewModel)?.SelectAll(true);
    private void OnNone(object? sender, RoutedEventArgs e) => (DataContext as BandSetViewModel)?.SelectAll(false);

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        (Avalonia.Application.Current as App)?.NotifyBandSetClosing(this);
        base.OnClosing(e);
    }
}
