using Masterwork.App.Shared.Resources;
using Masterwork.App.Shared.Services;

namespace Masterwork.App.Services;

/// <inheritdoc cref="INativeFilePicker"/>
/// <remarks>
/// MAUI-only — lives in the MAUI head rather than Shared because a native file picker isn't
/// available to a plain Razor Class Library. Picks entirely outside the <c>BlazorWebView</c>'s own
/// WebView2 (Windows) / native WebView (Android) host, which is the whole point — see
/// <see cref="INativeFilePicker"/>'s own remarks for why that matters.
///
/// Windows uses <c>Microsoft.Windows.Storage.Pickers.FileOpenPicker</c> directly instead of
/// <see cref="Microsoft.Maui.Storage.FilePicker"/> — a real-world report (v0.1.0, unpackaged
/// self-contained build) hit <c>COMException 0x80004005</c> from MAUI's own picker every time on
/// one user's machine while never reproducing on others, on a build already confirmed to have the
/// VC++ redistributable installed and not running elevated — i.e. some other window-association
/// failure inside MAUI's internal picker wrapper (a known, still-open class of MAUI issue on
/// Windows; see dotnet/maui#27552, #2194). The older <c>Windows.Storage.Pickers</c> API MAUI's
/// wrapper uses underneath also has its own well-documented unpackaged-app gaps (e.g. failing
/// outright when elevated, ruled out for that report but not necessarily for every future one).
/// <c>Microsoft.Windows.Storage.Pickers</c> is the newer Windows App SDK replacement built
/// specifically for unpackaged desktop apps — it takes a <c>WindowId</c> directly in its
/// constructor (see <c>MainWindowState</c>) instead of relying on whatever window-resolution
/// mechanism MAUI's wrapper uses internally, and is documented to keep working in cases (like
/// elevation) the older API doesn't. Android is unaffected by any of this — it keeps using MAUI's
/// cross-platform picker, which has no equivalent report against it. <see cref="FileSaveStore"/>'s
/// own Windows-side export fix reuses the same <c>MainWindowState</c> WindowId with the sibling
/// <c>FileSavePicker</c> API, for the same reason.
/// </remarks>
public sealed class MauiNativeFilePicker : INativeFilePicker
{
#if !WINDOWS
    private static readonly FilePickerFileType ModulePackageType = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.Android] = ["application/zip", "application/octet-stream"],
    });

    private static readonly FilePickerFileType SaveFileType = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        // .mwsave has no registered MIME type on Android — application/octet-stream is the catch-all
        // that still lets a real .mwsave file be selected despite that.
        [DevicePlatform.Android] = ["application/octet-stream"],
    });
#endif

    /// <inheritdoc/>
    public bool IsAvailable => true;

    /// <inheritdoc/>
    public async Task<byte[]?> PickAsync(NativeFileKind kind)
    {
#if WINDOWS
        var windowId = Masterwork.App.Platforms.Windows.MainWindowState.WindowId
            ?? throw new InvalidOperationException("MainWindowState.Initialize hasn't run yet — the app window isn't ready.");

        var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(windowId);
        picker.FileTypeFilter.Add(kind == NativeFileKind.ModulePackage ? ".mwm" : ".mwsave");

        var result = await picker.PickSingleFileAsync();
        if (result is null)
        {
            return null;
        }

        return await File.ReadAllBytesAsync(result.Path);
#else
        var (title, fileType) = kind == NativeFileKind.ModulePackage
            ? (AppStrings.StartNewGame_UploadModule, ModulePackageType)
            : (AppStrings.ContinueList_ImportSave, SaveFileType);

        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = title,
            FileTypes = fileType,
        });
        if (result is null)
        {
            return null;
        }

        await using var stream = await result.OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
#endif
    }
}
