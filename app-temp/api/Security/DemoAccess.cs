using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Playback.Api.Db;

namespace Playback.Api.Security;

// Any valid username can create a workspace with the shared demo password.
public sealed class DemoAccess
{
    public const string CsrfCookie = "Playback.Csrf";
    public const string CsrfHeader = "X-CSRF-TOKEN";
    readonly string? password = Environment.GetEnvironmentVariable("PLAYBACK_DEMO_PASSWORD");
    readonly string? origin = Environment.GetEnvironmentVariable("PLAYBACK_PUBLIC_ORIGIN");
    readonly bool allowHttp;
    public bool Enabled { get; }
    string CredentialVersion(string account) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account + "\0" + password)));

    public DemoAccess(IHostEnvironment environment)
    {
        Enabled = !environment.IsDevelopment() || origin is not null || password is not null;
        if (!Enabled) return;
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Set PLAYBACK_DEMO_PASSWORD before starting the secured app");
        allowHttp = Uri.TryCreate(origin, UriKind.Absolute, out var local) && local.IsLoopback && local.Scheme == "http";
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            uri.Scheme != (allowHttp ? "http" : "https") || uri.AbsolutePath != "/" ||
            uri.Query != "" || uri.Fragment != "" || uri.UserInfo != "" || origin != uri.GetLeftPart(UriPartial.Authority))
            throw new InvalidOperationException("PLAYBACK_PUBLIC_ORIGIN must be an HTTPS origin without a trailing slash; HTTP is allowed only for localhost");
    }

    public void AddServices(IServiceCollection services)
    {
        if (!Enabled) return;
        var keys = services.AddDataProtection().SetApplicationName("Playback.Demo");
        if (!string.IsNullOrEmpty(PlaybackEnvironment.KeyPath))
            keys.PersistKeysToFileSystem(new DirectoryInfo(PlaybackEnvironment.KeyPath));
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.Cookie.Name = "Playback.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = allowHttp ? CookieSecurePolicy.None : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = false;
            options.Events.OnValidatePrincipal = async context =>
            {
                var account = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (account is null || !ValidUsername(account) ||
                    context.Principal?.FindFirstValue("credential-version") != CredentialVersion(account))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync();
                }
            };
        });
        services.AddAntiforgery(options =>
        {
            options.HeaderName = CsrfHeader;
            options.Cookie.Name = "Playback.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            // Cloud Run terminates HTTPS before forwarding HTTP to this process.
            // Secure cookies are explicit; proxy headers cannot relax this policy.
            options.Cookie.SecurePolicy = allowHttp ? CookieSecurePolicy.None : CookieSecurePolicy.Always;
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Global per instance: spoofing a forwarding header cannot bypass the login limit.
            options.AddFixedWindowLimiter("demo-login", limiter =>
            {
                limiter.PermitLimit = 10;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
                limiter.AutoReplenishment = true;
            });
        });
    }

    public void Use(WebApplication app)
    {
        if (!Enabled) return;
        app.UseAuthentication();
        app.UseRateLimiter();
        app.Use(async (http, next) =>
        {
            // This HTTPS deployment sits behind Cloud Run's TLS termination. Use the
            // configured scheme rather than trusting caller-supplied forwarding headers.
            if (!allowHttp) http.Request.Scheme = "https";
            http.Response.Headers.XContentTypeOptions = "nosniff";
            http.Response.Headers.ContentSecurityPolicy = "frame-ancestors 'none'";
            http.Response.Headers["Referrer-Policy"] = "same-origin";
            http.Response.Headers.CacheControl = "no-store";
            var path = http.Request.Path;
            if (path == "/healthz") { await next(http); return; }
            var unsafeMethod = !HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method);
            var suppliedOrigin = http.Request.Headers.Origin;
            if (http.Request.Headers["Sec-Fetch-Site"] == "cross-site" ||
                suppliedOrigin.Count > 0 && suppliedOrigin.ToString() != origin ||
                unsafeMethod && suppliedOrigin.ToString() != origin)
            {
                await Reject(http, 403, "Only requests from the configured app origin are allowed");
                return;
            }
            if (path != "/login" && http.User.Identity?.IsAuthenticated != true)
            {
                if (path.StartsWithSegments("/api")) await Reject(http, 401, "Sign in to Playback");
                else http.Response.Redirect("/login");
                return;
            }
            var antiforgery = http.RequestServices.GetRequiredService<IAntiforgery>();
            if (unsafeMethod)
            {
                if (path == "/login" && (!http.Request.HasFormContentType || http.Request.ContentLength is null or > 4096))
                {
                    await Reject(http, 400, "A bounded login form is required");
                    return;
                }
                try { await antiforgery.ValidateRequestAsync(http); }
                catch (AntiforgeryValidationException)
                {
                    await Reject(http, 403, "Invalid CSRF token. Reload the app and retry");
                    return;
                }
            }
            else if (http.User.Identity?.IsAuthenticated == true &&
                !path.StartsWithSegments("/api") && http.Request.Headers.Accept.ToString().Contains("text/html"))
                IssueToken(http, antiforgery);
            await next(http);
        });

        app.MapGet("/login", (HttpContext http, IAntiforgery csrf) =>
        {
            if (http.User.Identity?.IsAuthenticated == true) return Results.Redirect("/");
            return LoginPage(http, csrf);
        });
        app.MapPost("/login", async (HttpContext http, IAntiforgery csrf, PlaybackStore store) =>
        {
            if (!http.Request.HasFormContentType || http.Request.ContentLength is null or > 4096)
                return Results.BadRequest(new { error = "A bounded login form is required" });
            var form = await http.Request.ReadFormAsync(http.RequestAborted);
            var account = form["account"].ToString();
            var validAccount = ValidUsername(account);
            var validPassword = Equal(form["password"].ToString(), password!);
            if (!(validAccount & validPassword)) return LoginPage(http, csrf, failed: true);
            await store.GetOrCreateDemoUser(account, http.RequestAborted);
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, account),
                new Claim(ClaimTypes.Name, account),
                new Claim("credential-version", CredentialVersion(account))
            ], CookieAuthenticationDefaults.AuthenticationScheme));
            await http.SignInAsync(principal);
            http.User = principal;
            IssueToken(http, csrf);
            return Results.Redirect("/");
        }).RequireRateLimiting("demo-login");
        app.MapPost("/api/auth/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync();
            http.Response.Cookies.Delete(CsrfCookie);
            return Results.NoContent();
        });
    }

    void IssueToken(HttpContext http, IAntiforgery csrf) => http.Response.Cookies.Append(
        CsrfCookie, csrf.GetAndStoreTokens(http).RequestToken!, new CookieOptions
        { HttpOnly = false, Secure = !allowHttp, SameSite = SameSiteMode.Strict, Path = "/" });

    static bool Equal(string supplied, string expected) => CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));

    static bool ValidUsername(string name) => name.Length is >= 1 and <= 64 &&
        name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');

    static Task Reject(HttpContext http, int status, string error)
    {
        http.Response.StatusCode = status;
        return http.Response.WriteAsJsonAsync(new { error });
    }

    static IResult LoginPage(HttpContext http, IAntiforgery csrf, bool failed = false)
    {
        var token = HtmlEncoder.Default.Encode(csrf.GetAndStoreTokens(http).RequestToken!);
        return Results.Content($$"""
            <!doctype html><html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1"><title>Sign in · Playback</title>
            <style>
            *{box-sizing:border-box}body{margin:0;background:#f4f5fa;color:#263147;font:16px system-ui}
            main{max-width:380px;margin:12vh auto;padding:28px;background:white;border-radius:16px}
            h1{margin-top:0}label{display:block;margin-top:18px}input,button{width:100%;padding:12px;margin-top:6px;border:1px solid #cbd0df;border-radius:8px;font:inherit}
            button{margin-top:24px;background:#6855a8;color:white;cursor:pointer}p{color:#b23a48}
            </style></head><body><main><h1>Playback</h1><form method="post" action="/login">
            <input type="hidden" name="__RequestVerificationToken" value="{{token}}">
            <label>Username<input name="account" autocomplete="username" maxlength="64" pattern="[A-Za-z0-9_.-]+" required autofocus></label>
            <label>Password<input name="password" type="password" autocomplete="current-password" maxlength="1024" required></label>
            {{(failed ? "<p role=\"alert\">Username or password is incorrect.</p>" : "")}}
            <small>New usernames create a workspace automatically.</small>
            <button type="submit">Sign in</button></form></main></body></html>
            """, "text/html", statusCode: failed ? 401 : 200);
    }
}
