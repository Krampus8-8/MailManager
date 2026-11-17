using System.IO;
using System.IO.Compression;

namespace MailManager.App.Services;

internal static class GmailExtensionArchive
{
    private const string ResourcePrefix = "MailManager.GmailExtension.";

    internal static void Save(string destination)
    {
        var assembly = typeof(GmailExtensionArchive).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .ToArray();
        if (!resources.Contains(ResourcePrefix + "manifest.json", StringComparer.Ordinal))
            throw new InvalidOperationException("The Gmail extension manifest is missing.");

        var folder = Path.GetDirectoryName(destination)!;
        var staging = Path.Combine(folder, $".gmail-extension-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var archive = ZipFile.Open(staging, ZipArchiveMode.Create))
            {
                foreach (var resource in resources)
                {
                    using var source = assembly.GetManifestResourceStream(resource)
                        ?? throw new InvalidOperationException("An embedded Gmail extension file is missing.");
                    using var entry = archive.CreateEntry("GmailExtension/" + resource[ResourcePrefix.Length..]).Open();
                    source.CopyTo(entry);
                }
            }

            File.Move(staging, destination, overwrite: true);
        }
        finally
        {
            File.Delete(staging);
        }
    }
}
