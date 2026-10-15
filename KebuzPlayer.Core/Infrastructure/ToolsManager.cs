using System.IO.Compression;

namespace KebuzPlayer.Infrastructure;

public static class ToolsManager
{
    private static readonly string ToolsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KebuzPlayer", "tools");

    public static string FfmpegPath  => Path.Combine(ToolsDir, "ffmpeg.exe");
    public static string YtDlpPath   => Path.Combine(ToolsDir, "yt-dlp.exe");

    public static bool ToolsReady =>
        File.Exists(FfmpegPath) && File.Exists(YtDlpPath);

    public static async Task EnsureToolsAsync(IProgress<(string tool, int percent)>? progress = null)
    {
        Directory.CreateDirectory(ToolsDir);

        if (!File.Exists(YtDlpPath))
            await DownloadYtDlpAsync(progress);

        if (!File.Exists(FfmpegPath))
            await DownloadFfmpegAsync(progress);
    }

    private static async Task DownloadYtDlpAsync(IProgress<(string, int)>? progress)
    {
        const string url = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
        await DownloadFileAsync(url, YtDlpPath, "yt-dlp", progress);
    }

    private static async Task DownloadFfmpegAsync(IProgress<(string, int)>? progress)
    {
        const string url = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip";
        var zipPath = Path.Combine(ToolsDir, "ffmpeg.zip");

        progress?.Report(("ffmpeg", 0));
        await DownloadFileAsync(url, zipPath, "ffmpeg", progress);

        progress?.Report(("ffmpeg (распаковка)", 95));
        await Task.Run(() =>
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
            var entry = zip.Entries.FirstOrDefault(e =>
                e.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase) &&
                e.FullName.Contains("bin/", StringComparison.OrdinalIgnoreCase));
            if (entry != null)
                ZipFileExtensions.ExtractToFile(entry, FfmpegPath, overwrite: true);
        });

        File.Delete(zipPath);
        progress?.Report(("ffmpeg", 100));
    }

    private static async Task DownloadFileAsync(
        string url, string destPath, string label,
        IProgress<(string, int)>? progress)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("KebuzPlayer/1.0");

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        using var stream = await response.Content.ReadAsStreamAsync();
        using var file   = File.Create(destPath);

        var buffer = new byte[81920];
        long downloaded = 0;
        int read;

        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read));
            downloaded += read;
            if (totalBytes > 0)
                progress?.Report((label, (int)(downloaded * 100 / totalBytes)));
        }
    }
}
