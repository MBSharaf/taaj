# Taaj (تاج) — LabStage dental lab platform

منصة مصرية تربط أطباء الأسنان بمعامل الأسنان: يقارن الطبيب المعامل وأسعارها، يرسل الحالة مع ملفات المسح،
ويتابع مراحل التصنيع (التصميم، التفريز، الطبقات، الجليز) مع دفع على دفعتين (20% بعد اعتماد التصميم و80% عند الإتمام).

An Egyptian platform that connects dentists with dental labs: compare labs and prices, send a case with scan files,
follow every production stage (design, milling, layering, glazing), and pay in two milestones (20% after design
approval, 80% on completion). Arabic (RTL) and English, light and dark mode.

## Stack
- **Backend:** ASP.NET Core (.NET 10), Entity Framework Core, SQL Server (LocalDB for development), SignalR.
- **Frontend:** static HTML/CSS/JS served from `src/LabStage.Api/wwwroot` (no build step).
  three.js, GSAP and Lenis are vendored under `wwwroot/assets/vendor`.
- **Layers:** `LabStage.Domain`, `LabStage.Application`, `LabStage.Infrastructure`, `LabStage.Api`; tests in `tests/LabStage.Tests`.

## Run locally · التشغيل محليًا
Requirements: .NET 10 SDK and SQL Server LocalDB (comes with Visual Studio).

```bash
dotnet build
cd src/LabStage.Api
dotnet run --urls http://localhost:5080
```

Open http://localhost:5080. In the Development environment the database is created automatically and filled with
demo labs, doctors and orders (demo data is never seeded in other environments).

```bash
dotnet test
```

## Configuration · الإعدادات
No secrets are stored in this repository. Development works with no configuration at all.
Every other environment **must** provide these as environment variables (or a secret store);
the app refuses to start without them.

| Setting | Environment variable | Notes |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | SQL Server connection string. Development falls back to LocalDB. |
| `Payments:WebhookSecret` | `Payments__WebhookSecret` | At least 32 characters. Development generates a throwaway value. |
| `Bootstrap:AdminEmail` / `Bootstrap:AdminPassword` | `Bootstrap__AdminEmail` / `Bootstrap__AdminPassword` | Creates the first admin account on an empty database. |
| `Maps:GoogleMapsApiKey` | `Maps__GoogleMapsApiKey` | Optional, for maps on lab pages. |

For local overrides use `dotnet user-secrets` in `src/LabStage.Api` (never commit them).

## Hosting note
GitHub stores the code only. The site needs a host that runs ASP.NET Core with a SQL Server database
(for example Azure App Service + Azure SQL, or a Windows/Linux VPS). GitHub Pages cannot run it.
