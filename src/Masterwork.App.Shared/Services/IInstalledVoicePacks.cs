using Masterwork.ModuleFormat;

namespace Masterwork.App.Shared.Services;

/// <summary>An installed voice pack: its manifest and its files.</summary>
public sealed record InstalledVoicePack(VoicePackManifest Manifest, IModuleAssetSource Assets);

/// <summary>The voice packs installed on this device, as playback reads them.</summary>
public interface IInstalledVoicePacks
{
    /// <summary>Every installed pack whose <c>for:</c> names <paramref name="moduleId"/>, whatever its minimum version.</summary>
    Task<IReadOnlyList<InstalledVoicePack>> ListForModuleAsync(string moduleId);

    /// <summary>Every installed pack.</summary>
    Task<IReadOnlyList<InstalledVoicePack>> ListAsync();
}

/// <summary>The <see cref="IInstalledVoicePacks"/> for a host that can't install voice packs yet: none are ever installed.</summary>
public sealed class NoInstalledVoicePacks : IInstalledVoicePacks
{
    /// <inheritdoc/>
    public Task<IReadOnlyList<InstalledVoicePack>> ListForModuleAsync(string moduleId) =>
        Task.FromResult<IReadOnlyList<InstalledVoicePack>>([]);

    /// <inheritdoc/>
    public Task<IReadOnlyList<InstalledVoicePack>> ListAsync() =>
        Task.FromResult<IReadOnlyList<InstalledVoicePack>>([]);
}
