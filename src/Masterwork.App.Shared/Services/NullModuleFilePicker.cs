namespace Masterwork.App.Shared.Services;

/// <inheritdoc cref="IModuleFilePicker"/>
/// <remarks>
/// Default registration for the Web/WASM heads — <c>StartNewGame.razor</c> keeps using its existing
/// <c>InputFile</c> element there, since the WebView2 bug <see cref="IModuleFilePicker"/> documents
/// doesn't apply outside a WebView2 host. <see cref="PickAsync"/> is never expected to be called
/// (guarded by <see cref="IsAvailable"/>); it throws rather than silently returning null so a future
/// call site that forgets the guard fails loudly instead of masking a real bug.
/// </remarks>
public sealed class NullModuleFilePicker : IModuleFilePicker
{
    /// <inheritdoc/>
    public bool IsAvailable => false;

    /// <inheritdoc/>
    public Task<byte[]?> PickAsync() =>
        throw new NotSupportedException($"{nameof(NullModuleFilePicker)} does not support picking — check {nameof(IsAvailable)} first.");
}
