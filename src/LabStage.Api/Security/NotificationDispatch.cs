using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using LabStage.Api.Hubs;
using LabStage.Application.Email;
using LabStage.Domain.Entities;
using LabStage.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LabStage.Api.Security;

/// <summary>Queue of saved notifications waiting to be pushed live and emailed.</summary>
public sealed class NotificationQueue
{
    private readonly Channel<Guid> channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });
    public ChannelReader<Guid> Reader => channel.Reader;
    public void Enqueue(Guid id) => channel.Writer.TryWrite(id);
}

/// <summary>
/// Every notification added anywhere in the app gets its category and order number here, and once saved it is queued
/// for delivery (live push + email). Code that creates notifications only needs <c>db.Notifications.Add(...)</c>.
/// </summary>
public sealed class NotificationSaveInterceptor(NotificationQueue queue) : SaveChangesInterceptor
{
    private static readonly ConditionalWeakTable<DbContext, List<Guid>> Pending = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData e, InterceptionResult<int> result) { Collect(e.Context); return result; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> result, CancellationToken ct = default) { Collect(e.Context); return ValueTask.FromResult(result); }
    public override int SavedChanges(SaveChangesCompletedEventData e, int result) { Flush(e.Context); return result; }
    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData e, int result, CancellationToken ct = default) { Flush(e.Context); return ValueTask.FromResult(result); }

    private static void Collect(DbContext? ctx)
    {
        if (ctx is null) return;
        var added = ctx.ChangeTracker.Entries<Notification>().Where(x => x.State == EntityState.Added).Select(x => x.Entity).ToList();
        if (added.Count == 0) return;
        var list = Pending.GetOrCreateValue(ctx);
        foreach (var n in added)
        {
            if (string.IsNullOrEmpty(n.Category)) n.Category = NotificationCategories.Of(n.Type);
            n.OrderNumber ??= OrderFromLink(n.LinkUrl);
            if (!list.Contains(n.Id)) list.Add(n.Id);
        }
    }

    private void Flush(DbContext? ctx)
    {
        if (ctx is null || !Pending.TryGetValue(ctx, out var list)) return;
        foreach (var id in list) queue.Enqueue(id);
        list.Clear();
    }

    private static string? OrderFromLink(string? link)
    {
        if (string.IsNullOrEmpty(link)) return null;
        var i = link.IndexOf("orderNumber=", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        var v = link[(i + "orderNumber=".Length)..].Split('&', '#')[0];
        v = Uri.UnescapeDataString(v);
        return v.Length is > 0 and <= 32 ? v : null;
    }
}

/// <summary>Delivers queued notifications: live to the user's open pages (SignalR) and by email, per the user's preferences.</summary>
public sealed class NotificationDispatcher(NotificationQueue queue, IServiceScopeFactory scopes, IHubContext<OrderHub> hub, IEmailSender email,
    IConfiguration config, ILogger<NotificationDispatcher> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await foreach (var id in queue.Reader.ReadAllAsync(stop))
        {
            try { await DeliverAsync(id, stop); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Notification {Id} delivery failed", id); }
        }
    }

    public async Task DeliverAsync(Guid id, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabStageDbContext>();
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (n is null) return;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == n.UserId, ct);
        if (user is null) return;
        var pref = await db.NotificationPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == n.UserId && p.Category == n.Category, ct);
        var inSite = pref?.InSite ?? true;
        var byEmail = pref?.Email ?? NotificationCategories.EmailByDefault(n.Category);

        if (!inSite && !n.IsRead) { n.IsRead = true; await db.SaveChangesAsync(ct); }
        if (inSite)
        {
            var unread = await db.Notifications.CountAsync(x => x.UserId == n.UserId && !x.IsRead, ct);
            // Lab users: amounts stay hidden in live pop-ups (the finance password rules apply to the list itself).
            var lab = user.Role is AppRoles.LaboratoryOwner or AppRoles.LaboratoryStaff;
            await hub.Clients.Group($"user:{n.UserId}").SendAsync("notification", new
            {
                n.Id, n.Type, n.Category, Title = lab ? MaskMoney(n.Title) : n.Title, Body = lab ? MaskMoney(n.Body) : n.Body,
                n.LinkUrl, n.OrderNumber, n.CreatedAt, n.IsRead, UnreadCount = unread,
            }, ct);
        }

        // Email only to confirmed addresses and never twice.
        if (byEmail && user.EmailConfirmedAt is not null && !user.IsSuspended && n.EmailedAt is null)
        {
            var baseUrl = (config["App:PublicBaseUrl"] ?? "http://localhost:5080").TrimEnd('/');
            await email.SendAsync(Build(user, n, baseUrl), ct);
            n.EmailedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    private static readonly System.Text.RegularExpressions.Regex Money = new(@"\d[\d,٬.]*\s*(ج\.م|EGP)");
    public static string MaskMoney(string s) => Money.Replace(s ?? string.Empty, "•••• ج.م");

    private static readonly Dictionary<string, (string Ar, string En)> CategoryNames = new()
    {
        [NotificationCategories.Orders] = ("الطلبات", "Orders"),
        [NotificationCategories.Production] = ("مراحل الإنتاج", "Production"),
        [NotificationCategories.Payments] = ("المدفوعات", "Payments"),
        [NotificationCategories.Delivery] = ("التوصيل", "Delivery"),
        [NotificationCategories.Chat] = ("الرسائل", "Messages"),
        [NotificationCategories.Account] = ("الحساب", "Account"),
    };

    /// <summary>Branded email: Arabic first, English below, one button to the order.</summary>
    public static EmailMessage Build(AppUser u, Notification n, string baseUrl)
    {
        static string e(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        (string Ar, string En) cat = CategoryNames.GetValueOrDefault(n.Category, ("تنبيه", "Update"));
        var link = string.IsNullOrEmpty(n.LinkUrl) ? baseUrl : n.LinkUrl.StartsWith("http") ? n.LinkUrl : baseUrl + n.LinkUrl;
        var html = $"""
            <div dir="rtl" style="font-family:Tahoma,Arial,sans-serif;max-width:560px;margin:auto;color:#0B1A45">
              <p style="margin:0 0 6px;color:#2A62F5;font-size:13px;font-weight:bold">تاج · {e(cat.Ar)}</p>
              <h2 style="margin:0 0 10px;color:#0D2A78">{e(n.Title)}</h2>
              <p>مرحبًا {e(u.FullName)}،</p>
              <p>{e(n.Body)}</p>
              <p><a href="{e(link)}" style="display:inline-block;background:#0D2A78;color:#fff;padding:12px 22px;border-radius:999px;text-decoration:none">فتح على تاج</a></p>
              <p style="color:#5A6785;font-size:12px">تصلك هذه الرسالة حسب إعدادات التنبيهات في حسابك، ويمكنك تغييرها من صفحة «حسابي».</p>
              <hr style="border:0;border-top:1px solid #E6E9F1">
              <div dir="ltr" style="text-align:left;color:#5A6785;font-size:12px">
                <p>Taaj · {e(cat.En)} update. <a href="{e(link)}">Open in Taaj</a>. You can change email alerts from "My account".</p>
              </div>
            </div>
            """;
        var text = $"{n.Title}\n\n{n.Body}\n{link}\n\nTaaj · {cat.En} update: {link}\n";
        return new EmailMessage(u.Email, $"{n.Title} — تاج", html, text);
    }
}
