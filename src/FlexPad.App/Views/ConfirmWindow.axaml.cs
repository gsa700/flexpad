using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace FlexPad.App.Views;

/// <summary>Minimal modal Yes/No dialog. Use via <c>await new ConfirmWindow(title, msg).ShowDialog&lt;bool&gt;(owner)</c>.</summary>
public partial class ConfirmWindow : Window
{
    public ConfirmWindow() => InitializeComponent();

    /// <param name="affirmative">
    /// Label for the button that returns true. Name the action ("Install", "Remove") rather than
    /// saying "OK" — the button text is what a user actually reads before committing to something
    /// that changes their machine.
    /// </param>
    /// <param name="negative">
    /// Label for the button that returns false, or null for a one-button message where there is
    /// nothing to decline — the outcome has already happened and is only being reported.
    /// </param>
    /// <param name="detail">Secondary line spelling out the consequences. Hidden when null.</param>
    public ConfirmWindow(string title, string message,
        string affirmative = "Continue", string? negative = "Cancel", string? detail = null) : this()
    {
        Title = title;
        this.FindControl<TextBlock>("MessageText")!.Text = message;
        this.FindControl<Button>("AffirmativeButton")!.Content = affirmative;

        var no = this.FindControl<Button>("NegativeButton")!;
        if (negative is null) no.IsVisible = false;
        else no.Content = negative;

        if (!string.IsNullOrWhiteSpace(detail))
        {
            var d = this.FindControl<TextBlock>("DetailText")!;
            d.Text = detail;
            d.IsVisible = true;
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnContinue(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
