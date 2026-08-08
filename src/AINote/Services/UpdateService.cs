using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Nodes;

namespace AINote.Services;

public sealed class UpdateAsset
{
    public required string Name { get; init; }
    public required string Url { get; init; }
    public long Size { get; init; }
}

public sealed class UpdateInfo
{
    public required string Tag { get; init; }
    public required string HtmlUrl { get; init; }
    public string? Body { get; init; }
    public List<UpdateAsset> Assets { get; init; } = new();
    public bool HasUpdate { get; init; }
}

public static class UpdateService
{
    public const string Repo = "JiafuYuan/AINote";

    public static string CurrentVersionText { get; } = ReadCurrentVersionText();
    public static Version? CurrentVersion { get; } = ParseVersion(CurrentVersionText);

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AINote-Updater/1.0");
        client.Timeout = TimeSpan.FromMinutes(10);
        return client;
    }

    public static Version? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim().TrimStart('v', 'V');
        var parts = text.Split('.');
        while (parts.Length < 3)
            parts = parts.Append("0").ToArray();
        if (parts.Length > 3)
            parts = parts.Take(3).ToArray();

        return Version.TryParse(string.Join('.', parts), out var version) ? version : null;
    }

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Http.GetAsync(
                $"https://api.github.com/repos/{Repo}/releases/latest",
                cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var root = JsonNode.Parse(json);
            var tag = root?["tag_name"]?.GetValue<string>() ?? "";
            var htmlUrl = root?["html_url"]?.GetValue<string>() ?? "";
            if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(htmlUrl))
                return null;

            var assets = new List<UpdateAsset>();
            if (root?["assets"] is JsonArray array)
            {
                foreach (var item in array)
                {
                    if (item is null)
                        continue;

                    assets.Add(new UpdateAsset
                    {
                        Name = item["name"]?.GetValue<string>() ?? "",
                        Url = item["browser_download_url"]?.GetValue<string>() ?? "",
                        Size = item["size"]?.GetValue<long>() ?? 0
                    });
                }
            }

            var latest = ParseVersion(tag);
            return new UpdateInfo
            {
                Tag = tag,
                HtmlUrl = htmlUrl,
                Body = root?["body"]?.GetValue<string>(),
                Assets = assets,
                HasUpdate = latest != null && CurrentVersion != null && latest > CurrentVersion
            };
        }
        catch
        {
            return null;
        }
    }

    public static UpdateAsset? FindAsset(UpdateInfo info, string prefix, string suffix)
        => info.Assets.FirstOrDefault(asset =>
            asset.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            asset.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    public static async Task<bool> DownloadAndInstallAsync(
        UpdateInfo info,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
#if ANDROID
        return await UpdateAndroidAsync(info, progress, cancellationToken);
#else
        return await UpdateWindowsAsync(info, progress, cancellationToken);
#endif
    }

    public static void OpenReleasePage(string url)
    {
#if ANDROID
        var context = Android.App.Application.Context;
        var intent = new Android.Content.Intent(
            Android.Content.Intent.ActionView,
            Android.Net.Uri.Parse(url));
        intent.AddFlags(Android.Content.ActivityFlags.NewTask);
        context.StartActivity(intent);
#else
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        };
        System.Diagnostics.Process.Start(startInfo);
#endif
    }

#if !ANDROID
    private static async Task<bool> UpdateWindowsAsync(
        UpdateInfo info,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var asset = FindAsset(info, "AINote", "-win-x64.zip");
        if (asset is null)
            return false;

        var cache = Path.Combine(Path.GetTempPath(), "AINote-update");
        Directory.CreateDirectory(cache);
        var zipPath = Path.Combine(cache, asset.Name);
        var extractDir = Path.Combine(cache, "update_" + Guid.NewGuid().ToString("N"));
        await DownloadFileAsync(asset.Url, zipPath, progress, cancellationToken);

        ZipFile.ExtractToDirectory(zipPath, extractDir);
        var appDir = AppContext.BaseDirectory.TrimEnd('\\');
        var batPath = Path.Combine(cache, "update_app.bat");
        var script = "@echo off\r\n"
            + "timeout /t 3 /nobreak >nul\r\n"
            + $"robocopy \"{extractDir}\" \"{appDir}\" /E /IS /IT /NFL /NDL /NJH /NJS >nul\r\n"
            + $"rd /s /q \"{extractDir}\" >nul 2>nul\r\n"
            + $"del /q \"{zipPath}\" >nul 2>nul\r\n"
            + $"start \"\" \"{Path.Combine(appDir, "AINote.exe")}\"\r\n"
            + "exit\r\n";
        await File.WriteAllTextAsync(batPath, script, cancellationToken);

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{batPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
        };
        System.Diagnostics.Process.Start(psi);
        Environment.Exit(0);
        return true;
    }
#else
    private static async Task<bool> UpdateAndroidAsync(
        UpdateInfo info,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var asset = FindAsset(info, "AINote-Android", ".apk");
        if (asset is null)
            return false;

        var cacheDir = Android.App.Application.Context.CacheDir!.AbsolutePath;
        var apkPath = Path.Combine(cacheDir, asset.Name);
        await DownloadFileAsync(asset.Url, apkPath, progress, cancellationToken);

        var context = Android.App.Application.Context;
        var file = new Java.IO.File(apkPath);
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
            context,
            context.PackageName + ".fileprovider",
            file);
        var intent = new Android.Content.Intent(Android.Content.Intent.ActionView);
        intent.SetDataAndType(uri, "application/vnd.android.package-archive");
        intent.AddFlags(Android.Content.ActivityFlags.GrantReadUriPermission);
        intent.AddFlags(Android.Content.ActivityFlags.NewTask);
        try
        {
            context.StartActivity(intent);
        }
        catch (Android.Content.ActivityNotFoundException)
        {
            throw new InvalidOperationException("请先允许安装未知来源应用");
        }
        context.StartActivity(intent);
        return true;
    }
#endif

    private static async Task DownloadFileAsync(
        string url,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(destinationPath);
        var buffer = new byte[81920];
        long read = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            read += count;
            if (progress is not null && total > 0)
                progress.Report(Math.Min(1d, (double)read / total));
        }
    }

    private static string ReadCurrentVersionText()
    {
        var attribute = typeof(UpdateService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var raw = attribute?.InformationalVersion
            ?? typeof(UpdateService).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";
        return raw.Split('+')[0].Trim().TrimStart('v', 'V');
    }
}
