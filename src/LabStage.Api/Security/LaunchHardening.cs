using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LabStage.Infrastructure.Payments;

namespace LabStage.Api.Security;

/// <summary>
/// Production start-up guard: the app refuses to start outside Development while a setting that only makes sense for
/// local testing is still on. A staging server can opt out with Launch:AllowIncompleteSetup=true (logged loudly).
/// </summary>
public static class LaunchGuard
{
    public static IReadOnlyList<string> Problems(IConfiguration c)
    {
        var p = new List<string>();
        var hosts = c["AllowedHosts"];
        if (string.IsNullOrWhiteSpace(hosts) || hosts.Split(';').Any(h => h.Trim() == "*"))
            p.Add("AllowedHosts must list the real domain(s), not \"*\" (set AllowedHosts).");
        var baseUrl = c["App:PublicBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl) || !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            p.Add("App:PublicBaseUrl must be the public https address (used in email links).");
        if (!PaymobPaymentGateway.IsConfigured(c))
            p.Add("No real payment gateway: set Payments:Provider=Paymob and the Payments:Paymob:* secrets (the test simulator only works in Development).");
        if (string.IsNullOrWhiteSpace(c["Email:SendGrid:ApiKey"]) && string.IsNullOrWhiteSpace(c["Email:Smtp:Host"]))
            p.Add("No email sender: set Email:Smtp:* or Email:SendGrid:ApiKey (password resets and alerts would not be sent).");
        return p;
    }

    public static void Enforce(IConfiguration c, IHostEnvironment env, ILogger log)
    {
        if (env.IsDevelopment()) return;
        var problems = Problems(c);
        if (problems.Count == 0) return;
        if (string.Equals(c["Launch:AllowIncompleteSetup"], "true", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var x in problems) log.LogCritical("INCOMPLETE SETUP (allowed by Launch:AllowIncompleteSetup): {Problem}", x);
            return;
        }
        throw new InvalidOperationException("Not ready for production:\n - " + string.Join("\n - ", problems));
    }
}

/// <summary>
/// Security headers (CSP and friends), a same-origin check on every write (CSRF defence on top of SameSite cookies),
/// and a log line for every refused request (401/403/429) with IP and user.
/// </summary>
public sealed class SecurityMiddleware(RequestDelegate next, IConfiguration config, ILogger<SecurityMiddleware> log)
{
    // The one inline script on every page (same text in each <head>): marks that JS runs, and handles the brand fonts:
    // a returning visitor (fonts cached) gets them at once; on a first visit the page stays in the system font and
    // fonts-warm.js downloads them after the page has loaded, so nothing jumps or waits on them.
    public const string InlineScript =
        "document.documentElement.classList.add('js');(function(d,w){var s=function(u,c){var e=d.createElement(c?'link':'script');if(c){e.rel='stylesheet';e.href=u}else{e.src=u}d.head.appendChild(e)};try{if(w.localStorage.getItem('taaj-fonts'))return s('/assets/css/fonts.css?v=3',1)}catch(e){}w.addEventListener('load',function(){setTimeout(function(){s('/assets/js/core/fonts-warm.js?v=1')},1500)})})(document,window)";
    private static readonly string InlineScriptHash =
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(InlineScript)));

    private static readonly HashSet<string> Unsafe = ["POST", "PUT", "PATCH", "DELETE"];

    public async Task InvokeAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path;
        var h = ctx.Response.Headers;
        h.XContentTypeOptions = "nosniff";
        h.XFrameOptions = "DENY";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(self), payment=(self)";
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        h["Cross-Origin-Resource-Policy"] = "same-origin";
        var host = ctx.Request.Host.Value;
        h.ContentSecurityPolicy = string.Join("; ",
            "default-src 'self'",
            $"script-src 'self' 'sha256-{InlineScriptHash}'",
            "style-src 'self' 'unsafe-inline'",
            "font-src 'self'",
            "img-src 'self' data: blob:",
            $"connect-src 'self' wss://{host} ws://{host}",
            "media-src 'self' blob:",
            "worker-src 'self' blob:",
            "object-src 'none'",
            "base-uri 'self'",
            "form-action 'self'",
            "frame-ancestors 'none'");

        // CSRF: a write from a browser must come from our own site. Gateway callbacks are signed and exempt.
        if (Unsafe.Contains(ctx.Request.Method) && (path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs"))
            && !path.StartsWithSegments("/api/v1/payments/webhooks") && !SameOrigin(ctx))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new { title = "Forbidden", status = 403, detail = "Cross-site request refused." });
            Log(ctx, "cross-site write refused");
            return;
        }

        await next(ctx);

        if (ctx.Response.StatusCode is 401 or 403 or 429 && (path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs")))
            Log(ctx, "refused");
    }

    private bool SameOrigin(HttpContext ctx)
    {
        var origin = ctx.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin)) origin = ctx.Request.Headers.Referer.ToString();
        if (string.IsNullOrEmpty(origin)) return true; // not a browser cross-site request (server-to-server, tests)
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var u)) return false;
        var originHost = u.IsDefaultPort ? u.Host : $"{u.Host}:{u.Port}";
        if (string.Equals(originHost, ctx.Request.Host.Value, StringComparison.OrdinalIgnoreCase)) return true;
        var allowed = (config["Security:AllowedOrigins"] ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return allowed.Any(a => string.Equals(a.TrimEnd('/'), $"{u.Scheme}://{originHost}", StringComparison.OrdinalIgnoreCase));
    }

    private void Log(HttpContext ctx, string what) =>
        log.LogWarning("Security {What}: {Status} {Method} {Path} ip={Ip} user={User}", what, ctx.Response.StatusCode, ctx.Request.Method,
            ctx.Request.Path.Value, ctx.Connection.RemoteIpAddress, ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "-");
}
