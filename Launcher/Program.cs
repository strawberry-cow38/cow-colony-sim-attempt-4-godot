using System.IO.Compression;
using System.Text.Json;

namespace CowColonySim.Launcher;

internal static class Program
{
    private const string Repo = "strawberry-cow38/cow-colony-sim-attempt-4-godot";
    private const string ReleaseTag = "latest";
    private const string GameDirName = "game";
    private const string VersionFileName = "version.txt";
    private const string GameExeName = "CowColonySim.exe";

    private static async Task<int> Main()
    {
        Console.Title = "Cow Colony Sim — Launcher";
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var gameDir = Path.Combine(baseDir, GameDirName);
            var versionPath = Path.Combine(baseDir, VersionFileName);

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CowColonyLauncher/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            Console.WriteLine($"Checking {Repo} @ tag '{ReleaseTag}' ...");
            var apiUrl = $"https://api.github.com/repos/{Repo}/releases/tags/{ReleaseTag}";
            using var resp = await http.GetAsync(apiUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"GitHub API returned {(int)resp.StatusCode} {resp.StatusCode}.");
                return Bail(1);
            }

            await using var apiStream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(apiStream);
            var root = doc.RootElement;

            string? zipUrl = null;
            string? zipName = null;
            string assetUpdatedAt = "";
            long assetSize = 0;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString();
                    if (name is null || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!name.Contains("windows", StringComparison.OrdinalIgnoreCase)) continue;
                    zipName = name;
                    zipUrl = asset.GetProperty("browser_download_url").GetString();
                    assetUpdatedAt = asset.TryGetProperty("updated_at", out var u) ? u.GetString() ?? "" : "";
                    assetSize = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                    break;
                }
            }
            // Use asset.updated_at + size as the version key. release.published_at
            // does NOT change when softprops/action-gh-release@v2 replaces assets
            // on an existing tag, so it can't be used to detect new builds.
            var remoteVersion = $"{assetUpdatedAt}|{assetSize}";
            if (zipUrl is null)
            {
                Console.Error.WriteLine("No Windows zip asset on the 'latest' release yet.");
                return Bail(1);
            }

            var localVersion = File.Exists(versionPath) ? File.ReadAllText(versionPath).Trim() : "";
            var hasGame = Directory.Exists(gameDir) &&
                          Directory.EnumerateFiles(gameDir, GameExeName, SearchOption.AllDirectories).Any();

            if (localVersion != remoteVersion || !hasGame)
            {
                Console.WriteLine($"Update available: {zipName}");
                Console.WriteLine($"  remote: {remoteVersion}");
                Console.WriteLine($"  local:  {(string.IsNullOrEmpty(localVersion) ? "(none)" : localVersion)}");

                var tmpZip = Path.Combine(baseDir, "update.zip");
                Console.WriteLine("Downloading...");
                await DownloadWithRetry(http, zipUrl, tmpZip, expectedSize: assetSize, maxAttempts: 4);
                Console.WriteLine();

                Console.WriteLine("Extracting...");
                if (Directory.Exists(gameDir)) Directory.Delete(gameDir, recursive: true);
                Directory.CreateDirectory(gameDir);
                ZipFile.ExtractToDirectory(tmpZip, gameDir, overwriteFiles: true);
                File.Delete(tmpZip);

                File.WriteAllText(versionPath, remoteVersion);
                Console.WriteLine("Update complete.");
            }
            else
            {
                Console.WriteLine($"Up to date: {remoteVersion}");
            }

            var exePath = Directory
                .EnumerateFiles(gameDir, GameExeName, SearchOption.AllDirectories)
                .FirstOrDefault();
            if (exePath is null)
            {
                Console.Error.WriteLine($"{GameExeName} not found after extract.");
                return Bail(1);
            }

            Console.WriteLine($"Launching: {exePath}");
            var psi = new System.Diagnostics.ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath)!,
            };
            System.Diagnostics.Process.Start(psi);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Launcher error:");
            Console.Error.WriteLine(ex);
            return Bail(1);
        }
    }

    private static int Bail(int code)
    {
        Console.WriteLine();
        Console.WriteLine("Press any key to close...");
        try { Console.ReadKey(intercept: true); } catch { }
        return code;
    }

    private static async Task DownloadWithRetry(HttpClient http, string url, string dst, long expectedSize, int maxAttempts)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (File.Exists(dst)) File.Delete(dst);
                using var dlResp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                dlResp.EnsureSuccessStatusCode();
                var total = dlResp.Content.Headers.ContentLength ?? expectedSize;
                await using (var src = await dlResp.Content.ReadAsStreamAsync())
                await using (var fs = File.Create(dst))
                {
                    await CopyWithProgress(src, fs, total);
                }
                var actual = new FileInfo(dst).Length;
                if (expectedSize > 0 && actual != expectedSize)
                {
                    throw new IOException($"Downloaded {actual} bytes but expected {expectedSize}.");
                }
                return;
            }
            catch (Exception ex) when (ex is HttpIOException or IOException or HttpRequestException or TaskCanceledException)
            {
                last = ex;
                Console.WriteLine();
                Console.WriteLine($"  attempt {attempt}/{maxAttempts} failed: {ex.GetType().Name}: {ex.Message}");
                if (attempt < maxAttempts)
                {
                    var waitMs = 1500 * attempt;
                    Console.WriteLine($"  retrying in {waitMs}ms...");
                    await Task.Delay(waitMs);
                }
            }
        }
        if (File.Exists(dst)) try { File.Delete(dst); } catch { }
        throw new IOException($"Download failed after {maxAttempts} attempts.", last);
    }

    private static async Task CopyWithProgress(Stream src, Stream dst, long total)
    {
        var buffer = new byte[81920];
        long copied = 0;
        var lastReport = DateTime.UtcNow;
        int read;
        while ((read = await src.ReadAsync(buffer)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read));
            copied += read;
            if ((DateTime.UtcNow - lastReport).TotalMilliseconds < 250) continue;
            lastReport = DateTime.UtcNow;
            if (total > 0)
            {
                var pct = (double)copied / total * 100.0;
                Console.Write($"\r  {copied / 1024.0 / 1024.0:F1} / {total / 1024.0 / 1024.0:F1} MiB ({pct:F0}%)   ");
            }
            else
            {
                Console.Write($"\r  {copied / 1024.0 / 1024.0:F1} MiB   ");
            }
        }
    }
}
