namespace Masterwork.ModuleFormat;

/// <summary>
/// One dependency asset pack's restext contribution to <see cref="IModuleLoader.LoadFromSources"/>'s
/// per-key fallback merge — see that parameter's own remarks for the full tier ordering. A module
/// depending on more than one asset pack passes one of these per dependency, in the same order as
/// its manifest's own <c>dependencies:</c> list; later entries win over earlier ones on a key both
/// declare, but every dependency loses to the module's own restext regardless of order.
/// </summary>
/// <param name="RestextText">Raw restext text from this asset pack's own file for whichever locale the module resolved to — <see langword="null"/> if the asset pack doesn't ship that locale.</param>
/// <param name="DefaultRestextText">Raw restext text for this asset pack's own default locale (<c>AssetPackManifest.DefaultLocale</c>) — the pack's own final fallback tier.</param>
public sealed record DependencyRestext(string? RestextText, string? DefaultRestextText);
