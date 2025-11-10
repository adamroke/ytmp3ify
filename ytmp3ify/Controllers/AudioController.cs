using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using ytmp3ify.Services;

namespace ytmp3ify.Controllers
{
    public sealed class DirectRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string Url { get; set; } = "";
        public string? Format { get; set; } = "best";
    }

    [Authorize]
    [Route("audio")]
    public sealed class AudioController : Controller
    {
        private readonly YtdlpService _svc;

        public AudioController(YtdlpService svc) => _svc = svc;

        // GET /audio?url=...&format=mp3|m4a|aac|flac|best
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string url, [FromQuery] string? format)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                // show the form
                ModelState.Clear();              // nuke any automatic messages
                ViewBag.Submitted = false;      // flag for view
                return View("Index");
            }

            ViewBag.Submitted = true;

            var fmt = (format ?? "best").Trim().ToLowerInvariant();
            var allowed = new HashSet<string> { "best", "mp3", "m4a", "aac", "flac" };
            if (!allowed.Contains(fmt))
            {
                ModelState.AddModelError(string.Empty, $"Unsupported format '{fmt}'. Use one of: best, mp3, m4a, aac, flac.");
                return View("Index");
            }

            var ok = _svc.ValidateBinaries();
            if (!ok.Success)
            {
                ModelState.AddModelError(string.Empty, $"Downloader not ready: {ok.Error}");
                return View("Index");
            }

            var result = await _svc.DownloadAudioAsync(url, fmt);
            if (!result.Success || string.IsNullOrWhiteSpace(result.OutputFile) || !System.IO.File.Exists(result.OutputFile))
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Unknown error extracting audio");
                return View("Index");
            }

            var filePath = result.OutputFile!;
            var fileName = Path.GetFileName(filePath);
            var contentType = _svc.GetContentTypeByExtension(Path.GetExtension(filePath));

            // no-store to avoid sticky caches
            Response.Headers[HeaderNames.CacheControl] = "no-store";

            // Ensure temp cleanup after response completes (even if client aborts)
            HttpContext.Response.OnCompleted(() =>
            {
                try { System.IO.File.Delete(filePath); } catch { /* ignore */ }
                return Task.CompletedTask;
            });

            // Stream with range support (ControllerBase.File has this overload)
            var stream = System.IO.File.OpenRead(filePath);
            return File(stream, contentType, fileName, enableRangeProcessing: true);
        }

        // Optional: quick health probe to test in browser: GET /audio/healthz
        [AllowAnonymous]
        [HttpGet("healthz")]
        public IActionResult Health()
        {
            var ok = _svc.ValidateBinaries();
            return ok.Success ? Ok(new { ok = true }) : StatusCode(503, new { ok = false, error = ok.Error });
        }

        // ========= COOKIE-LESS POST ENDPOINT (JSON creds) =========
        // POST /audio/direct
        // Body: { "username": "...", "password": "...", "url": "...", "format": "best|mp3|m4a|aac|flac" }
        [AllowAnonymous]
        [HttpPost("direct")]
        [Consumes("application/json")]
        public async Task<IActionResult> Direct([FromBody] DirectRequest req)
        {
            if (req is null || string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password) || string.IsNullOrWhiteSpace(req.Url))
            {
                return BadRequest(new { error = "username, password, and url are required" });
            }

            // Load allowed users from configuration: Auth:Users:{i}:Username / Password
            var cfg = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var users = LoadUsers(cfg);
            if (users.Count == 0)
                return StatusCode(500, new { error = "server auth not configured" });

            var user = users.FirstOrDefault(u => string.Equals(u.Username, req.Username, StringComparison.Ordinal));
            if (user is null || !SecureEquals(req.Password, user.Password))
                return Unauthorized(new { error = "invalid credentials" });

            var fmt = (req.Format ?? "best").Trim().ToLowerInvariant();
            var allowed = new HashSet<string> { "best", "mp3", "m4a", "aac", "flac" };
            if (!allowed.Contains(fmt))
                return BadRequest(new { error = $"unsupported format '{fmt}'" });

            var ok = _svc.ValidateBinaries();
            if (!ok.Success)
                return Problem($"downloader not ready: {ok.Error}");

            var inputUrl = NormalizeUrl(req.Url);

            var result = await _svc.DownloadAudioAsync(inputUrl, fmt);
            if (!result.Success || string.IsNullOrWhiteSpace(result.OutputFile) || !System.IO.File.Exists(result.OutputFile))
                return Problem(result.Error ?? "failed to extract audio");

            var filePath = result.OutputFile!;
            var fileName = Path.GetFileName(filePath);
            var contentType = _svc.GetContentTypeByExtension(Path.GetExtension(filePath));

            Response.Headers[HeaderNames.CacheControl] = "no-store";
            HttpContext.Response.OnCompleted(() =>
            {
                try { System.IO.File.Delete(filePath); } catch { }
                return Task.CompletedTask;
            });

            var stream = System.IO.File.OpenRead(filePath);
            return File(stream, contentType, fileName, enableRangeProcessing: true);
        }

        // ------------ helpers (private, no external deps) ------------

        // pull users from config without requiring the Binder package
        private static List<LoginUser> LoadUsers(IConfiguration cfg)
        {
            var list = new List<LoginUser>();
            for (int i = 0; i < 100; i++)
            {
                var u = cfg[$"Auth:Users:{i}:Username"];
                var p = cfg[$"Auth:Users:{i}:Password"];
                if (string.IsNullOrEmpty(u) && string.IsNullOrEmpty(p))
                    break; // stop at first fully missing slot
                if (!string.IsNullOrEmpty(u) && !string.IsNullOrEmpty(p))
                    list.Add(new LoginUser { Username = u!, Password = p! });
            }
            return list;
        }

        private static bool SecureEquals(string? a, string? b)
        {
            var aa = System.Text.Encoding.UTF8.GetBytes(a ?? "");
            var bb = System.Text.Encoding.UTF8.GetBytes(b ?? "");
            return aa.Length == bb.Length &&
                   System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(aa, bb);
        }

        private static string NormalizeUrl(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            s = s.Trim();
            if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return s;
            return "https://" + s.TrimStart('/');
        }

        /*
        [AllowAnonymous]
        [HttpGet("diag")]
        public IActionResult Diag()
        {
            var baseDir = AppContext.BaseDirectory;
            var tempDir = Path.Combine(Path.GetTempPath(), "yt-audio-api");

            var yt = System.IO.Path.Combine(baseDir, "bin-deps", "yt-dlp.exe");
            var ff = System.IO.Path.Combine(baseDir, "bin-deps", "ffmpeg.exe");

            return Ok(new
            {
                BaseDirectory = baseDir,
                TempDir = tempDir,
                YtDlpPath = yt,
                YtDlpExists = System.IO.File.Exists(yt),
                FfmpegPath = ff,
                FfmpegExists = System.IO.File.Exists(ff)
            });
        }

        [Authorize]
        [HttpGet("cookie-status")]
        public IActionResult CookieStatus()
        {
            // Mirror YtdlpService.PickCookie() logic without calling it
            var envName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
            var home = Environment.GetEnvironmentVariable("HOME"); // "D:\\home" on Azure

            string path;
            if (!string.IsNullOrEmpty(home))
                path = Path.Combine(home, "data", "ytmp3ify", "youtube.txt");
            else if (string.Equals(envName, "Development", StringComparison.OrdinalIgnoreCase))
                path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "dev-cookies", "youtube.txt"));
            else
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ytmp3ify", "youtube.txt");

            var exists = System.IO.File.Exists(path);
            var size = exists ? new System.IO.FileInfo(path).Length : 0;

            return Ok(new
            {
                Environment = envName,
                CookiePath = path,
                Exists = exists,
                Size = size
            });
        }*/
    }
}