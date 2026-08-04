namespace Masterwork.App.Shared.Services;

/// <inheritdoc cref="INativeFilePicker"/>
/// <remarks>
/// Default registration for the Web/WASM heads — <c>StartNewGame.razor</c> and
/// <c>ContinueList.razor</c> keep using their existing <c>InputFile</c> elements there, since the
/// WebView2 bug <see cref="INativeFilePicker"/> documents doesn't apply outside a WebView2 host.
/// <see cref="PickAsync"/> is never expected to be called (guarded by <see cref="IsAvailable"/>); it
/// throws rather than silently returning null so a future call site that forgets the guard fails
/// loudly instead of masking a real bug.
/// </remarks>
public sealed class NullNativeFilePicker : INativeFilePicker
{
    /// <inheritdoc/>
    public bool IsAvailable => false;

    /// <inheritdoc/>
    public Task<byte[]?> PickAsync(NativeFileKind kind) =>
        throw new NotSupportedException($"{nameof(NullNativeFilePicker)} does not support picking — check {nameof(IsAvailable)} first.");
}
