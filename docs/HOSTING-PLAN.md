# خطة استضافة تاج (Taaj) — الموقع الكامل

> النسخة الموجودة على GitHub Pages (https://mbsharaf.github.io/taaj/) نسخة عرض للواجهة فقط:
> بدون تسجيل دخول ولا بيانات حية، لأن GitHub Pages لا يشغّل الباك إند.
> الموقع الكامل يحتاج سيرفر يشغّل ASP.NET Core (.NET 10) وقاعدة بيانات SQL Server.

## الشرط قبل أي نشر حقيقي
لازم تخلص **المرحلة الأولى (الأمان)** من خطة الباك إند الأول (ملف داخلي غير مرفوع على GitHub).
بعدها فقط نفتح الموقع للأطباء والمعامل.

## الخيار 1 (المقترح): Azure App Service + Azure SQL
- **ليه:** مُدار بالكامل، HTTPS ونطاق مخصص مجانًا، نسخ احتياطية تلقائية للداتابيز، نشر مباشر من GitHub Actions.
- **التكلفة التقريبية شهريًا:**
  - App Service خطة B1 (Linux): حوالي 13 دولار.
  - Azure SQL (Basic 5 DTU للبداية): حوالي 5 دولار، أو Serverless General Purpose حسب الاستخدام.
  - Azure Blob Storage لملفات الحالات (STL/صور): أقل من دولارين في البداية.
  - **الإجمالي في البداية: حوالي 20 دولار في الشهر**، ويكبر مع الاستخدام.
- **الخطوات:**
  1. إنشاء Resource Group ثم Azure SQL Database وApp Service (.NET 10).
  2. ضبط الإعدادات (Environment variables) في App Service، الجدول بالأسفل.
  3. تحويل إنشاء الداتابيز من EnsureCreated إلى EF Core Migrations (جزء من خطة الباك إند).
  4. GitHub Actions workflow ينشر عند كل push على `main` (بعد نجاح الاختبارات).
  5. ربط الدومين `taajlab.com` و`www.taajlab.com` بالـ App Service، وتفعيل شهادة HTTPS المجانية.
  6. تفعيل Application Insights للأخطاء والأداء، وتنبيه بالإيميل.

## الخيار 2: VPS (مثل Hetzner أو DigitalOcean) + Docker
- **ليه:** أرخص وتحكم كامل، لكن الصيانة والنسخ الاحتياطي والأمان مسؤوليتنا.
- **التكلفة التقريبية:** سيرفر 4GB RAM حوالي 6–12 دولار شهريًا.
  SQL Server Express على Linux (مجاني حتى 10GB لكل داتابيز) داخل Docker.
- **الخطوات:** Dockerfile للتطبيق + docker compose (التطبيق + SQL Server + Caddy/Nginx للـ HTTPS عبر Let's Encrypt)،
  نسخ احتياطي يومي للداتابيز لمكان خارجي، وتحديثات أمان دورية للسيرفر.

## الإعدادات المطلوبة (Environment variables)
| الإعداد | الوصف |
|---|---|
| `ASPNETCORE_ENVIRONMENT=Production` | ضروري: البيانات التجريبية لا تُنشأ إلا في Development |
| `ConnectionStrings__DefaultConnection` | اتصال SQL Server |
| `Payments__WebhookSecret` | سر توقيع إشعارات الدفع، 32 حرفًا على الأقل |
| `Bootstrap__AdminEmail` / `Bootstrap__AdminPassword` | أول حساب أدمن على داتابيز فاضية (تُغيَّر الباسورد بعد أول دخول) |
| `Maps__GoogleMapsApiKey` | اختياري للخرائط |

لا تُكتب أي قيمة من دول في الكود أو في `appsettings.json`.

## الدومين `taajlab.com`
- سجلات DNS: `A`/`CNAME` للدومين الرئيسي و`www` حسب الخيار (Azure يعطي القيم في صفحة Custom domains).
- HTTPS إجباري (HSTS)، وتحويل `www` إلى الدومين الرئيسي.
- لاحقًا: إيميل `noreply@taajlab.com` للإشعارات (SPF/DKIM).

## ترتيب التنفيذ المقترح
1. المرحلة الأولى من خطة الباك إند (الأمان).
2. Migrations + Dockerfile/Workflow النشر.
3. بيئة Staging على Azure (أو VPS) للتجربة.
4. ربط `taajlab.com` وHTTPS.
5. الإطلاق التجريبي لعدد محدود من الأطباء والمعامل.
