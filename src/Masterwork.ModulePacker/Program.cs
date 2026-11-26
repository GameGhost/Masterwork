using System.Security.Cryptography.X509Certificates;
using Masterwork.ModuleFormat;
using Masterwork.ModulePacker.Translation;

void PrintUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  Masterwork.ModulePacker asset <asset-pack-directory> <output.mwassets>");
    Console.Error.WriteLine("  Masterwork.ModulePacker module <module-directory> <output.mwm> [asset-pack-directory ...]");
    Console.Error.WriteLine("      The asset-pack directories are read for the logic lint only; nothing from them is packed.");
    Console.Error.WriteLine("  Masterwork.ModulePacker standalone <module-directory> <output.mwm> <asset-pack-directory> [asset-pack-directory ...]");
    Console.Error.WriteLine("  Masterwork.ModulePacker voices <voice-pack-directory> <output.mwvoices> [--module <module-directory>]");
    Console.Error.WriteLine("      Warns about narration files nothing plays; with --module, also reports each track's coverage of that module.");
    Console.Error.WriteLine("  Masterwork.ModulePacker gencert <common-name> <output.pfx> <password> [validity-years, default 1]");
    Console.Error.WriteLine("      <common-name> is a bare name (\"Masterwork Content Signing\"), not a \"CN=...\" string.");
    Console.Error.WriteLine("  Masterwork.ModulePacker sign <package-file> <pfx-file> <pfx-password> [output-file, default overwrites package-file]");
    Console.Error.WriteLine("  Masterwork.ModulePacker verify <package-file>");
    Console.Error.WriteLine("  Masterwork.ModulePacker catalog <output.json> <source-title> <release-tag> <package-file> [package-file ...]");
    Console.Error.WriteLine("      Entry paths become <release-tag>/<file name>. Sign the result with 'signfile'.");
    Console.Error.WriteLine("  Masterwork.ModulePacker signfile <file> <pfx-file> <pfx-password>");
    Console.Error.WriteLine("      Writes a detached <file>.sig beside it.");
    Console.Error.WriteLine("  Masterwork.ModulePacker verifyfile <file>");
    Console.Error.WriteLine("      Checks <file> against its <file>.sig sibling.");
    Console.Error.WriteLine(TranslationCheckCommand.Usage);
}

void PrintVoiceWarnings(string directory, IReadOnlyList<string> warnings)
{
    foreach (var warning in warnings)
    {
        Console.Error.WriteLine($"Warning: {directory}: {warning}");
    }
}

if (args.Length < 1)
{
    PrintUsage();
    return 1;
}

var mode = args[0];

if (mode == "checktranslation")
{
    return TranslationCheckCommand.Run(args[1..], Console.Out, Console.Error);
}

if (mode == "gencert")
{
    if (args.Length < 4)
    {
        PrintUsage();
        return 1;
    }

    var subject = args[1];
    var pfxPath = args[2];
    var password = args[3];
    var years = args.Length > 4 ? int.Parse(args[4]) : 1;

    var cert = SelfSignedCertificateGenerator.Create(subject, years);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pfxPath))!);
    File.WriteAllBytes(pfxPath, cert.Export(X509ContentType.Pfx, password));

    Console.WriteLine($"Wrote {pfxPath}");
    Console.WriteLine($"Subject:    {cert.Subject}");
    Console.WriteLine($"Thumbprint: {cert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256)}");
    Console.WriteLine($"Valid:      {cert.NotBefore:u} to {cert.NotAfter:u}");
    Console.WriteLine();
    Console.WriteLine("Keep the .pfx and its password private — this is the one signing identity for all");
    Console.WriteLine("canonical content. The thumbprint above is what a white-label build pins as its trust anchor.");
    return 0;
}

if (mode == "sign")
{
    if (args.Length < 4)
    {
        PrintUsage();
        return 1;
    }

    var packagePath = args[1];
    var pfxPath = args[2];
    var password = args[3];
    var outputPath = args.Length > 4 ? args[4] : packagePath;

    if (!File.Exists(packagePath))
    {
        Console.Error.WriteLine($"Package file not found: {packagePath}");
        return 1;
    }
    if (!File.Exists(pfxPath))
    {
        Console.Error.WriteLine($"PFX file not found: {pfxPath}");
        return 1;
    }

    var cert = X509CertificateLoader.LoadPkcs12FromFile(pfxPath, password, X509KeyStorageFlags.Exportable);
    var packageBytes = File.ReadAllBytes(packagePath);
    var signedBytes = PackageSigner.Sign(packageBytes, cert);

    File.WriteAllBytes(outputPath, signedBytes);
    Console.WriteLine($"Signed {packagePath} -> {outputPath} ({signedBytes.Length:N0} bytes)");
    Console.WriteLine($"Signer: {cert.Subject}");
    return 0;
}

if (mode == "verify")
{
    if (args.Length < 2)
    {
        PrintUsage();
        return 1;
    }

    var packagePath = args[1];
    if (!File.Exists(packagePath))
    {
        Console.Error.WriteLine($"Package file not found: {packagePath}");
        return 1;
    }

    var result = PackageSigner.Verify(File.ReadAllBytes(packagePath));
    Console.WriteLine($"{packagePath}: {result.Outcome}");
    if (result.CertificateSubject is not null)
    {
        Console.WriteLine($"Signer:     {result.CertificateSubject}");
        Console.WriteLine($"Thumbprint: {result.CertificateThumbprint}");
    }

    // Unsigned isn't a failure -- it's the norm for anything packed without -SignWith, and the app
    // installs it behind a prompt. Only a signature that's present and doesn't check out is.
    return result.Outcome == SignatureVerificationOutcome.Invalid ? 1 : 0;
}

if (mode == "catalog")
{
    if (args.Length < 5)
    {
        PrintUsage();
        return 1;
    }

    var catalogPath = args[1];
    var sourceTitle = args[2];
    var releaseTag = args[3];
    var packageFiles = args[4..];

    var inputs = new List<CatalogBuilder.PackageInput>();
    foreach (var file in packageFiles)
    {
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"Package file not found: {file}");
            return 1;
        }

        // The release tag is the first path segment, so a catalog can reference packages from
        // several releases at once — only entries republished this cycle move to the new tag.
        inputs.Add(new CatalogBuilder.PackageInput($"{releaseTag}/{Path.GetFileName(file)}", File.ReadAllBytes(file)));
    }

    var built = CatalogBuilder.Build(sourceTitle, inputs);
    var catalogDirectory = Path.GetDirectoryName(Path.GetFullPath(catalogPath))!;
    Directory.CreateDirectory(catalogDirectory);
    File.WriteAllBytes(catalogPath, CatalogParser.Write(built.Catalog));

    // Thumbnails are published beside the catalog, not inside a release — an entry's thumbnail path
    // is relative to the catalog's own directory, so they must be committed along with it.
    foreach (var thumbnail in built.Thumbnails)
    {
        var thumbnailPath = Path.Combine(catalogDirectory, thumbnail.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(thumbnailPath)!);
        File.WriteAllBytes(thumbnailPath, thumbnail.Bytes);
    }

    Console.WriteLine($"Wrote {catalogPath} ({built.Catalog.Entries.Count} entries)");
    foreach (var entry in built.Catalog.Entries)
    {
        var art = entry.Thumbnail?.Path ?? "no thumbnail";
        var target = entry.For is { } voiced ? $"  for {voiced.ModuleId} v{voiced.MinVersion}+" : "";
        Console.WriteLine($"  {entry.Type,-6} {entry.Id} v{entry.Version}  {entry.Path}  ({entry.Size:N0} bytes)  [{art}]{target}");
    }

    if (built.Thumbnails.Count > 0)
    {
        var totalBytes = built.Thumbnails.Sum(t => t.Bytes.LongLength);
        Console.WriteLine($"Wrote {built.Thumbnails.Count} thumbnail(s) under {catalogDirectory} ({totalBytes:N0} bytes total)");
    }

    Console.WriteLine();
    Console.WriteLine("Not signed yet — run 'signfile' on it, and publish catalog.json.sig and the");
    Console.WriteLine("thumbnails/ folder alongside it.");
    return 0;
}

if (mode == "signfile")
{
    if (args.Length < 4)
    {
        PrintUsage();
        return 1;
    }

    var filePath = args[1];
    var pfx = args[2];
    var pfxPassword = args[3];

    if (!File.Exists(filePath))
    {
        Console.Error.WriteLine($"File not found: {filePath}");
        return 1;
    }
    if (!File.Exists(pfx))
    {
        Console.Error.WriteLine($"PFX file not found: {pfx}");
        return 1;
    }

    var signingCert = X509CertificateLoader.LoadPkcs12FromFile(pfx, pfxPassword, X509KeyStorageFlags.Exportable);
    var content = File.ReadAllBytes(filePath);
    var signaturePath = filePath + ".sig";
    File.WriteAllBytes(signaturePath, DetachedSignature.Sign(content, signingCert));

    Console.WriteLine($"Wrote {signaturePath}");
    Console.WriteLine($"Signer:     {signingCert.Subject}");
    Console.WriteLine($"Thumbprint: {signingCert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256)}");
    return 0;
}

if (mode == "verifyfile")
{
    if (args.Length < 2)
    {
        PrintUsage();
        return 1;
    }

    var filePath = args[1];
    if (!File.Exists(filePath))
    {
        Console.Error.WriteLine($"File not found: {filePath}");
        return 1;
    }

    var signaturePath = filePath + ".sig";
    var signature = File.Exists(signaturePath) ? File.ReadAllBytes(signaturePath) : null;
    var fileResult = DetachedSignature.Verify(File.ReadAllBytes(filePath), signature);

    Console.WriteLine($"{filePath}: {fileResult.Outcome}");
    if (fileResult.CertificateSubject is not null)
    {
        Console.WriteLine($"Signer:     {fileResult.CertificateSubject}");
        Console.WriteLine($"Thumbprint: {fileResult.CertificateThumbprint}");
    }

    return fileResult.Outcome == SignatureVerificationOutcome.Invalid ? 1 : 0;
}

if (args.Length < 3)
{
    PrintUsage();
    return 1;
}

var sourceDir = args[1];
var packOutputPath = args[2];

if (!Directory.Exists(sourceDir))
{
    Console.Error.WriteLine($"Directory not found: {sourceDir}");
    return 1;
}

var assetPackDirs = args[3..];
if (mode is "module" or "standalone")
{
    foreach (var dir in assetPackDirs)
    {
        if (!Directory.Exists(dir))
        {
            Console.Error.WriteLine($"Asset pack directory not found: {dir}");
            return 1;
        }
    }

    var findings = ModuleLogicLint.Check(sourceDir, assetPackDirs);
    if (findings.Count > 0)
    {
        Console.Error.WriteLine($"Not packed: {findings.Count} logic lint finding(s) in {sourceDir}.");
        Console.Error.WriteLine("Logic must compare restext-sourced values by key, not by their text in one language.");
        foreach (var finding in findings)
        {
            Console.Error.WriteLine($"  - {finding.Message}");
        }

        return 1;
    }

    var manifestPath = Path.Combine(sourceDir, "manifest.yaml");
    var builtInVoices = File.Exists(manifestPath) ? new ManifestParser().Parse(File.ReadAllText(manifestPath)).Voices : [];
    PrintVoiceWarnings(sourceDir, VoiceCoverage.Warnings(
        builtInVoices, VoiceCoverage.ListVoiceFiles(sourceDir), VoiceCoverage.CollectNarration(sourceDir)));
}

if (mode == "voices")
{
    string? moduleDir = null;
    if (assetPackDirs is ["--module", var path])
    {
        moduleDir = path;
    }
    else if (assetPackDirs.Length > 0)
    {
        PrintUsage();
        return 1;
    }

    if (moduleDir is not null && !Directory.Exists(moduleDir))
    {
        Console.Error.WriteLine($"Module directory not found: {moduleDir}");
        return 1;
    }

    var packManifestPath = Path.Combine(sourceDir, "manifest.yaml");
    if (!File.Exists(packManifestPath))
    {
        Console.Error.WriteLine($"No manifest.yaml in {sourceDir}.");
        return 1;
    }

    var packManifest = new VoicePackManifestParser().Parse(File.ReadAllText(packManifestPath));
    var packFiles = VoiceCoverage.ListVoiceFiles(sourceDir);
    if (moduleDir is null)
    {
        PrintVoiceWarnings(sourceDir, VoiceCoverage.Warnings(packManifest.Voices, packFiles, narration: null));
        Console.WriteLine("Pass --module <module-directory> to check this pack's files and coverage against its module.");
    }
    else
    {
        var moduleManifest = new ManifestParser().Parse(File.ReadAllText(Path.Combine(moduleDir, "manifest.yaml")));
        if (moduleManifest.Id != packManifest.For.ModuleId)
        {
            Console.Error.WriteLine($"Warning: this pack is for '{packManifest.For.ModuleId}', but {moduleDir} is '{moduleManifest.Id}'.");
        }

        var narration = VoiceCoverage.CollectNarration(moduleDir);
        PrintVoiceWarnings(sourceDir, VoiceCoverage.Warnings(packManifest.Voices, packFiles, narration));
        foreach (var coverage in VoiceCoverage.Coverage(packManifest.Voices, packFiles, narration))
        {
            Console.WriteLine($"Track {coverage.Track.Language}/{coverage.Track.Id}: {coverage.Summary}");
            foreach (var uncovered in coverage.Uncovered)
            {
                Console.WriteLine($"  - missing: {uncovered}");
            }
        }
    }
}

byte[] bytes;
switch (mode)
{
    case "asset":
    case "voices":
        bytes = AssetPackPackage.WriteToBytes(sourceDir);
        break;

    case "module":
        bytes = ModulePackage.WriteToBytes(sourceDir);
        break;

    case "standalone":
        if (assetPackDirs.Length == 0)
        {
            Console.Error.WriteLine("standalone mode needs at least one asset-pack directory.");
            return 1;
        }

        bytes = ModulePackage.WriteStandaloneToBytes(sourceDir, assetPackDirs);
        break;

    default:
        PrintUsage();
        return 1;
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(packOutputPath))!);
File.WriteAllBytes(packOutputPath, bytes);

Console.WriteLine($"Wrote {packOutputPath} ({bytes.Length:N0} bytes)");
return 0;
