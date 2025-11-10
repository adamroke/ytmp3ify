using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ytmp3ify.Controllers
{
    public class HomeController : Controller
    {
        [AllowAnonymous]
        [HttpGet("")]
        [HttpGet("home")]
        [HttpGet("home/index")]
        public IActionResult Index()
        {
            if (User?.Identity?.IsAuthenticated == true)
                return RedirectToAction("ready");

            return RedirectToAction("Login", "Auth");
        }

        [Authorize]
        [HttpGet("home/ready")]
        public IActionResult ready()
        {
            return View();
        }

        [Authorize]
        [HttpGet("logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Auth");
        }
    }
}
