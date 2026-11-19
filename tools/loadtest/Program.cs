// Taaj load-test tool: seeds 10,000 users, signs them in, and runs the three scenarios.
// Usage: dotnet LoadTool.dll <seed|login|s1|s2|s3> [options]
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using LabStage.Application.Security;
using LabStage.Domain.Entities;
using LabStage.Domain.Orders;
using LabStage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

ThreadPool.SetMinThreads(512, 512);
var cmd = args.Length > 0 ? args[0] : "help";
string Arg(string name, string def) { var i = Array.IndexOf(args, "--" + name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }
int N = int.Parse(Arg("pairs", "5000"));
var baseUrl = Arg("url", "http://127.0.0.1:5080");
var resultsDir = Arg("out", "/work/results");
Directory.CreateDirectory(resultsDir);
const string Password = "Load@12345";
int Shard = int.Parse(Arg("shard", "0")), Shards = int.Parse(Arg("shards", "1"));
var maxConn = int.Parse(Arg("maxconn", int.MaxValue.ToString()));
var t0 = DateTimeOffset.FromUnixTimeSeconds(long.Parse(Arg("t0", "0")));
async Task Until(DateTimeOffset at) { var d = at - DateTimeOffset.UtcNow; if (d > TimeSpan.Zero) await Task.Delay(d); }
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

switch (cmd)
{
    case "seed": await Seed(); break;
    case "login": await Login(); break;
    case "s1": await S1(); break;
    case "s2": await S2(); break;
    case "s3": await S3(); break;
    case "merge": Merge(); break;
    default: Console.WriteLine("seed | login | s1 | s2 | s3"); break;
}
return;

// ---------------------------------------------------------------- seed
async Task Seed()
{
    var cs = Arg("cs", "Server=127.0.0.1,1433;Database=TaajLoad;User Id=sa;Password=LoadT3st!Pass_2026;TrustServerCertificate=True");
    var opts = new DbContextOptionsBuilder<LabStageDbContext>().UseSqlServer(cs).Options;
    var hash = PasswordHasher.Hash(Password); // one hash reused: seeding speed only, sign-in still verifies it fully
    var sw = Stopwatch.StartNew();
    const int batch = 250;
    for (var start = 0; start < N; start += batch)
    {
        await using var db = new LabStageDbContext(opts);
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        for (var i = start; i < Math.Min(N, start + batch); i++)
        {
            var doc = new AppUser { Email = $"d{i}@load.test", NormalizedEmail = $"D{i}@LOAD.TEST", PasswordHash = hash, FullName = $"د. تجربة {i}", Phone = "01000000000", Role = AppRoles.Dentist, EmailConfirmedAt = DateTimeOffset.UtcNow };
            var labUser = new AppUser { Email = $"l{i}@load.test", NormalizedEmail = $"L{i}@LOAD.TEST", PasswordHash = hash, FullName = $"معمل تجربة {i}", Phone = "01000000000", Role = AppRoles.LaboratoryOwner, EmailConfirmedAt = DateTimeOffset.UtcNow };
            db.Users.AddRange(doc, labUser);
            db.DoctorProfiles.Add(new DoctorProfile { UserId = doc.Id, ClinicName = $"عيادة {i}", Governorate = "القاهرة", City = "مدينة نصر", Address = "شارع التجربة" });
            var clinic = new Clinic { DoctorUserId = doc.Id, Name = $"عيادة {i}", Address = "شارع التجربة", City = "مدينة نصر", Governorate = "القاهرة", IsDefault = true };
            db.Clinics.Add(clinic);
            var lab = new Laboratory { OwnerUserId = labUser.Id, Slug = $"load-lab-{i}", Name = $"معمل تجربة {i}", Governorate = "القاهرة", City = "المعادي", Address = "شارع المعمل", VerificationStatus = VerificationStatus.Verified };
            lab.Services.Add(new LaboratoryService { LaboratoryId = lab.Id, Name = "Zirconia Crown", PricePiastres = 150_000 });
            lab.Subscriptions.Add(new LaboratorySubscription { LaboratoryId = lab.Id, Status = SubscriptionStatus.Active, IsTrial = false, EndsAt = DateTimeOffset.UtcNow.AddYears(1) });
            db.Laboratories.Add(lab);
            // One open case per pair (doctor i <-> lab i), used by the chat and upload scenarios.
            var order = new Order
            {
                OrderNumber = $"LT-{i:D6}", DentistUserId = doc.Id, LaboratoryId = lab.Id, CaseReference = $"مريض {i}", PatientName = $"مريض {i}",
                ServiceName = "Zirconia Crown", ToothNumbers = "11", EstimatedPricePiastres = 150_000, ClinicId = clinic.Id, Status = OrderStatus.PendingLabReview,
                Stages = [new OrderStage { Stage = ProductionStage.Design, RequiresDentistReview = true }, new OrderStage { Stage = ProductionStage.Milling }],
            };
            db.Orders.Add(order);
            db.Conversations.Add(new Conversation { OrderId = order.Id, DentistUserId = doc.Id, LaboratoryId = lab.Id });
        }
        db.ChangeTracker.DetectChanges();
        await db.SaveChangesAsync();
        if ((start / batch) % 4 == 0) Console.WriteLine($"seeded {Math.Min(N, start + batch)} pairs, {sw.Elapsed.TotalSeconds:F0}s");
    }
    Console.WriteLine($"Seed done: {N} doctors + {N} labs + {N} orders in {sw.Elapsed.TotalSeconds:F1}s");
}

// ---------------------------------------------------------------- shared helpers
HttpClient NewClient() => new(new SocketsHttpHandler
{
    UseCookies = false,
    MaxConnectionsPerServer = maxConn,
    ConnectTimeout = TimeSpan.FromSeconds(60),
    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
    AutomaticDecompression = DecompressionMethods.None,
}) { Timeout = TimeSpan.FromSeconds(Arg("timeout", "180") is var t ? int.Parse(t) : 180) };

static string Ip(int idx) => $"10.{(idx >> 16) & 255}.{(idx >> 8) & 255}.{idx & 255}";

HttpRequestMessage Req(HttpMethod m, string path, LoadUser? u, HttpContent? body = null)
{
    var r = new HttpRequestMessage(m, baseUrl + path) { Content = body };
    r.Headers.TryAddWithoutValidation("Origin", "http://localhost:5080");
    if (u is not null)
    {
        r.Headers.TryAddWithoutValidation("X-Forwarded-For", Ip(u.Idx + 1));
        if (u.Cookie is not null) r.Headers.TryAddWithoutValidation("Cookie", u.Cookie);
    }
    return r;
}

List<LoadUser> Users()
{
    var list = new List<LoadUser>();
    for (var i = 0; i < N; i++) list.Add(new LoadUser($"d{i}@load.test", "Dentist", i, i));
    for (var i = 0; i < N; i++) list.Add(new LoadUser($"l{i}@load.test", "Lab", N + i, i));
    return list.Where(u => u.Pair % Shards == Shard).ToList();
}

List<LoadUser> LoadCookies()
{
    var path = Path.Combine(resultsDir, "cookies.json");
    var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
    var users = Users();
    foreach (var u in users) u.Cookie = map.GetValueOrDefault(u.Email);
    var missing = users.Count(u => u.Cookie is null);
    if (missing > 0) Console.WriteLine($"WARNING: {missing} users have no session cookie");
    return users;
}

static string ErrKey(Exception e) => e switch
{
    TaskCanceledException => "timeout",
    HttpRequestException h => "http:" + (h.HttpRequestError.ToString()),
    WebSocketException w => "ws:" + w.WebSocketErrorCode,
    _ => e.GetType().Name,
};

async Task<(double ms, int status, string? body)> Timed(HttpClient c, Func<HttpRequestMessage> make, bool readBody = true)
{
    var sw = Stopwatch.StartNew();
    using var resp = await c.SendAsync(make());
    var body = readBody || !resp.IsSuccessStatusCode ? await resp.Content.ReadAsStringAsync() : null;
    return (sw.Elapsed.TotalMilliseconds, (int)resp.StatusCode, body);
}

// ---------------------------------------------------------------- login (all 10,000 users)
async Task Login()
{
    var conc = int.Parse(Arg("concurrency", "100"));
    var users = Users();
    var c = NewClient();
    var stats = new Stats($"Sign-in of {users.Count} users ({conc} at a time)");
    var cookies = new ConcurrentDictionary<string, string>();
    var sem = new SemaphoreSlim(conc);
    var sw = Stopwatch.StartNew();
    var done = 0;
    await Task.WhenAll(users.Select(async u =>
    {
        await sem.WaitAsync();
        try
        {
            var t = Stopwatch.StartNew();
            using var resp = await c.SendAsync(Req(HttpMethod.Post, "/api/v1/auth/login", u,
                JsonContent(new { email = u.Email, password = Password, rememberMe = true })));
            var ms = t.Elapsed.TotalMilliseconds;
            if (resp.IsSuccessStatusCode && resp.Headers.TryGetValues("Set-Cookie", out var sc))
            {
                cookies[u.Email] = string.Join("; ", sc.Select(x => x.Split(';')[0]).Where(x => x.StartsWith("LabStage.Auth")));
                stats.Ok(ms);
            }
            else stats.Fail(ms, ((int)resp.StatusCode).ToString());
        }
        catch (Exception e) { stats.Fail(0, ErrKey(e)); }
        finally { sem.Release(); if (Interlocked.Increment(ref done) % 1000 == 0) Console.WriteLine($"  {done} signed in, {sw.Elapsed.TotalSeconds:F0}s"); }
    }));
    stats.Wall = sw.Elapsed.TotalSeconds;
    File.WriteAllText(Path.Combine(resultsDir, "cookies.json"), JsonSerializer.Serialize(cookies));
    Save("login", [stats]);
}

StringContent JsonContent(object o) => new(JsonSerializer.Serialize(o, json), Encoding.UTF8, "application/json");

void Save(string name, List<Stats> list, object? extra = null)
{
    foreach (var s in list) Console.WriteLine(s.Summary());
    File.WriteAllText(Path.Combine(resultsDir, name + ".json"), JsonSerializer.Serialize(new { scenario = name, at = DateTimeOffset.UtcNow, stats = list.Select(s => s.ToObj()), raw = list.Select(s => s.Raw()), extra }, new JsonSerializerOptions { WriteIndented = true }));
}

// ---------------------------------------------------------------- S1: 1,000 doctors create a case at the same moment
async Task S1()
{
    var count = int.Parse(Arg("doctors", "1000"));
    var users = LoadCookies().Where(u => u.Role == "Dentist").Take(count).ToList();
    var c = NewClient();
    // Warm-up: every doctor has the site open (one request each), so connections already exist, like real browsers.
    var warm = new Stats("Warm-up: open dashboard (GET /api/v1/orders)");
    var swW = Stopwatch.StartNew();
    await Task.WhenAll(users.Select(async u =>
    {
        try { var r = await Timed(c, () => Req(HttpMethod.Get, "/api/v1/orders", u), false); if (r.status == 200) warm.Ok(r.ms); else warm.Fail(r.ms, r.status.ToString()); }
        catch (Exception e) { warm.Fail(0, ErrKey(e)); }
    }));
    warm.Wall = swW.Elapsed.TotalSeconds;
    await Task.Delay(2000);

    var stats = new Stats($"{count} doctors create a new case at the same moment (POST /api/v1/orders)");
    var gate = new TaskCompletionSource();
    var errBodies = new ConcurrentBag<string>();
    var created = new ConcurrentBag<string>();
    var round = Arg("tag", "1");
    var tasks = users.Select(async u =>
    {
        await gate.Task;
        try
        {
            var r = await Timed(c, () => Req(HttpMethod.Post, "/api/v1/orders", u, JsonContent(new
            {
                laboratorySlug = $"load-lab-{(u.Pair + 1) % N}", caseReference = $"حالة تحميل {round}-{u.Pair}", serviceName = "Zirconia Crown",
                toothNumbers = "11,12", shade = "A2", instructions = "اختبار تحمل", stages = new[] { 1, 2, 3, 4 }, estimatedPriceEgp = 0,
                deliveryMethod = 0, pickupAddress = (string?)null, requiredDeliveryDate = (DateTimeOffset?)null, patientName = $"مريض {u.Pair}",
            })));
            if (r.status == 200) { stats.Ok(r.ms); created.Add(JsonDocument.Parse(r.body!).RootElement.GetProperty("orderNumber").GetString()!); }
            else { stats.Fail(r.ms, r.status.ToString()); if (errBodies.Count < 5) errBodies.Add($"{r.status}: {r.body?[..Math.Min(300, r.body.Length)]}"); }
        }
        catch (Exception e) { stats.Fail(0, ErrKey(e)); }
    }).ToList();
    var sw = Stopwatch.StartNew();
    gate.SetResult();
    await Task.WhenAll(tasks);
    stats.Wall = sw.Elapsed.TotalSeconds;
    var dupes = created.GroupBy(x => x).Count(g => g.Count() > 1);
    Save($"s1{Arg("suffix", "")}", [warm, stats], new { sampleErrors = errBodies.ToArray(), created = created.Count, duplicateOrderNumbers = dupes });
}

// ---------------------------------------------------------------- S2: 5,000 doctors chat with 5,000 labs
async Task S2()
{
    var users = LoadCookies();
    var rounds = int.Parse(Arg("rounds", "5"));
    var steadySeconds = int.Parse(Arg("steady", "120"));
    var steadyInterval = double.Parse(Arg("interval", "20")); // average seconds between messages per user in the steady phase
    var connConc = int.Parse(Arg("connconcurrency", "200"));
    var c = NewClient();
    var connStats = new Stats($"Open live chat connection (SignalR negotiate + WebSocket + join case) x {users.Count}");
    var conns = new ConcurrentDictionary<int, HubConn>();
    var deliverD2L = new Stats("Delivery time doctor -> lab (send click until it appears at the lab)");
    var deliverL2D = new Stats("Delivery time lab -> doctor (reply appears at the doctor)");
    var sendBurst = new Stats($"Send message during burst rounds (POST /messages), {N} doctors at once + {N} lab replies");
    var sendSteady = new Stats("Send message during steady chatting (POST /messages)");
    var openChat = new Stats("Open case chat history (GET /messages)");
    var phase = "burst";
    var replied = new ConcurrentDictionary<(int, int), bool>();
    var gotReply = new ConcurrentDictionary<(int, int), bool>();
    var expected = new ConcurrentDictionary<string, long>(); // msg tag -> sent ticks, removed on delivery
    LoadUser ByIdx(int idx) => users[idx];

    async Task SendMsg(LoadUser u, string tag, Stats st)
    {
        try
        {
            expected[tag] = Stopwatch.GetTimestamp();
            var r = await Timed(c, () => Req(HttpMethod.Post, $"/api/v1/orders/LT-{u.Pair:D6}/messages", u, JsonContent(new { body = $"{tag}|رسالة تجربة من {(u.Role == "Dentist" ? "الطبيب" : "المعمل")}" })), false);
            if (r.status == 200) st.Ok(r.ms); else { st.Fail(r.ms, r.status.ToString()); expected.TryRemove(tag, out _); }
        }
        catch (Exception e) { st.Fail(0, ErrKey(e)); expected.TryRemove(tag, out _); }
    }

    void OnChat(HubConn hc, string body)
    {
        // body = "T|round|senderIdx|seq|text"
        var p = body.Split('|');
        if (p.Length < 4 || p[0] != "T") return;
        var senderIdx = int.Parse(p[2]);
        if (senderIdx == hc.User.Idx) return; // own echo
        var tag = string.Join('|', p[..4]);
        if (expected.TryRemove(tag, out var t0))
        {
            var ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            (hc.User.Role == "Lab" ? deliverD2L : deliverL2D).Ok(ms);
        }
        var round = int.Parse(p[1]);
        if (round > 0 && hc.User.Role == "Lab" && replied.TryAdd((hc.User.Pair, round), true))
            _ = SendMsg(hc.User, $"T|{round}|{hc.User.Idx}|r", sendBurst);
        if (round > 0 && hc.User.Role == "Dentist") gotReply.TryAdd((hc.User.Pair, round), true);
    }

    // 1) everyone opens the case page: live connection + join the case group
    Console.WriteLine($"Connecting {users.Count} live chat connections...");
    var sem = new SemaphoreSlim(connConc);
    var swC = Stopwatch.StartNew();
    await Task.WhenAll(users.Select(async u =>
    {
        await sem.WaitAsync();
        try
        {
            var t = Stopwatch.StartNew();
            var hc = await HubConn.ConnectAsync(c, baseUrl, u, Ip(u.Idx + 1), $"LT-{u.Pair:D6}", OnChat);
            connStats.Ok(t.Elapsed.TotalMilliseconds);
            conns[u.Idx] = hc;
        }
        catch (Exception e) { connStats.Fail(0, ErrKey(e) is var k && k == "Exception" ? e.Message[..Math.Min(60, e.Message.Length)] : ErrKey(e)); }
        finally { sem.Release(); }
    }));
    connStats.Wall = swC.Elapsed.TotalSeconds;
    Console.WriteLine(connStats.Summary());
    using var pinger = new CancellationTokenSource();
    _ = Task.Run(async () =>
    {
        while (!pinger.IsCancellationRequested)
        {
            await Task.Delay(10_000);
            foreach (var hc in conns.Values) _ = hc.PingAsync();
        }
    });

    // opening the chat tab loads the history (all users, at once) — all shards start at t0
    await Until(t0);
    var swO = Stopwatch.StartNew();
    await Task.WhenAll(users.Select(async u =>
    {
        try { var r = await Timed(c, () => Req(HttpMethod.Get, $"/api/v1/orders/LT-{u.Pair:D6}/messages", u), false); if (r.status == 200) openChat.Ok(r.ms); else openChat.Fail(r.ms, r.status.ToString()); }
        catch (Exception e) { openChat.Fail(0, ErrKey(e)); }
    }));
    openChat.Wall = swO.Elapsed.TotalSeconds;
    Console.WriteLine(openChat.Summary());

    // 2) burst rounds: all doctors send at the same moment, each lab answers as soon as the message arrives
    var roundInfo = new List<object>();
    var swB = Stopwatch.StartNew();
    for (var r = 1; r <= rounds; r++)
    {
        await Until(t0.AddSeconds(60 * r));
        var doctors = users.Where(u => u.Role == "Dentist" && conns.ContainsKey(u.Idx)).ToList();
        var t = Stopwatch.StartNew();
        await Task.WhenAll(doctors.Select(u => SendMsg(u, $"T|{r}|{u.Idx}|q", sendBurst)));
        var sentAt = t.Elapsed.TotalSeconds;
        var deadline = DateTime.UtcNow.AddSeconds(90);
        deadline = t0.AddSeconds(60 * r + 50).UtcDateTime;
        while (gotReply.Keys.Count(k => k.Item2 == r) < doctors.Count && DateTime.UtcNow < deadline) await Task.Delay(250);
        var completed = gotReply.Keys.Count(k => k.Item2 == r);
        roundInfo.Add(new { round = r, doctors = doctors.Count, allSentSec = Math.Round(sentAt, 2), conversationsCompleted = completed, roundTripSec = Math.Round(t.Elapsed.TotalSeconds, 2) });
        Console.WriteLine($"  round {r}: all {doctors.Count} sent in {sentAt:F1}s, {completed} replies back in {t.Elapsed.TotalSeconds:F1}s");
    }
    sendBurst.Wall = swB.Elapsed.TotalSeconds;
    var lostBurst = expected.Count; expected.Clear();

    // 3) steady chatting: every user sends a message every ~interval seconds (random), for steadySeconds
    phase = "steady";
    var swS = Stopwatch.StartNew();
    var seq = 0;
    if (steadySeconds > 0)
    {
        await Until(t0.AddSeconds(60 * (rounds + 1)));
        swS.Restart();
        var end = DateTime.UtcNow.AddSeconds(steadySeconds);
        await Task.WhenAll(users.Where(u => conns.ContainsKey(u.Idx)).Select(async u =>
        {
            var rnd = new Random(u.Idx);
            await Task.Delay(rnd.Next((int)(steadyInterval * 1000)));
            while (DateTime.UtcNow < end)
            {
                await SendMsg(u, $"T|0|{u.Idx}|{Interlocked.Increment(ref seq)}", sendSteady);
                await Task.Delay(TimeSpan.FromSeconds(steadyInterval * (0.5 + rnd.NextDouble())));
            }
        }));
        await Task.Delay(5000);
    }
    sendSteady.Wall = swS.Elapsed.TotalSeconds;
    var lostSteady = expected.Count;
    pinger.Cancel();
    var alive = conns.Values.Count(h => h.Ws.State == WebSocketState.Open);
    Save(Shards > 1 ? $"s2-shard{Shard}" : "s2", [connStats, openChat, sendBurst, sendSteady, deliverD2L, deliverL2D],
        new { rounds = roundInfo, notDeliveredBurst = lostBurst, notDeliveredSteady = lostSteady, connectionsStillOpenAtEnd = alive, connected = conns.Count, steadyIntervalSec = steadyInterval });
    foreach (var hc in conns.Values) { try { hc.Ws.Abort(); } catch { } }
}

// ---------------------------------------------------------------- S3: 10,000 users upload a file in the chat at the same moment
async Task S3()
{
    var users = LoadCookies();
    var take = int.Parse(Arg("users", users.Count.ToString()));
    users = users.Take(take / 2).Concat(users.Skip(N).Take(take - take / 2)).ToList();
    var sizeKb = int.Parse(Arg("kb", "1024"));
    var payload = new byte[sizeKb * 1024];
    new Random(1).NextBytes(payload);
    payload[0] = 0xFF; payload[1] = 0xD8; payload[2] = 0xFF; payload[3] = 0xE0; // JPEG signature
    var c = NewClient();
    var warm = new Stats("Warm-up: open case page (GET /api/v1/orders/{n})");
    await Task.WhenAll(users.Select(async u =>
    {
        try { var r = await Timed(c, () => Req(HttpMethod.Get, $"/api/v1/orders/LT-{u.Pair:D6}", u), false); if (r.status == 200) warm.Ok(r.ms); else warm.Fail(r.ms, r.status.ToString()); }
        catch (Exception e) { warm.Fail(0, ErrKey(e)); }
    }));
    await Task.Delay(2000);
    var up = new Stats($"Upload {sizeKb} KB file (POST /orders/{{n}}/files), {users.Count} users at the same moment");
    var msg = new Stats("Post chat message with the attachment (POST /messages with fileId)");
    var total = new Stats("Total: upload + message (what the user waits for)");
    var errBodies = new ConcurrentBag<string>();
    var gate = new TaskCompletionSource();
    var tasks = users.Select(async u =>
    {
        await gate.Task;
        var t = Stopwatch.StartNew();
        try
        {
            var form = new MultipartFormDataContent();
            var fc = new ByteArrayContent(payload);
            fc.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(fc, "file", $"scan-{u.Idx}.jpg");
            var r = await Timed(c, () => Req(HttpMethod.Post, $"/api/v1/orders/LT-{u.Pair:D6}/files", u, form));
            if (r.status != 200) { up.Fail(r.ms, r.status.ToString()); total.Fail(t.Elapsed.TotalMilliseconds, "upload " + r.status); if (errBodies.Count < 5) errBodies.Add($"{r.status}: {r.body?[..Math.Min(300, r.body?.Length ?? 0)]}"); return; }
            up.Ok(r.ms);
            var fileId = JsonDocument.Parse(r.body!).RootElement.GetProperty("id").GetGuid();
            var m = await Timed(c, () => Req(HttpMethod.Post, $"/api/v1/orders/LT-{u.Pair:D6}/messages", u, JsonContent(new { body = "", fileId })), false);
            if (m.status == 200) { msg.Ok(m.ms); total.Ok(t.Elapsed.TotalMilliseconds); }
            else { msg.Fail(m.ms, m.status.ToString()); total.Fail(t.Elapsed.TotalMilliseconds, "message " + m.status); }
        }
        catch (Exception e) { up.Fail(t.Elapsed.TotalMilliseconds, ErrKey(e)); total.Fail(t.Elapsed.TotalMilliseconds, ErrKey(e)); if (errBodies.Count < 5) errBodies.Add(e.Message); }
    }).ToList();
    var sw = Stopwatch.StartNew();
    gate.SetResult();
    await Task.WhenAll(tasks);
    up.Wall = msg.Wall = total.Wall = sw.Elapsed.TotalSeconds;
    Save($"s3{Arg("suffix", "")}", [warm, up, msg, total], new { sizeKb, users = users.Count, sampleErrors = errBodies.ToArray(), megabytesUploaded = up.OkCount * sizeKb / 1024.0 });
}

void Merge()
{
    var name = Arg("name", "s2");
    var files = Directory.GetFiles(resultsDir, name + "-shard*.json").OrderBy(x => x).ToList();
    var merged = new List<Stats>();
    var extras = new List<JsonElement>();
    foreach (var f in files)
    {
        var doc = JsonDocument.Parse(File.ReadAllText(f)).RootElement;
        extras.Add(doc.GetProperty("extra").Clone());
        var i = 0;
        foreach (var r in doc.GetProperty("raw").EnumerateArray())
        {
            var st = Stats.FromRaw(r);
            if (merged.Count <= i) merged.Add(new Stats(st.Name));
            merged[i].Absorb(st); i++;
        }
    }
    Save(name, merged, new { shards = files.Count, perShard = extras });
}

// ---------------------------------------------------------------- types
sealed class LoadUser(string email, string role, int idx, int pair)
{
    public string Email { get; } = email;
    public string Role { get; } = role;
    public int Idx { get; } = idx;
    public int Pair { get; } = pair;
    public string? Cookie { get; set; }
}

sealed class Stats(string name)
{
    public string Name { get; } = name;
    private readonly ConcurrentBag<double> _ok = [];
    private readonly ConcurrentBag<double> _all = [];
    private readonly ConcurrentDictionary<string, int> _errors = new();
    public double Wall { get; set; }
    public int OkCount => _ok.Count;
    public void Ok(double ms) { _ok.Add(ms); _all.Add(ms); }
    public void Fail(double ms, string key) { _errors.AddOrUpdate(key, 1, (_, v) => v + 1); if (ms > 0) _all.Add(ms); }
    private static double P(double[] s, double p) => s.Length == 0 ? 0 : s[Math.Min(s.Length - 1, (int)Math.Ceiling(p / 100 * s.Length) - 1)];
    public object ToObj()
    {
        var s = _ok.OrderBy(x => x).ToArray();
        var errs = _errors.Values.Sum();
        return new
        {
            name = Name, total = s.Length + errs, ok = s.Length, errors = errs, errorRatePct = s.Length + errs == 0 ? 0 : Math.Round(100.0 * errs / (s.Length + errs), 2),
            errorBreakdown = _errors, p50 = Math.Round(P(s, 50)), p95 = Math.Round(P(s, 95)), p99 = Math.Round(P(s, 99)), max = Math.Round(s.LastOrDefault()),
            mean = s.Length == 0 ? 0 : Math.Round(s.Average()), wallSec = Math.Round(Wall, 2), throughputPerSec = Wall > 0 ? Math.Round(s.Length / Wall, 1) : 0,
        };
    }
    public object Raw() => new { name = Name, ok = _ok.ToArray(), errors = _errors, wall = Wall };
    public static Stats FromRaw(JsonElement r)
    {
        var st = new Stats(r.GetProperty("name").GetString()!) { Wall = r.GetProperty("wall").GetDouble() };
        foreach (var v in r.GetProperty("ok").EnumerateArray()) st._ok.Add(v.GetDouble());
        foreach (var e in r.GetProperty("errors").EnumerateObject()) st._errors[e.Name] = e.Value.GetInt32();
        return st;
    }
    public void Absorb(Stats o) { foreach (var v in o._ok) _ok.Add(v); foreach (var (k, v) in o._errors) _errors.AddOrUpdate(k, v, (_, x) => x + v); Wall = Math.Max(Wall, o.Wall); }
    public string Summary() => JsonSerializer.Serialize(ToObj());
}

/// <summary>Minimal SignalR JSON-protocol client over a raw WebSocket (cheap enough to hold 10,000 at once).</summary>
sealed class HubConn
{
    public required ClientWebSocket Ws { get; init; }
    public required LoadUser User { get; init; }
    private readonly SemaphoreSlim _send = new(1, 1);
    private const char RS = '\u001e';

    public static async Task<HubConn> ConnectAsync(HttpClient http, string baseUrl, LoadUser u, string ip, string orderNumber, Action<HubConn, string> onChat)
    {
        using var neg = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/hubs/orders/negotiate?negotiateVersion=1");
        neg.Headers.TryAddWithoutValidation("Cookie", u.Cookie);
        neg.Headers.TryAddWithoutValidation("X-Forwarded-For", ip);
        neg.Headers.TryAddWithoutValidation("Origin", "http://localhost:5080");
        using var nr = await http.SendAsync(neg);
        if (!nr.IsSuccessStatusCode) throw new Exception("negotiate " + (int)nr.StatusCode);
        var token = JsonDocument.Parse(await nr.Content.ReadAsStringAsync()).RootElement.GetProperty("connectionToken").GetString();
        var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Cookie", u.Cookie);
        ws.Options.SetRequestHeader("X-Forwarded-For", ip);
        ws.Options.SetRequestHeader("Origin", "http://localhost:5080");
        ws.Options.KeepAliveInterval = TimeSpan.Zero;
        await ws.ConnectAsync(new Uri($"{baseUrl.Replace("http", "ws")}/hubs/orders?id={token}"), CancellationToken.None);
        var hc = new HubConn { Ws = ws, User = u };
        await hc.SendRaw("{\"protocol\":\"json\",\"version\":1}" + RS);
        var joined = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = hc.ReceiveLoop(onChat, joined);
        await hc.SendRaw("{\"type\":1,\"invocationId\":\"1\",\"target\":\"JoinOrderGroup\",\"arguments\":[\"" + orderNumber + "\"]}" + RS);
        var done = await Task.WhenAny(joined.Task, Task.Delay(60_000));
        if (done != joined.Task) throw new Exception("join timeout");
        if (joined.Task.Result is { } err) throw new Exception("join: " + err);
        return hc;
    }

    public Task PingAsync() => Ws.State == WebSocketState.Open ? SendRaw("{\"type\":6}" + RS) : Task.CompletedTask;

    private async Task SendRaw(string s)
    {
        await _send.WaitAsync();
        try { await Ws.SendAsync(Encoding.UTF8.GetBytes(s), WebSocketMessageType.Text, true, CancellationToken.None); }
        catch { }
        finally { _send.Release(); }
    }

    private async Task ReceiveLoop(Action<HubConn, string> onChat, TaskCompletionSource<string?> joined)
    {
        var buf = new byte[16 * 1024];
        var sb = new StringBuilder();
        try
        {
            while (Ws.State == WebSocketState.Open)
            {
                var r = await Ws.ReceiveAsync(buf, CancellationToken.None);
                if (r.MessageType == WebSocketMessageType.Close) break;
                sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                if (!r.EndOfMessage) continue;
                var all = sb.ToString(); sb.Clear();
                foreach (var frame in all.Split(RS, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (frame == "{}") continue;
                    using var d = JsonDocument.Parse(frame);
                    var type = d.RootElement.GetProperty("type").GetInt32();
                    if (type == 3) joined.TrySetResult(d.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null);
                    else if (type == 1 && d.RootElement.GetProperty("target").GetString() == "chatMessage")
                    {
                        var body = d.RootElement.GetProperty("arguments")[0].GetProperty("body").GetString() ?? "";
                        onChat(this, body);
                    }
                    else if (type == 7) break; // server closed
                }
            }
        }
        catch { }
        joined.TrySetResult("closed");
    }
}
