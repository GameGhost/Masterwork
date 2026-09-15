using System.Security.Cryptography.X509Certificates;
using Masterwork.ModuleFormat;

void PrintUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  Masterwork.ModulePacker asset <asset-pack-directory> <output.mwassets>");
    Console.Error.WriteLine("  Masterwork.ModulePacker module <module-directory> <output.mwm>");
    Console.Error.WriteLine("  Masterwork.ModulePacker standalone <module-directory> <output.mwm> <asset-pack-directory> [asset-pack-directory ...]");
    Console.Error.WriteLine("  Masterwork.ModulePacker gencert <common-name> <output.pfx> <password> [validity-years, default 1]");
    Console.Error.WriteLine("      <common-name> is a bare name (\"Masterwork Content Signing\"), not a \"CN=...\" string.");
    Console.Error.WriteLine("  Masterwork.ModulePacker sign <package-file> <pfx-file> <pfx-password> [output-file, default overwrites package-file]");
    Console.Error.WriteLine("  Masterwork.ModulePacker verify <package-file>");
}

if (args.Length < 1)
{
    PrintUsage();
    return 1;
}

var mode = args[0];

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

byte[] bytes;
switch (mode)
{
    case "asset":
        bytes = AssetPackPackage.WriteToBytes(sourceDir);
        break;

    case "module":
        bytes = ModulePackage.WriteToBytes(sourceDir);
        break;

    case "standalone":
        var assetPackDirs = args[3..];
        if (assetPackDirs.Length == 0)
        {
            Console.Error.WriteLine("standalone mode needs at least one asset-pack directory.");
            return 1;
        }

        foreach (var dir in assetPackDirs)
        {
            if (!Directory.Exists(dir))
            {
                Console.Error.WriteLine($"Asset pack directory not found: {dir}");
                return 1;
            }
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
