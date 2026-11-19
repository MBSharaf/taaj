# تشغيل تاج بعد الإطلاق: المراقبة والتنبيهات وقاعدة البيانات والنسخ الاحتياطي

الملف ده بيكمّل [خطة الاستضافة](HOSTING-PLAN.md). كل اللي هنا بيتظبط مرة واحدة وقت تجهيز السيرفر.

## 1. فحوصات الصحة (Health checks)
| الرابط | بيقول إيه | يُستخدم في |
|---|---|---|
| `GET /api/v1/health` | التطبيق شغال ويرد | Azure App Service › Health check، أو أي مراقبة uptime |
| `GET /api/v1/health/ready` | التطبيق شايف قاعدة البيانات (خلال ثانيتين) والإشعارات مش متكدسة | تنبيه "الموقع واقع" |

`/ready` بيرد 200 لما كله تمام (أو Degraded لو الإشعارات المتأخرة أكتر من `Monitoring__NotificationBacklogLimit`، الافتراضي 5000)، و503 لو قاعدة البيانات مش متاحة. الرد فيه حالة كل فحص بس، من غير تفاصيل داخلية.

## 2. المراقبة (Application Insights)
لما الإعداد `APPLICATIONINSIGHTS_CONNECTION_STRING` يكون موجود في إعدادات السيرفر، التطبيق بيبعت تلقائيًا: الطلبات وزمنها، الأخطاء، استعلامات SQL، والـ logs. من غيره مفيش أي بيانات بتتبعت لأي جهة.

## 3. التنبيهات المطلوبة (Azure Monitor › Alerts، إيميل لـ Sharaf)
| التنبيه | الشرط | الأهمية |
|---|---|---|
| الموقع واقع | فحص `/api/v1/health/ready` فشل مرتين ورا بعض (Availability test كل 5 دقايق من مكانين) | حرج |
| أخطاء السيرفر | طلبات 5xx أكتر من 1% خلال 5 دقايق | حرج |
| بطء | p95 لزمن الطلبات أكتر من 2 ثانية لمدة 10 دقايق | متوسط |
| هجوم على الدخول | أكتر من 50 رد 401/429 على `/api/v1/auth/*` من نفس الـ IP خلال 10 دقايق | عالي |
| رفض أمني | أكتر من 100 سطر log يبدأ بـ "Security " (طلبات اترفضت 401/403/429) خلال 10 دقايق | متوسط |
| قاعدة البيانات | DTU/CPU أكتر من 80% لمدة 15 دقيقة، أو المساحة أكتر من 85% | متوسط |
| الإشعارات متأخرة | `/ready` بيرجع Degraded لمدة 15 دقيقة | متوسط |

استعلام جاهز (Log Analytics) لتنبيه هجوم الدخول:
```kusto
requests
| where timestamp > ago(10m) and url has "/api/v1/auth/" and resultCode in ("401", "429")
| summarize attempts = count() by client_IP
| where attempts > 50
```

## 4. مستخدم قاعدة البيانات بأقل صلاحيات
- التطبيق بيشتغل بمستخدم يقرا ويكتب بيانات بس، ومينفعش يعدّل الجداول. السكربت: [deploy/sql/create-app-user.sql](../deploy/sql/create-app-user.sql).
- تحديث الجداول (migrations) بيحصل وقت النشر بمستخدم المالك:
  `dotnet ef migrations script --idempotent -p src/LabStage.Infrastructure -o migrate.sql` ثم تشغيله على القاعدة قبل تشغيل النسخة الجديدة.
- في إعدادات السيرفر: `Database__MigrateOnStartup=false` و`ConnectionStrings__DefaultConnection` بمستخدم `taaj_app`.
- على Azure الأفضل: Managed Identity بدل الباسورد (`Authentication=Active Directory Managed Identity` في الـ connection string) و`CREATE USER [اسم-الـ-App-Service] FROM EXTERNAL PROVIDER;` بنفس الأدوار.

## 5. النسخ الاحتياطي
- **Azure SQL:** نسخ تلقائية مع استرجاع لأي لحظة (Point-in-time) لمدة 7 أيام في Basic/Standard. نخليها 35 يوم، ونفعّل Long-term retention: نسخة أسبوعية لمدة 12 أسبوع ونسخة شهرية لمدة 12 شهر.
- **ملفات الحالات** (`App_Data/case-files` أو Azure Blob بعدين): Soft delete لمدة 30 يوم + نسخ يومي لحساب تخزين تاني.
- **مفاتيح Data Protection** (بتحمي الكوكيز وأسرار التحقق بخطوتين): لازم تتحفظ في مكان دائم ومشترك (Azure Blob + Key Vault). لو ضاعت، كل المستخدمين هيخرجوا ولازم يفعلوا التحقق بخطوتين من جديد.
- **تجربة استرجاع** كل 3 شهور على قاعدة تجريبية، ونكتب الوقت اللي أخدته.

## 6. الإعدادات الأمنية المرتبطة
| الإعداد | الافتراضي | الوصف |
|---|---|---|
| `Security__RequireAdminTwoFactor` | `true` خارج Development | حساب الأدمن لازم يفعّل التحقق بخطوتين قبل ما يستخدم لوحة الإدارة |
| `Security__AuthRequestsPerMinute` | `10` | محاولات الدخول/التسجيل/كلمة المرور لكل IP في الدقيقة |
| `Performance__MaxConcurrentApiRequests` | `80` | طلبات API في نفس اللحظة قبل ما الباقي يستنى دوره |
| `Monitoring__NotificationBacklogLimit` | `5000` | بعده `/ready` يقول Degraded |
