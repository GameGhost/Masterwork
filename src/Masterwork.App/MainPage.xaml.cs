using Masterwork.App.Shared.Services;

namespace Masterwork.App;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }

    // Android's hardware back button isn't wired to any browser-style navigation history by
    // default (unlike a real browser tab) — this makes it act like one, via HardwareBackButtonBridge
    // (MainLayout.razor registers the app's own NavigationManager/AppNavigationHistory there, since
    // BlazorWebView doesn't publicly expose its DI container to host code like this). Navigating
    // through the same NavigationManager the rest of the app already uses means Play.razor's own
    // RegisterLocationChangingHandler still gets a say: pressing back while a session is active gets
    // intercepted there exactly like browser back/forward does on the web head, showing the quit
    // flow instead of silently leaving. Returning false (nothing to go back to, or nothing
    // registered yet) falls through to the platform's own default — exiting the app, the expected
    // behavior once there's nowhere left to go.
    protected override bool OnBackButtonPressed() =>
        HardwareBackButtonBridge.TryGoBack() || base.OnBackButtonPressed();
}
