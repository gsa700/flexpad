using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FlexPad.App.ViewModels;

namespace FlexPad.App.Views;

/// <summary>Modal editor for one button. Returns true from ShowDialog when saved.</summary>
public partial class ButtonEditorWindow : Window
{
    public ButtonEditorWindow()
    {
        InitializeComponent();

        // The example snippets are a static list, so the menu is built once here rather than
        // templated: a MenuFlyout with a handler per item is simpler than an items-source binding.
        var flyout = new MenuFlyout();
        foreach (var (name, lines) in Reference.Examples)
        {
            var item = new MenuItem { Header = name };
            var snippet = lines;
            item.Click += (_, _) => (DataContext as ButtonEditorViewModel)?.Insert(snippet);
            flyout.Items.Add(item);
        }
        this.FindControl<DropDownButton>("ExamplesButton")!.Flyout = flyout;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnSave(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        (Avalonia.Application.Current as App)?.NotifyEditorClosing(this);
        base.OnClosing(e);
    }
    private void OnReferenceClick(object? sender, RoutedEventArgs e) => (Avalonia.Application.Current as App)?.ShowReference(this);
}
