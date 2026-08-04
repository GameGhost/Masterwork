using Microsoft.UI;

namespace Masterwork.App.Platforms.Windows;

/// <summary>
/// Captures the app's single window id once at startup, for native Windows APIs that need one to
/// associate themselves with — <see cref="Microsoft.Windows.Storage.Pickers.FileOpenPicker"/>
/// (see <c>MauiNativeFilePicker</c>'s own remarks for why the older <c>Windows.Storage.Pickers</c>
/// API it replaced was unreliable for an unpackaged app) and <see cref="Microsoft.Windows.Storage.Pickers.FileSavePicker"/>
/// (see <c>FileSaveStore</c>'s own remarks — same class of fix, for save export instead of import).
/// </summary>
public static class MainWindowState
{
    public static WindowId? WindowId { get; private set; }

    public static void Initialize(WindowId windowId) => WindowId = windowId;
}
