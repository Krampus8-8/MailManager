using System.Security.Cryptography;
using MailManager.App.Services;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: UpdateDelta <target EXE> <output folder> <source EXE> [<source EXE> ...]");
    return 1;
}

var target = File.ReadAllBytes(args[0]);
Directory.CreateDirectory(args[1]);
foreach (var path in args[2..])
{
    var source = File.ReadAllBytes(path);
    var patch = BinaryDelta.Create(source, target);
    if (patch.Length >= target.Length)
    {
        Console.WriteLine($"Skipped {path}: patch is not smaller than the full EXE.");
        continue;
    }

    var reconstructed = BinaryDelta.Apply(source, patch, target.Length);
    if (!reconstructed.AsSpan().SequenceEqual(target))
        throw new InvalidDataException("Applying the delta did not reproduce the release EXE.");

    var name = BinaryDelta.AssetName(SHA256.HashData(source));
    File.WriteAllBytes(Path.Combine(args[1], name), patch);
    Console.WriteLine($"{name} {patch.Length} bytes (from {path})");
}

return 0;
