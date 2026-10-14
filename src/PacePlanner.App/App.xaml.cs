using Microsoft.UI.Xaml;

namespace PacePlanner.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();

        // Last line of defence: show unexpected errors in the window instead of crashing.
        UnhandledException += (_, e) =>
        {
            if (_window is not null)
            {
                e.Handled = true;
                _window.ViewModel.ReportUnexpectedError(e.Exception);
            }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
