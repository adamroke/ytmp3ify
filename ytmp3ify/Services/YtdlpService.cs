using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;
using YoutubeDLSharp.Options;

namespace ytmp3ify.Services
{
    public sealed class YtdlpService
    {
        private readonly string _workDir;
        private readonly string _binDir;
        private readonly YoutubeDL _ytdl; 
        private readonly string _defaultCookieFile;

        public YtdlpService()
        {
            // Temp workspace (Azure Windows App Service -> D:\local\Temp\...)
            _workDir = Path.Combine(Path.GetTempPath(), "yt-audio-api");
            Directory.CreateDirectory(_workDir);

            // Binaries are deployed alongside the app in a folder we publish: bin-deps\
            // AppContext.BaseDirectory -> the folder where your app is running from
            _binDir = Path.Combine(AppContext.BaseDirectory, "bin-deps");
            Directory.CreateDirectory(_binDir); // harmless if it already exists

            var ytPath = Path.Combine(_binDir, "yt-dlp.exe");
            var ffPath = Path.Combine(_binDir, "ffmpeg.exe");

            _ytdl = new YoutubeDL
            {
                YoutubeDLPath = ytPath,
                FFmpegPath = ffPath,
                OutputFolder = _workDir,
                OverwriteFiles = true
            };

            _defaultCookieFile = pickCookie();
        }

        public (bool Success, string? Error) ValidateBinaries()
        {
            try
            {
                if (!File.Exists(_ytdl.YoutubeDLPath))
                    return (false, $"Missing yt-dlp at {_ytdl.YoutubeDLPath}. Place yt-dlp.exe in bin-deps/.");

                if (!File.Exists(_ytdl.FFmpegPath))
                    return (false, $"Missing ffmpeg at {_ytdl.FFmpegPath}. Place ffmpeg.exe in bin-deps/.");

                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public async Task<(bool Success, string? OutputFile, string? Error)> DownloadAudioAsync(string url, string format, string? cookieFile = null, string? cookieHeader = null)
        {
            try
            {
                //1. Probe for metadata
                var (infoOk, info, infoErr) = await FetchInfoAsync(url, cookieFile, cookieHeader);

                //1.5. if FetchInfoAsync fails, just use generic metadata and proceed with download
                var videoTitle = infoOk ? (info!.Title ?? "Unknown Title") : "Unknown Title";
                var channel = infoOk ? (info!.Channel ?? info!.Uploader ?? "Unknown Channel") : "Unknown Channel";
                var canonical = infoOk ? (info!.WebpageUrl ?? url) : url;

                var (cookieFileUse, cookieHeaderUse) = ResolveCookies(cookieFile, cookieHeader);

                var opts = new OptionSet
                {
                    ExtractAudio = true,
                    AudioQuality = 0, // best
                    EmbedThumbnail = false,
                    EmbedMetadata = false,   //we will write our own later
                    RestrictFilenames = true,
                    NoCheckCertificates = true,
                    IgnoreErrors = false,
                    Output = Path.Combine(_workDir, "audio - %(channel|uploader)s - %(title)s.%(ext)s"),
                    Cookies = cookieFileUse
                };

                // Use android client ONLY when no cookies are present
                if (cookieFileUse is null && string.IsNullOrWhiteSpace(cookieHeaderUse))
                {
                    opts.ExtractorArgs = "youtube:player_client=android";
                }

                //3. Download audio according to options configured above
                var res = await _ytdl.RunAudioDownload(url, (format == "best" ? AudioConversionFormat.Best : ParseFormat(format)), overrideOptions: opts);

                if (!res.Success) { return (false, null, FlattenError(res.ErrorOutput) ?? "yt-dlp failed"); }

                //4. Resolve output
                var path = handlePath(res.Data);

                //5. Remux to keep ONLY chosen metadata
                var remux = RemuxWithSelectedMetadata(path, videoTitle, channel, canonical);
                if (!remux.Success) { return (false, null, remux.Error ?? "Failed to set metadata"); }

                return path == "null" ? (false, null, "No output produced") : (true, path, null);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        public async Task<(bool Success, VideoData? Info, string? Error)> FetchInfoAsync(string url, string? cookieFile = null, string? cookieHeader = null)
        {
            try
            {
                var (cookieFileUse, cookieHeaderUse) = ResolveCookies(cookieFile, cookieHeader);

                var probe = new OptionSet
                {
                    // Cookies file path (Netscape format) if present
                    Cookies = cookieFileUse
                };

                if (cookieFileUse is null && string.IsNullOrWhiteSpace(cookieHeaderUse))
                {
                    probe.ExtractorArgs = "youtube:player_client=android";
                }

                var infoRes = await _ytdl.RunVideoDataFetch(url, overrideOptions: probe);
                if (!infoRes.Success || infoRes.Data is null)
                    return (false, null, FlattenError(infoRes.ErrorOutput) ?? "Failed to fetch video info");

                return (true, infoRes.Data, null);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        private string pickCookie()
        {
            string cookiePath;

            // Choose cookie path by environment:
            // - Azure App Service: D:\home\data\ytmp3ify\youtube.txt (outside webroot)
            // - Local dev: <repo-root>/dev-cookies/youtube.txt
            // - Fallback: %AppData%\ytmp3ify\youtube.txt
            var envName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
            var home = Environment.GetEnvironmentVariable("HOME"); // "D:\home" on App Service

            if (!string.IsNullOrEmpty(home))
            {
                // Azure App Service
                var dataDir = Path.Combine(home, "data", "ytmp3ify");
                Directory.CreateDirectory(dataDir);
                cookiePath = Path.Combine(dataDir, "youtube.txt");
            }
            else if (string.Equals(envName, "Development", StringComparison.OrdinalIgnoreCase))
            {
                // Local dev: project-root/dev-cookies/youtube.txt
                var devDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "dev-cookies");
                var devDirFull = Path.GetFullPath(devDir);
                Directory.CreateDirectory(devDirFull);
                cookiePath = Path.Combine(devDirFull, "youtube.txt");
            }
            else
            {
                // Fallback: user profile appdata
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var appDir = Path.Combine(appData, "ytmp3ify");
                Directory.CreateDirectory(appDir);
                cookiePath = Path.Combine(appDir, "youtube.txt");
            }

            return cookiePath;
        }

        private (string? CookieFile, string? CookieHeader) ResolveCookies(string? cookieFileParam, string? cookieHeaderParam)
        {
            if (!string.IsNullOrWhiteSpace(cookieFileParam)) return (cookieFileParam, null);
            if (!string.IsNullOrWhiteSpace(cookieHeaderParam)) return (null, cookieHeaderParam);
            if (File.Exists(_defaultCookieFile)) return (_defaultCookieFile, null);
            return (null, null);
        }

        private static string Q(string s) => "\"" + s.Replace("\"", "'") + "\""; // naive quote helper

        private (bool Success, string? NewPath, string? Error) RemuxWithSelectedMetadata(string inputPath, string title, string channel, string url)
        {
            try
            {
                if(inputPath == null) { return (false, "null", "No output produced"); }

                var ext = Path.GetExtension(inputPath).Trim('.').ToLowerInvariant();
                var tempPath = Path.Combine(Path.GetDirectoryName(inputPath)!, Path.GetFileNameWithoutExtension(inputPath) + ".clean." + ext);

                // Strip ALL metadata: -map_metadata -1
                // Copy audio (no re-encode): -c copy
                // Add only the tags we want:
                //   title  = video title
                //   artist = channel/uploader (shows up nicely in most players)
                //   comment= canonical video URL
                // Works for mp3/m4a/aac/flac/opus/ogg
                var args = $"-y -i {Q(inputPath)} -vn -c copy -map_metadata -1 " +
                           $"-metadata title={Q(title)} -metadata artist={Q(channel)} -metadata comment={Q(url)} {Q(tempPath)}";

                var psi = new ProcessStartInfo
                {
                    FileName = _ytdl.FFmpegPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                using var p = Process.Start(psi)!;
                p.WaitForExit();

                if (p.ExitCode != 0 || !File.Exists(tempPath))
                {
                    var err = p.StandardError.ReadToEnd();
                    return (false, null, string.IsNullOrWhiteSpace(err) ? "ffmpeg remux failed" : err);
                }

                // Replace original atomically-ish
                File.Delete(inputPath);
                File.Move(tempPath, inputPath);

                return (true, inputPath, null);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        private string handlePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                // Fallback: grab the most recent file in the work dir (in case yt-dlp changed names)
                var candidate = Directory.EnumerateFiles(_workDir)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (candidate is null)
                    return "null";

                path = candidate;
            }

            return path;
        }

        public string GetContentTypeByExtension(string ext)
        {
            ext = ext.Trim('.').ToLowerInvariant();
            return ext switch
            {
                "mp3" => "audio/mpeg",
                "m4a" => "audio/mp4",
                "aac" => "audio/aac",
                "flac" => "audio/flac",
                "opus" => "audio/ogg",
                "ogg" => "audio/ogg",
                _ => "application/octet-stream"
            };
        }

        private static AudioConversionFormat ParseFormat(string fmt) => fmt switch
        {
            "mp3" => AudioConversionFormat.Mp3,
            "m4a" => AudioConversionFormat.M4a,
            "aac" => AudioConversionFormat.Aac,
            "flac" => AudioConversionFormat.Flac,
            _ => AudioConversionFormat.Best
        };

        private static string? FlattenError(object? err)
        {
            if (err is null) return null;
            if (err is string s) return s;
            if (err is IEnumerable<string> many) return string.Join(Environment.NewLine, many);
            return err.ToString();
        }
    }

}
