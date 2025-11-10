using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using ytmp3ify.Models;
using Microsoft.Extensions.Configuration;

namespace ytmp3ify.Controllers
{
    public sealed class LoginUser
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

    [AllowAnonymous]
    [Route("auth")]
    public sealed class AuthController : Controller
    {
        private readonly IConfiguration _config;
        public AuthController(IConfiguration config) => _config = config;

        [HttpGet("login")]
        public IActionResult Login()
        {
            return View("Login", new LoginViewModel { ReturnUrl = "/Home/ready" });
        }

        private static bool SecureEquals(string? a, string? b)
        {
            var aa = System.Text.Encoding.UTF8.GetBytes(a ?? "");
            var bb = System.Text.Encoding.UTF8.GetBytes(b ?? "");
            return aa.Length == bb.Length &&
                   System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(aa, bb);
        }

        private IReadOnlyList<LoginUser> LoadUsers()
        {
            var users = _config.GetSection("Auth:Users").Get<List<LoginUser>>() ?? new List<LoginUser>();
            return users;
        }

        [ValidateAntiForgeryToken]
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid) return View("Login", model);

            var users = LoadUsers();
            if (users.Count == 0)
            {
                ModelState.AddModelError(string.Empty, "Server auth not configured.");
                return View("Login", model);
            }

            // find username match (case-sensitive or insensitive; pick your poison)
            var user = users.FirstOrDefault(u => string.Equals(u.Username, model.Username, StringComparison.Ordinal));
            if (user is null || !SecureEquals(model.Password, user.Password))
            {
                ModelState.AddModelError(string.Empty, "Invalid credentials.");
                return View("Login", model);
            }

            var claims = new[] { new Claim(ClaimTypes.Name, user.Username) };

            var id = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(id);

            var props = new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
            };

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, props);
            return LocalRedirect(model.ReturnUrl ?? "/audio");
        }

        [HttpGet("logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        [HttpGet("denied")]
        public IActionResult Denied() => View("Denied");
    }
}
