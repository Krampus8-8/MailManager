using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using MailManager.Core.Services;

namespace MailManager.App.Services;

internal sealed record UpdateRelease(Version Version, long AssetId, long Size, byte[] Digest);

internal sealed class GitHubUpdates
{
    private const string Repository = "https://api.github.com/repos/Krampus8-8/MailManager";
    private const string Account = "Krampus8-8";
    private const long MaximumDownload = 200 * 1024 * 1024;
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });

    internal async Task<string?> GetTokenAsync(bool interactive)
    {
        var manager = new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(folder => Path.Combine(Environment.GetFolderPath(folder), "Git", "mingw64", "bin", "git-credential-manager.exe"))
            .Where(Path.IsPathFullyQualified)
            .FirstOrDefault(File.Exists);
        if (manager is null)
            throw new InputException("Для доступа к приватным обновлениям установите Git for Windows с Git Credential Manager.");

        var start = new ProcessStartInfo(manager)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(manager)!
        };
        start.ArgumentList.Add("get");
        start.Environment["GCM_INTERACTIVE"] = interactive ? "true" : "false";
        start.Environment["GCM_GITHUB_AUTHMODES"] = "browser";
        start.Environment["GCM_GUI_PROMPT"] = "1";
        start.Environment["GCM_TRACE"] = "0";
        start.Environment["GCM_TRACE_SECRETS"] = "0";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var process = StartCredentialManager(start);
        using var limit = new CancellationTokenSource(interactive ? TimeSpan.FromMinutes(3) : TimeSpan.FromSeconds(12));
        var output = process.StandardOutput.ReadToEndAsync(limit.Token);
        var errors = process.StandardError.ReadToEndAsync(limit.Token);
        try
        {
            await process.StandardInput.WriteAsync($"protocol=https\nhost=github.com\nusername={Account}\n\n");
            process.StandardInput.Close();
            await Task.WhenAll(output, errors, process.WaitForExitAsync(limit.Token));
            if (process.ExitCode != 0)
                return null;

            return output.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(line => line.StartsWith("password=", StringComparison.Ordinal))?[9..].Trim();
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
        }
    }

    private static Process StartCredentialManager(ProcessStartInfo start)
    {
        try
        {
            return Process.Start(start) ?? throw new InputException("Не удалось запустить Git Credential Manager.");
        }
        catch (Win32Exception)
        {
            throw new InputException("Windows не смогла запустить Git Credential Manager.");
        }
    }

    internal async Task<UpdateRelease?> CheckAsync(string token, Version currentVersion, CancellationToken cancellationToken)
    {
        using var response = await GetAsync(new Uri(Repository + "/releases/latest"), "application/vnd.github+json", token, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            throw new InputException($"GitHub не открыл приватный релиз. Проверьте, что вход Git for Windows выполнен под аккаунтом с доступом к {Account}/MailManager.");
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var release = document.RootElement;
        var tag = release.GetProperty("tag_name").GetString();
        if (tag is null || !tag.StartsWith('v') || !Version.TryParse(tag[1..], out var version) || version.Build < 0 || version.Revision != -1)
            throw new InputException("GitHub вернул релиз с непонятным номером версии.");
        if (version <= currentVersion)
            return null;

        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != "MailManager.exe")
                continue;

            var hash = asset.TryGetProperty("digest", out var digest) ? digest.GetString() : null;
            if (asset.GetProperty("state").GetString() == "uploaded" &&
                asset.GetProperty("id").TryGetInt64(out var id) && id > 0 &&
                asset.GetProperty("size").TryGetInt64(out var size) && size is > 0 and <= MaximumDownload &&
                hash is { Length: 71 } && hash.StartsWith("sha256:", StringComparison.Ordinal) &&
                hash[7..].All(Uri.IsHexDigit))
            {
                return new(version, id, size, Convert.FromHexString(hash[7..]));
            }

            break;
        }

        throw new InputException("Релиз GitHub ещё не содержит проверяемую сборку MailManager.exe.");
    }

    internal async Task DownloadAsync(UpdateRelease release, string token, string destination, Action<int> report, CancellationToken cancellationToken)
    {
        using var api = await GetAsync(new Uri($"{Repository}/releases/assets/{release.AssetId}"), "application/octet-stream", token, cancellationToken);
        if (api.StatusCode == HttpStatusCode.Found)
        {
            var location = api.Headers.Location;
            if (location is not { IsAbsoluteUri: true, Scheme: "https", IsDefaultPort: true } || location.UserInfo.Length != 0 ||
                location.Host is not ("release-assets.githubusercontent.com" or "objects.githubusercontent.com" or "github-releases.githubusercontent.com"))
                throw new InputException("GitHub перенаправил скачивание на неизвестный сервер.");

            // Ссылка на файл уже содержит разрешение GitHub. Токен остаётся на api.github.com.
            using var storage = await GetAsync(location, "application/octet-stream", null, cancellationToken);
            await SaveVerifiedAsync(storage, release, destination, report, cancellationToken);
            return;
        }

        await SaveVerifiedAsync(api, release, destination, report, cancellationToken);
    }

    private static async Task<HttpResponseMessage> GetAsync(Uri uri, string accept, string? token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("MailManager-Updater");
        request.Headers.Accept.ParseAdd(accept);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        }

        return await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static async Task SaveVerifiedAsync(HttpResponseMessage response, UpdateRelease release, string destination, Action<int> report,
        CancellationToken cancellationToken)
    {
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, useAsync: true);
        using var checksum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[131072];
        var total = 0L;
        var previous = -1;
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += count;
            if (total > release.Size)
                throw new InputException("Размер скачанной сборки не совпал с данными GitHub.");
            checksum.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            var percent = (int)(total * 100 / release.Size);
            if (percent != previous)
            {
                report(percent);
                previous = percent;
            }
        }

        if (total != release.Size || !checksum.GetHashAndReset().AsSpan().SequenceEqual(release.Digest))
            throw new InputException("Проверка скачанной сборки не прошла. Попробуйте обновиться ещё раз.");
    }
}
