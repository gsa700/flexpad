using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FlexPad.App.ViewModels;

namespace FlexPad.App.Views;

/// <summary>
/// Guided start for a new button. ShowDialog returns "save" (make the button), "edit" (carry what
/// was chosen into the full editor), "bands" (open the band-set generator instead) or null.
/// </summary>
public partial class NewButtonWindow : Window
{
    public const string Save = "save", Edit = "edit", Bands = "bands";

    public NewButtonWindow() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private NewButtonViewModel? Vm => DataContext as NewButtonViewModel;

    private void OnNext(object? sender, RoutedEventArgs e)
    {
        if (Vm?.Next() is { } result) { Close(result); return; }
        // Page two: put the caret in the label so a name can be typed at once; Enter still saves.
        if (Vm?.IsNaming == true && this.FindControl<TextBox>("LabelBox") is { } box)
        {
            box.Focus();
            box.SelectAll();
        }
    }

    private void OnBack(object? sender, RoutedEventArgs e) => Vm?.Back();
    private void OnSave(object? sender, RoutedEventArgs e) => Close(Save);
    private void OnEdit(object? sender, RoutedEventArgs e) => Close(Edit);
    private void OnBands(object? sender, RoutedEventArgs e) => Close(Bands);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        (Avalonia.Application.Current as App)?.NotifyNewButtonClosing(this);
        base.OnClosing(e);
    }
}
