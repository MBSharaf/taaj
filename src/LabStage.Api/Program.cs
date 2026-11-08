using System.Threading.RateLimiting;
using LabStage.Api.Hubs;
using LabStage.Application.Payments;
using LabStage.Infrastructure.Payments;
using LabStage.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var isDevelopment = builder.Environment.IsDevelopment();

// ---- Secrets: never read from appsettings.json. Development uses user-secrets (or a throwaway value);
// every other environment must supply them as environment variables / a vault, or the app refuses to start.
const string LegacyDevWebhookSecret = "dev-sandbox-hmac-secret-change-in-prod";
var connStr = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connStr))
{
    if (!isDevelopment)
        throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured (set ConnectionStrings__DefaultConnection).");
    connStr = "Server=(localdb)\\mssqllocaldb;Database=LabStageEgyptDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";
}

var webhookSecret = builder.Configuration["Payments:WebhookSecret"];
if (string.IsNullOrWhiteSpace(webhookSecret))
{
    if (!isDevelopment)
        throw new InvalidOperationException("Payments:WebhookSecret is not configured (set Payments__WebhookSecret).");
    // Throwaway per-process secret: the in-app sandbox helper signs with it, nothing outside can.
    builder.Configuration["Payments:WebhookSecret"] = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
}
else if (!isDevelopment && (webhookSecret == LegacyDevWebhookSecret || webhookSecret.Length < 32))
{
    throw new InvalidOperationException("Payments:WebhookSecret is the old development value or shorter than 32 characters.");
}

builder.Services.AddDbContext<LabStageDbContext>(o => o.UseSqlServer(connStr));
builder.Services.AddScoped<IPaymentGateway, SandboxPaymentGateway>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "LabStage.Auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        // HTTPS-only cookie outside local development; sessions expire after 8 idle hours unless "remember me".
        o.Cookie.SecurePolicy = isDevelopment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
        o.LoginPath = "/login.html";
        // A suspended or deleted account loses access on its next request, not when the cookie expires.
        o.Events.OnValidatePrincipal = async ctx =>
        {
            var id = ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var db = ctx.HttpContext.RequestServices.GetRequiredService<LabStageDbContext>();
            var active = Guid.TryParse(id, out var userId) &&
                         await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && !u.IsSuspended, ctx.HttpContext.RequestAborted);
            if (!active)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
        o.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
            ctx.Response.Redirect("/");
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSignalR();

builder.Services.AddMemoryCache();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Sign-in and sign-up: at most 10 attempts per minute from one address (on top of the per-account lockout).
    o.AddPolicy("auth", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 240,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

builder.Services.AddControllers();
builder.Services.AddOpenApi("v1");
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes
        .Concat(["image/svg+xml", "model/gltf-binary"]);
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LabStageDbContext>();
    await DatabaseSeeder.SeedAsync(db, new SeedOptions(
        IncludeDemoData: app.Environment.IsDevelopment(),
        BootstrapAdminEmail: app.Configuration["Bootstrap:AdminEmail"],
        BootstrapAdminPassword: app.Configuration["Bootstrap:AdminPassword"]));
}

app.UseResponseCompression();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "LabStage API v1"));
}
else
{
    app.UseHsts();
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});
app.UseHttpsRedirection();

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h.XContentTypeOptions = "nosniff";
    h.XFrameOptions = "DENY";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(self)";
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/api/v1/health");
app.MapHub<OrderHub>("/hubs/orders");
app.MapControllers();

app.Run();

public partial class Program;
