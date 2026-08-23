namespace Masterwork.App.Shared.Chrome;

/// <summary>
/// Which footer actions <see cref="OptionsDialog"/> shows — <see cref="MainMenu"/> adds
/// "Manage Modules", <see cref="InGame"/> adds "Quit" (with its own 3-way confirm).
/// </summary>
public enum OptionsDialogContext
{
    MainMenu,
    InGame,
}
