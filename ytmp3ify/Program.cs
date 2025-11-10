using Microsoft.Net.Http.Headers;
using ytmp3ify.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Cookie auth (1 hour, no sliding)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opt =>
    {
        opt.LoginPath = "/auth/login";
        opt.AccessDeniedPath = "/auth/denied";
        opt.ExpireTimeSpan = TimeSpan.FromHours(1);
        opt.SlidingExpiration = false;
        opt.Cookie.Name = "ytauth";
        opt.Cookie.HttpOnly = true;
        opt.Cookie.SecurePolicy = CookieSecurePolicy.Always; // enable on HTTPS
    });

builder.Services.AddAuthorization();

// Dependency Injection
builder.Services.AddSingleton<YtdlpService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
    ctx.Response.Headers["X-Download-Options"] = "noopen";
    await next();
});

app.UseAuthentication();
app.UseAuthorization();

// Conventional route for MVC
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();