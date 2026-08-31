namespace Masterwork.App.Shared.Services;

/// <summary>
/// How a locally-installed asset pack got there — governs its delete lifecycle.
/// <see cref="Auto"/>-installed packs are removed automatically once no installed module still
/// depends on them; <see cref="Manual"/> ones survive that cleanup and need an explicit delete. A
/// manual install of an id+version that already exists as <see cref="Auto"/> promotes it to
/// <see cref="Manual"/> rather than creating a second record — there is only ever one installed
/// copy per exact id+version.
/// </summary>
public enum AssetPackInstallSource
{
    Auto,
    Manual,
}
