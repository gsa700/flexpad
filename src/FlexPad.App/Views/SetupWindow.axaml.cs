using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace FlexPad.App.Views;

public partial class SetupWindow : Window
{
    public SetupWindow() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        (Application.Current as App)?.NotifySetupClosing(this);
        base.OnClosing(e);
    }
}
