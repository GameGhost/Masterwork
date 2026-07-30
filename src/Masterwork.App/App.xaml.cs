namespace Masterwork.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // MinimumWidth/Height are MAUI's own cross-platform Window properties (DIPs, not raw
        // pixels) -- on Windows this maps to OverlappedPresenter.PreferredMinimumWidth/Height
        // under the hood, already DPI-correct, so there's no need to reach for the native WinUI
        // AppWindow API the way the fullscreen toggle (MauiProgram.cs) has to.
        return new Window(new MainPage())
        {
            Title = "Masterwork - My Father's Work",
            MinimumWidth = 800,
            MinimumHeight = 600,
        };
    }
}
