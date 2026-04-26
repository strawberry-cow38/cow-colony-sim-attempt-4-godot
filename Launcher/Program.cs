using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace CowColonySim.Launcher;

internal static class Program
{
    private const string Repo = "strawberry-cow38/cow-colony-sim-attempt-4-godot";
    private const string ReleaseTag = "latest";
    private const string GameDirName = "game";
    private const string VersionFileName = "version.txt";
    private const string GameExeName = "CowColonySim.exe";
    private const string ManifestAssetName = "manifest.json";
    private const string ZipAssetSuffix = "-windows.zip";
    private const int ParallelDownloads = 6;

    private static async Task<int> Main()
    {
        Console.Title = "Cow Colony Sim — Launcher";
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var gameDir = Path.Combine(baseDir, GameDirName);
            var versionPath = Path.Combine(baseDir, VersionFileName);

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CowColonyLauncher/1.1");
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
            var assets = BuildAssetMap(doc.RootElement);

            if (!assets.TryGetValue(ManifestAssetName, out var manifestAsset))
            {
                Console.WriteLine("No manifest.json on release — falling back to full zip.");
                return await FullZipPath(http, assets, baseDir, gameDir, versionPath);
            }

            // Use manifest.updated_at|size as the short-circuit version key.
            var remoteVersion = $"manifest:{manifestAsset.UpdatedAt}|{manifestAsset.Size}";
            var localVersion = File.Exists(versionPath) ? File.ReadAllText(versionPath).Trim() : "";
            var hasGame = Directory.Exists(gameDir) &&
                          Directory.EnumerateFiles(gameDir, GameExeName, SearchOption.AllDirectories).Any();

            if (localVersion == remoteVersion && hasGame)
            {
                Console.WriteLine($"Up to date: {remoteVersion}");
                return Launch(gameDir);
            }

            Console.WriteLine($"Update available.");
            Console.WriteLine($"  remote: {remoteVersion}");
            Console.WriteLine($"  local:  {(string.IsNullOrEmpty(localVersion) ? "(none)" : localVersion)}");

            // First install (no game files yet) → bulk zip is the fastest path.
            if (!hasGame)
            {
                if (!await TryFullZipUpdate(http, assets, baseDir, gameDir))
                {
                    return Bail(1);
                }
                File.WriteAllText(versionPath, remoteVersion);
                return Launch(gameDir);
            }

            // Differential update via manifest.
            Console.WriteLine("Fetching manifest ...");
            var manifest = await FetchManifest(http, manifestAsset.Url);
            if (manifest is null)
            {
                Console.Error.WriteLine("Failed to read manifest. Falling back to full zip.");
                if (!await TryFullZipUpdate(http, assets, baseDir, gameDir))
                {
                    return Bail(1);
                }
                File.WriteAllText(versionPath, remoteVersion);
                return Launch(gameDir);
            }

            await ApplyManifest(http, assets, manifest, gameDir);
            File.WriteAllText(versionPath, remoteVersion);
            return Launch(gameDir);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Launcher error:");
            Console.Error.WriteLine(ex);
            return Bail(1);
        }
    }

    private static int Launch(string gameDir)
    {
        var exePath = Directory
            .EnumerateFiles(gameDir, GameExeName, SearchOption.AllDirectories)
            .FirstOrDefault();
        if (exePath is null)
        {
            Console.Error.WriteLine($"{GameExeName} not found after update.");
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

    private record AssetInfo(string Url, string UpdatedAt, long Size);

    private static Dictionary<string, AssetInfo> BuildAssetMap(JsonElement root)
    {
        var map = new Dictionary<string, AssetInfo>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("assets", out var assets)) return map;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            if (name is null) continue;
            var url = asset.GetProperty("browser_download_url").GetString() ?? "";
            var updatedAt = asset.TryGetProperty("updated_at", out var u) ? u.GetString() ?? "" : "";
            var size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
            map[name] = new AssetInfo(url, updatedAt, size);
        }
        return map;
    }

    private record Manifest(string Version, string BlobPrefix, string BlobSuffix, IReadOnlyList<ManifestEntry> Files);
    private record ManifestEntry(string Path, long Size, string Sha256);

    private static async Task<Manifest?> FetchManifest(HttpClient http, string url)
    {
        using var resp = await http.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var entries = new List<ManifestEntry>();
        foreach (var f in root.GetProperty("files").EnumerateArray())
        {
            entries.Add(new ManifestEntry(
                f.GetProperty("path").GetString() ?? "",
                f.GetProperty("size").GetInt64(),
                (f.GetProperty("sha256").GetString() ?? "").ToLowerInvariant()));
        }
        return new Manifest(
            root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "",
            root.TryGetProperty("blob_prefix", out var bp) ? bp.GetString() ?? "blob-" : "blob-",
            root.TryGetProperty("blob_suffix", out var bs) ? bs.GetString() ?? ".bin" : ".bin",
            entries);
    }

    private static async Task ApplyManifest(
        HttpClient http, Dictionary<string, AssetInfo> assets, Manifest manifest, string gameDir)
    {
        Console.WriteLine($"Manifest: {manifest.Files.Count} files. Hashing local copy ...");
        var manifestPaths = new HashSet<string>(
            manifest.Files.Select(f => NormalizePath(f.Path)),
            StringComparer.OrdinalIgnoreCase);

        var diffs = new List<ManifestEntry>();
        long bytesNeeded = 0;
        foreach (var entry in manifest.Files)
        {
            var localPath = Path.Combine(gameDir, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(localPath) && new FileInfo(localPath).Length == entry.Size)
            {
                var localHash = await Sha256Async(localPath);
                if (string.Equals(localHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }
            diffs.Add(entry);
            bytesNeeded += entry.Size;
        }

        Console.WriteLine($"  unchanged: {manifest.Files.Count - diffs.Count}, to fetch: {diffs.Count} ({bytesNeeded / 1024.0 / 1024.0:F1} MiB)");

        if (diffs.Count > 0)
        {
            await DownloadDiffs(http, assets, manifest, diffs, gameDir);
        }

        // Remove files that no longer appear in the manifest.
        if (Directory.Exists(gameDir))
        {
            foreach (var existing in Directory.EnumerateFiles(gameDir, "*", SearchOption.AllDirectories))
            {
                var rel = NormalizePath(Path.GetRelativePath(gameDir, existing));
                if (!manifestPaths.Contains(rel))
                {
                    try { File.Delete(existing); } catch { }
                }
            }
        }

        Console.WriteLine("Update complete.");
    }

    private static async Task DownloadDiffs(
        HttpClient http, Dictionary<string, AssetInfo> assets, Manifest manifest,
        List<ManifestEntry> diffs, string gameDir)
    {
        // Group by sha so blobs shared by multiple paths download once.
        var byHash = diffs.GroupBy(d => d.Sha256).ToList();

        var sem = new SemaphoreSlim(ParallelDownloads);
        var done = 0;
        var total = byHash.Count;
        var lockObj = new object();

        var tasks = byHash.Select(async group =>
        {
            await sem.WaitAsync();
            try
            {
                var hash = group.Key;
                var blobName = $"{manifest.BlobPrefix}{hash}{manifest.BlobSuffix}";
                if (!assets.TryGetValue(blobName, out var asset))
                {
                    throw new IOException($"Blob asset missing on release: {blobName}");
                }
                var firstPath = Path.Combine(gameDir, group.First().Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(firstPath)!);
                await DownloadFile(http, asset.Url, firstPath, asset.Size);

                // Copy to any other paths sharing this hash.
                foreach (var dup in group.Skip(1))
                {
                    var dst = Path.Combine(gameDir, dup.Path.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    File.Copy(firstPath, dst, overwrite: true);
                }

                lock (lockObj)
                {
                    done++;
                    Console.Write($"\r  {done}/{total} blobs   ");
                }
            }
            finally
            {
                sem.Release();
            }
        });

        await Task.WhenAll(tasks);
        Console.WriteLine();
    }

    private static async Task DownloadFile(HttpClient http, string url, string dst, long expectedSize)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                if (File.Exists(dst)) File.Delete(dst);
                using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync();
                await using var fs = File.Create(dst);
                await src.CopyToAsync(fs);
                if (expectedSize > 0 && new FileInfo(dst).Length != expectedSize)
                {
                    throw new IOException($"Downloaded {new FileInfo(dst).Length} bytes but expected {expectedSize}.");
                }
                return;
            }
            catch (Exception ex) when (ex is HttpIOException or IOException or HttpRequestException or TaskCanceledException)
            {
                last = ex;
                if (attempt < 4)
                {
                    await Task.Delay(800 * attempt);
                }
            }
        }
        throw new IOException($"Download failed: {url}", last);
    }

    private static async Task<string> Sha256Async(string path)
    {
        await using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizePath(string p) => p.Replace('\\', '/');

    private static async Task<bool> TryFullZipUpdate(
        HttpClient http, Dictionary<string, AssetInfo> assets, string baseDir, string gameDir)
    {
        var zipAsset = assets
            .Where(kv => kv.Key.EndsWith(ZipAssetSuffix, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Value)
            .FirstOrDefault();
        if (zipAsset is null)
        {
            Console.Error.WriteLine("No Windows zip asset on the 'latest' release yet.");
            return false;
        }
        var tmpZip = Path.Combine(baseDir, "update.zip");
        Console.WriteLine($"Downloading zip ({zipAsset.Size / 1024.0 / 1024.0:F1} MiB) ...");
        await DownloadFile(http, zipAsset.Url, tmpZip, zipAsset.Size);
        Console.WriteLine();
        Console.WriteLine("Extracting...");
        if (Directory.Exists(gameDir)) Directory.Delete(gameDir, recursive: true);
        Directory.CreateDirectory(gameDir);
        ZipFile.ExtractToDirectory(tmpZip, gameDir, overwriteFiles: true);
        File.Delete(tmpZip);
        Console.WriteLine("Update complete.");
        return true;
    }

    private static async Task<int> FullZipPath(
        HttpClient http, Dictionary<string, AssetInfo> assets,
        string baseDir, string gameDir, string versionPath)
    {
        var zipAsset = assets
            .Where(kv => kv.Key.EndsWith(ZipAssetSuffix, StringComparison.OrdinalIgnoreCase))
            .Select(kv => (KeyValuePair<string, AssetInfo>?)kv)
            .FirstOrDefault();
        if (zipAsset is null)
        {
            Console.Error.WriteLine("No Windows zip asset on the 'latest' release yet.");
            return Bail(1);
        }
        var info = zipAsset.Value.Value;
        var remoteVersion = $"zip:{info.UpdatedAt}|{info.Size}";
        var localVersion = File.Exists(versionPath) ? File.ReadAllText(versionPath).Trim() : "";
        var hasGame = Directory.Exists(gameDir) &&
                      Directory.EnumerateFiles(gameDir, GameExeName, SearchOption.AllDirectories).Any();
        if (localVersion != remoteVersion || !hasGame)
        {
            Console.WriteLine("Update available (zip).");
            if (!await TryFullZipUpdate(http, assets, baseDir, gameDir)) return Bail(1);
            File.WriteAllText(versionPath, remoteVersion);
        }
        else
        {
            Console.WriteLine($"Up to date: {remoteVersion}");
        }
        return Launch(gameDir);
    }

    private static int Bail(int code)
    {
        Console.WriteLine();
        Console.WriteLine("Press any key to close...");
        try { Console.ReadKey(intercept: true); } catch { }
        return code;
    }
}
