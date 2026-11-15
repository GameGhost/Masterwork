using Masterwork.App.Shared.Services;

namespace Masterwork.App;

public partial class App : Application
{
    private readonly WhiteLabelConfig _whiteLabel;

    public App(WhiteLabelConfig whiteLabel)
    {
        _whiteLabel = whiteLabel;
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
            Title = _whiteLabel.AppName,
            MinimumWidth = 800,
            MinimumHeight = 600,
        };
    }
}
