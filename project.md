# LabStage Project Documentation

## Overview
LabStage is a **full‑stack web platform** for dental labs and dentists in Egypt. It provides:
- A marketplace for dentists to discover verified dental labs.
- End‑to‑end case management (design, milling, layering, glazing).
- Two‑milestone payment flow (20 % after design approval, 80 % on delivery).
- Individual dashboards for **Dentists**, **Labs**, and **Admins**.
- Bilingual UI (Arabic ↔ English) with a **dark‑mode** toggle.
- A unified **price‑list** template for all labs.
- Commission model: **1 % service fee for dentists**, **2 % platform fee for labs**.

---

## Tech Stack
| Layer | Technology |
|-------|------------|
| **Backend** | ASP.NET Core 7, Entity Framework Core, SQL Server (or SQLite for dev) |
| **Frontend** | Static HTML/CSS/JS, custom i18n engine (`assets/js/core/i18n.js`), Chart.js for dashboards |
| **Build / Run** | `dotnet run` (Kestrel) on `http://localhost:5080` |
| **Version Control** | Git (workspace at `d:/Dental System`) |
| **Other** | Dark‑mode via CSS variables, HTTP‑only API endpoints for data fetching |

---

## Directory Structure (key folders/files)
```
Dental System/
├─ src/
│  └─ LabStage.Api/
│     ├─ LabStage.Api.csproj
│     ├─ Program.cs               # Kestrel host, service configuration
│     ├─ wwwroot/                 # Static assets served by the API
│     │   ├─ index.html
│     │   ├─ doctor-dashboard.html
│     │   ├─ lab-dashboard.html
│     │   ├─ assets/
│     │   │   ├─ css/            # tokens.css, base.css, dashboard.css, home.css
│     │   │   └─ js/
│     │   │       ├─ core/
│     │   │       │   └─ i18n.js   # Arabic‑>English map + theme handling
│     │   │       └─ pages/
│     │   │           └─ dashboard.js  # Dashboard logic, API calls
│     │   └─ images/…
│     └─ Infrastructure/          # EF Core migrations, DB context
└─ README.md
```

---

## Core Backend Files (excerpt)
### `Program.cs`
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
var app = builder.Build();
app.UseStaticFiles();
app.MapControllers();
app.Run();
```
*(full file located at `d:/Dental System/src/LabStage.Api/Program.cs`)*

### `i18n.js` (translation map)
The file contains the `EXACT_AR_TO_EN` dictionary used throughout the site. Important entries for dashboards are:
```js
const EXACT_AR_TO_EN = {
  // Dashboard common
  'بوابة الطبيب': 'Dentist Portal',
  'بوابة المعمل': 'Lab Portal',
  'إدارة المنصة — مصر': 'Platform Admin — Egypt',
  'إجمالي الطلبات': 'Total Orders',
  'قيد التصنيع أو المراجعة': 'In Production / Review',
  'المدفوع حتى الآن': 'Paid So Far',
  // Doctor dashboard navigation
  'لوحة المتابعة': 'Dashboard',
  'الطلبات وإنشاء حالة': 'Orders & New Case',
  'المدفوعات والفواتيل': 'Payments & Invoices',
  // Lab dashboard navigation
  'لوحة المعمل والإحصائيات': 'Lab Overview & Stats',
  'الطلبات ومراحل الإنتاج': 'Orders & Production Stages',
  'الأطباء وكشف حساب كل طبيب': 'Dentists & Individual Statements',
  'المحفظة وسحب الأرباح': 'Wallet & Withdrawals',
  // ... many more entries (see file)
};
```
*(full file at `d:/Dental System/src/LabStage.Api/wwwroot/assets/js/core/i18n.js`)*

---

## Front‑end Highlights
- **Language Toggle** – `[data-pref-lang]` button stores preference in `localStorage` (`labstage_lang`).
- **Dark‑Mode** – theme stored in `labstage_theme`, applied via CSS variables.
- **Dashboard Charts** – rendered with Chart.js, data fetched from API endpoints (`/api/...`).
- **Price‑List Editor** – interactive form in `lab-dashboard.html` allowing labs to add/edit services.

---

## Database (EF Core) Entities (simplified)
```csharp
public class Order {
    public int Id { get; set; }
    public string OrderNumber { get; set; }
    public Lab Lab { get; set; }
    public Dentist Dentist { get; set; }
    public Stage CurrentStage { get; set; }
    public decimal Paid { get; set; }
    public decimal Due { get; set; }
    // … other fields
}

public class Lab {
    public int Id { get; set; }
    public string Name { get; set; }
    public decimal CommissionRate { get; set; } = 0.02m; // 2 %
    // …
}

public class Dentist {
    public int Id { get; set; }
    public string Name { get; set; }
    public decimal ServiceFeeRate { get; set; } = 0.01m; // 1 %
    // …
}
```
*(Migrations live in `src/LabStage.Infrastructure`.)

---

## Architecture Diagram
```mermaid
flowchart TD
    subgraph Frontend
        A[HTML / CSS / JS] --> B[i18n Engine]
        B --> C[Dashboard UI]
        A --> D[Language & Theme Toggles]
    end
    subgraph Backend
        E[ASP.NET Core API] --> F[Controllers]
        F --> G[Entity Framework Core]
        G --> H[SQL Database]
        E --> I[Static Files (wwwroot)]
    end
    D --> E   %% UI calls API for data
    C --> E   %% Dashboard charts request data
    I --> E   %% Kestrel serves static assets
    style Frontend fill:#f9f9f9,stroke:#333,stroke-width:2px
    style Backend fill:#e6f7ff,stroke:#333,stroke-width:2px
```

---

## Project Cost Estimation (high‑level, for reference)
| Item | Approx. Cost (USD) |
|------|-------------------|
| Development (Full‑stack) | $12,000 – $18,000 |
| UI/UX design (branding, dark‑mode) | $3,000 – $5,000 |
| Hosting (Azure App Service, 1 M requests/mo) | $150 / month |
| SSL & domain | $80 / year |
| Ongoing maintenance (bug fixes, feature updates) | $1,000 / month |
| **Total first‑year estimate** | **≈ $20 k** |

---

## How to Run the Project Locally
```powershell
# Navigate to the API folder
cd "d:\Dental System\src\LabStage.Api"
# Restore packages & run
dotnet run --urls http://localhost:5080 --no-launch-profile --environment Development
```
Open a browser and go to `http://localhost:5080`. Use the language toggle (top‑right) to switch between Arabic and English.

---

## Next Steps / Open Tasks
- Add any missing translation entries (see `missing_i18n_translations.md`).
- Implement API endpoints for the new commission rates (lab 2 %, dentist 1 %).
- Write unit tests for the payment calculation service.
- Consider adding CI/CD pipelines (GitHub Actions → Azure). 

---

*All file paths referenced above are absolute and can be opened directly from this markdown.*