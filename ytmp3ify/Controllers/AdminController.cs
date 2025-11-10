using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace ytmp3ify.Controllers
{
    [Authorize]
    [Route("admin")]
    public class AdminController : Controller
    {
        private static string CookiePath()
        {
            // Azure App Service on Windows exposes HOME=D:\home
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrWhiteSpace(home))
            {
                return Path.Combine(home, "data", "ytmp3ify", "youtube.txt");
            }

            // Fallback to D:\home style if HOME missing (unlikely on App Service)
            return @"D:\home\data\ytmp3ify\youtube.txt";
        }

        private bool IsOwner() => User?.Identity?.IsAuthenticated == true && string.Equals(User.Identity!.Name, "addam.mp3", StringComparison.OrdinalIgnoreCase);

        [HttpGet("cookie")]
        public IActionResult Cookie()
        {
            if (!IsOwner()) { return NotFound(); } // hide from everyone else

            var path = CookiePath();
            var exists = System.IO.File.Exists(path);
            var size = exists ? new FileInfo(path).Length : 0;
            ViewBag.Path = path;
            ViewBag.Exists = exists;
            ViewBag.Size = size;
            return View();
        }

        [HttpPost("cookie")]
        [ValidateAntiForgeryToken]
        public IActionResult Cookie(string cookieText)
        {
            if (!IsOwner()) return NotFound();

            if (string.IsNullOrWhiteSpace(cookieText))
            {
                ModelState.AddModelError("", "Cookie text is required.");
                return Cookie();
            }

            var path = CookiePath();
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);

            // Atomic replace: write to temp, then move over existing
            var tmp = Path.Combine(dir, "youtube.txt.tmp");
            System.IO.File.WriteAllText(tmp, cookieText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (System.IO.File.Exists(path))
            {
                // On Windows, File.Replace gives atomic swap + optional backup; using simple Move overwrite here:
                System.IO.File.Delete(path);
            }
            System.IO.File.Move(tmp, path);

            TempData["ok"] = "Cookie updated.";
            return RedirectToAction(nameof(Cookie));
        }
    }
}