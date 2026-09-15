using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using FlexPad.App.Services;
using FlexPad.Core;

namespace FlexPad.App.Views;

/// <summary>The cheat sheet, plus the antenna ports this particular radio reports.</summary>
public partial class ReferenceWindow : Window
{
    public ReferenceWindow() => InitializeComponent();

    public ReferenceWindow(RadioService radio) : this()
    {
        this.FindControl<TextBlock>("Body")!.Text = Reference.Text;
        var ports = this.FindControl<TextBlock>("Ports")!;
        const string prefix = "Antenna ports on this radio: ";
        if (!radio.Connected) { ports.Text = prefix + "(not connected)"; return; }
        ports.Text = prefix + "asking…";
        _ = Task.Run(() =>
        {
            string answer;
            try
            {
                var (code, text) = radio.Client.Send("ant list");
                answer = code == 0 ? text.Replace(",", "  ") : "(no answer)";
            }
            catch (NotConnectedException) { answer = "(not connected)"; }
            Dispatcher.UIThread.Post(() => ports.Text = prefix + answer);
        });
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnWikiClick(object? sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(Reference.WikiUrl) { UseShellExecute = true }); } catch { }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
