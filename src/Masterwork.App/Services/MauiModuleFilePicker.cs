using Masterwork.App.Shared.Resources;
using Masterwork.App.Shared.Services;

namespace Masterwork.App.Services;

/// <inheritdoc cref="IModuleFilePicker"/>
/// <remarks>
/// MAUI-only — lives in the MAUI head rather than Shared because <see cref="Microsoft.Maui.Storage.FilePicker"/>
/// isn't available to a plain Razor Class Library. Picks entirely outside the <c>BlazorWebView</c>'s
/// own WebView2 (Windows) / native WebView (Android) host, which is the whole point — see
/// <see cref="IModuleFilePicker"/>'s own remarks for why that matters.
/// </remarks>
public sealed class MauiModuleFilePicker : IModuleFilePicker
{
    private static readonly FilePickerFileType ModulePackageType = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.WinUI] = [".mwm"],
        [DevicePlatform.Android] = ["application/zip", "application/octet-stream"],
    });

    /// <inheritdoc/>
    public bool IsAvailable => true;

    /// <inheritdoc/>
    public async Task<byte[]?> PickAsync()
    {
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = AppStrings.StartNewGame_UploadModule,
            FileTypes = ModulePackageType,
        });
        if (result is null)
        {
            return null;
        }

        await using var stream = await result.OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }
}
