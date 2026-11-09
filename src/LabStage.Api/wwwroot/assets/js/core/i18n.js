// Shared Bilingual (AR / EN) & Dark/Light Mode Engine for LabStage
// Zero MutationObserver overhead — executes once on page load and once after dynamic API renders.

const THEME_KEY = 'labstage_theme';
const LANG_KEY = 'labstage_lang';

const EXACT_AR_TO_EN = {
  // Brand & Top Navigation
  'تاج': 'Taaj',
  'تخطَّ إلى المحتوى': 'Skip to main content',
  'تخطّي إلى المحتوى الرئيسي': 'Skip to main content',
  'المعامل': 'Labs',
  'دليل المعامل': 'Lab Directory',
  'كيف تعمل المنصة': 'How It Works',
  'كيف تعمل': 'How It Works',
  'كيف يعمل': 'How It Works',
  'للمعامل': 'For Labs',
  'عن المنصة': 'About Us',
  'تواصل معنا': 'Contact Us',
  'تسجيل الدخول': 'Sign In',
  'أنشئ حسابًا': 'Create Account',
  'إنشاء حساب': 'Create Account',
  'انضم كطبيب أو معمل': 'Join as Dentist or Lab',
  'استكشف المعامل الآن': 'Explore Labs Now',
  'سجّل معملك الآن': 'Register Your Lab',
  'سجّل عيادتك مجانًا': 'Register Your Clinic',
  'استعراض دليل المعامل': 'Browse Lab Directory',
  'استعراض دليل المعامل العام': 'Browse Public Lab Directory',
  'تصفّح المعامل': 'Browse Labs',
  'خروج': 'Sign Out',
  'بحث': 'Search',
  'تصفية': 'Filter',
  'عرض': 'View',
  'حفظ': 'Save',
  'تحديث': 'Update',
  'إلغاء': 'Cancel',

  // Homepage Hero & Chapters
  'منصة معامل الأسنان في مصر': 'Dental Lab Platform in Egypt',
  'منصة معامل الأسنان الرقمية · مصر': 'Digital Dental Lab Platform · Egypt',
  'من ملف المسح إلى التاج النهائي،': 'From intraoral scan to final crown,',
  'مع معمل تثق به': 'with a lab you trust',
  'قارن معامل الأسنان الموثّقة حسب المحافظة والخدمة والسعر والتقييم، ارفع ملفات الحالة، وتابع كل مرحلة لحظة بلحظة — مع دفع واضح على دفعتين.': 'Compare verified dental labs by governorate, service, price, and rating, upload 3D case files, and track every stage in real time — with transparent 2-step milestone payments.',
  'الخدمة أو اسم المعمل': 'Service or Lab Name',
  'كل المحافظات': 'All Governorates',
  'ابحث عن معمل': 'Find a Lab',
  'معامل موثّقة السجل': 'Verified Dental Labs',
  'دفعة أولى 20% فقط بعد التصميم': 'Only 20% Initial Payment After Design',
  'محادثة وملفات 3D محمية لكل حالة': 'Protected 3D Files & Case Chat',
  'يرفع المعمل التصميم الرقمي للحالة، فتراجعه وتعتمده أو تطلب تعديلًا بملاحظاتك قبل أي تصنيع. عند الاعتماد تُستحق': 'The lab uploads the 3D digital design for your review and approval before milling. Upon approval,',
  'الدفعة الأولى 20%': 'the 20% first payment is due',
  'تم رفع التصميم': 'Design Uploaded',
  'مثال': 'Example',
  'يُفرَّز التصميم المعتمد من بلوك الزركونيا أو الإيماكس. يحدّث المعمل حالة المرحلة ويرفع صور التنفيذ، فترى التقدّم دون مكالمات متابعة.': 'Your approved design is milled from Zirconia or E.max blocks. The lab updates stage progress in real time without follow-up calls.',
  'بدأت مرحلة التفريز': 'Milling Started',
  'الوقت المتوقع للمرحلة التالية يظهر في الحالة': 'Estimated completion time is shown on the case',
  'تُبنى طبقات البورسلين لمطابقة اللون والشفافية الطبيعية. تستطيع إرسال صور اللون أو ملاحظات إضافية في محادثة الحالة الخاصة.': 'Porcelain layers are hand-built to match natural VITA shade and translucency. Share shade photos directly in the case chat.',
  'اللمسة النهائية واللمعان. باكتمال آخر مرحلة مطلوبة في الحالة تُستحق': 'Final polish and glaze. Upon completing the last required stage,',
  'الدفعة النهائية 80%': 'the 80% final payment is due',
  'ويبدأ التسليم أو الاستلام عبر مندوب المعمل.': 'and delivery or courier pickup begins.',
  'ليست كل حالة تحتاج المراحل الأربع؛ تختار المراحل المطلوبة عند إنشاء الطلب.': 'Not every case requires all four stages; select only the stages you need when creating an order.',

  // Homepage hero showcase
  'السابق': 'Previous',
  'التالي': 'Next',
  'إيقاف العرض التلقائي مؤقتًا': 'Pause automatic rotation',
  'تاج زركونيا متعدد الطبقات': 'Multilayer Zirconia Crown',
  'تاج إيماكس': 'E.max Crown',
  'جسر زركونيا رقمي — للوحدة': 'Digital Zirconia Bridge — per unit',
  'فينير إيماكس تجميلي': 'E.max Cosmetic Veneer',
  'دعامة زرعة مخصصة': 'Custom Implant Abutment',
  'يبدأ من': 'From',
  'مراحل التصنيع': 'Production stages',
  'أسعار استرشادية · كل معمل يحدد أسعاره': 'Indicative prices · each lab sets its own',
  'قوة الزركونيا، بلون طبيعي': 'Zirconia strength, natural shade',
  'شفافية تشبه السن الطبيعي': 'Translucency like a natural tooth',
  'دقة CAD/CAM من أول وحدة لآخرها': 'CAD/CAM precision, unit after unit',
  'ابتسامة مصمَّمة بعناية': 'A smile designed with care',
  'ملاءمة دقيقة فوق الزرعة': 'A precise fit on the implant',

  // Homepage Audience, Payments & FAQ
  'لمن المنصة': 'Who Is Taaj For',
  'مساحة عمل واحدة للطبيب والمعمل': 'One Unified Workspace for Dentists & Labs',
  'سوق لاكتشاف المعامل، ونظام متكامل لإدارة الحالات والمراحل والفواتير.': 'A marketplace to discover verified labs and a complete system to manage cases, stages, and invoices.',
  'لأطباء الأسنان': 'For Dentists',
  'اختر المعمل المناسب وتابع حالتك بوضوح': 'Choose the Right Lab & Track Every Case Clearly',
  'ابحث وقارن حسب المحافظة والخدمة والسعر والتقييم': 'Search and compare by governorate, service, price, and verified rating',
  'ارفع ملفات STL وPLY وOBJ والصور وPDF بأمان': 'Securely upload STL, PLY, OBJ, photos, and PDF prescriptions',
  'خط زمني لكل مرحلة مع اعتماد التصميم': 'Stage-by-stage timeline with 3D design approval',
  'ادفع على دفعتين بإيصالات وفواتير واضحة': 'Pay in two milestones with clear receipts and statements',
  'سجّل كطبيب': 'Register as Dentist',
  'لمعامل الأسنان': 'For Dental Labs',
  'استقبل طلبات جديدة وأدر معملك من مكان واحد': 'Receive New Clinic Orders & Manage Your Lab in One Place',
  'صفحة معمل موثّقة تظهر في نتائج البحث': 'Verified digital storefront appearing in directory search',
  'إدارة الطلبات والمراحل وفريق العمل': 'Manage orders, CAD/CAM production stages, and workflow',
  'كشوف حساب لكل طبيب ومستحقات وتسويات': 'Separate account statements per dentist & wallet payouts',
  'طلبات المندوب والاستلام والتسليم': 'Courier pickup and clinic delivery coordination',
  'سجّل معملك': 'Register Your Lab',
  'دفع مرحلي آمن': 'Secure Milestone Payments',
  'دفعتان مرتبطتان بمراحل الحالة الفعلية (20% ثم 80%)': 'Two Payments Tied to Real Production Stages (20% then 80%)',
  'لا تدفع مقدمًا قبل أن ترى التصميم الرقمي وتعتمده بنفسك. يرتبط السداد بتقدم مراحل التصنيع الفعلية لضمان حقوق الطبيب والمعمل.': 'Never pay upfront before reviewing and approving the 3D digital design yourself. Payments follow actual production milestones.',
  'الدفعة الأولى 20% فقط بعد مراجعة واعتماد مرحلة التصميم': 'First payment (20%) only after reviewing and approving the Design stage',
  'الدفعة النهائية 80% عند اكتمال آخر مرحلة تصنيع مطلوبة': 'Final payment (80%) upon completion of the final required stage',
  'توثيق كامل للمدفوعات والإيصالات داخل لوحة التحكم الخاصة بك': 'Full documentation of payments and receipts inside your dashboard',
  'بعد اعتماد التصميم': 'After Design approval',
  'بعد اكتمال آخر مرحلة': 'After final stage completion',
  'قيمة الحالة المعتمدة': 'Approved Case Value',
  'عند اعتماد التصميم (20%)': 'On Design Approval (20%)',
  'عند الجاهزية للتسليم (80%)': 'On Delivery Readiness (80%)',
  'أسئلة شائعة': 'Frequently Asked Questions',
  'كل ما تريد معرفته قبل البدء': 'Everything You Need to Know Before Starting',
  'كيف يتم دفع قيمة الحالة على المنصة؟': 'How do case payments work on Taaj?',
  'هل يجب اختيار المراحل الأربع في كل حالة؟': 'Do I have to select all four stages for every case?',
  'ما المزايا التي يحصل عليها طبيب الأسنان؟': 'What benefits do dentists get on Taaj?',
  'ما المزايا التي يحصل عليها معمل الأسنان؟': 'What benefits do dental labs get on Taaj?',
  'هل ملفات الحالات والحسابات المالية محمية؟': 'Are 3D case files and financial accounts protected?',
  'جاهز تبدأ حالتك القادمة؟': 'Ready to Start Your Next Case?',
  'أنشئ حسابك مجانًا وابحث عن المعمل المناسب في محافظتك.': 'Create your account and find the right dental lab in your governorate.',
  'المنصة': 'Platform',
  'الحساب': 'Account',
  'السياسات': 'Policies',
  'تسجيل طبيب': 'Dentist Registration',
  'تسجيل معمل': 'Lab Registration',
  'الشروط والأحكام': 'Terms & Conditions',
  'سياسة الخصوصية': 'Privacy Policy',
  'سياسة الدفع والاسترداد': 'Payments & Refunds',
  'سياسة النزاعات': 'Dispute Policy',
  'الأسعار بالجنيه المصري.': 'All prices in Egyptian Pounds (EGP).',

  // Doctor: simple new-case order form
  'إنشاء طلب حالة جديدة': 'Create a new case order',
  'حالة جديدة': 'New case',
  'اسم المريض': 'Patient name',
  'اسم المريض:': 'Patient:',
  'اكتب اسم المريض': 'Type the patient\'s name',
  'موعد التسليم': 'Due date',
  'الاستلام والتسليم': 'Pickup & delivery',
  'مندوب المعمل': 'Lab courier',
  'من مقر المعمل': 'At the lab',
  'عنوان العيادة للمندوب': 'Clinic address for the courier',
  'مثال: مدينة نصر، القاهرة': 'e.g. Nasr City, Cairo',
  'ملاحظات للمعمل': 'Notes for the lab',
  'خط الحواف، نقاط التماس، الإطباق، أو أي تعليمات أخرى': 'Margin line, contacts, occlusion, or any other instructions',
  'إرفاق ملف المسح': 'Attach scan file',
  'اختياري · STL / PLY / OBJ / صور / PDF': 'Optional · STL / PLY / OBJ / photos / PDF',
  'عدد الأسنان': 'Number of teeth',
  'نوع التركيبة': 'Restoration type',
  'التكلفة الكلية': 'Total cost',
  'المطلوب دفعه (20%)': 'To pay now (20%)',
  'بعد اعتمادك للتصميم': 'after you approve the design',
  'عند تأكيد المعمل للطلب': 'when the lab confirms the order',
  'المتبقي (80%)': 'Remaining (80%)',
  'عند اكتمال آخر مرحلة': 'when the last stage is done',
  'السعر من قائمة أسعار المعمل المختار · المعمل يؤكد السعر النهائي': 'Price from the selected lab\'s price list · the lab confirms the final price',
  'سعر استرشادي · المعمل يؤكد السعر النهائي': 'Guide price · the lab confirms the final price',
  'مراحل التصنيع المطلوبة': 'Production stages needed',
  'إرسال الطلب للمعمل': 'Send order to lab',
  'الأسنان والتركيبة': 'Teeth & restoration',
  'فينير إيماكس': 'E.max Veneer',
  'جسر زركونيا (للوحدة)': 'Zirconia bridge (per unit)',
  'واقي ليلي رقمي': 'Digital night guard',
  'تاج PMMA مؤقت': 'PMMA temporary crown',
  'اضغط على الأسنان المطلوبة · المحدد:': 'Tap the teeth you need · selected:',
  'القواطع العلوية': 'Upper incisors',
  'الأمامية العلوية': 'Upper anteriors',
  'لون الأسنان': 'Tooth shade',
  'طريقة الطبعة': 'Impression type',
  'مسح فموي رقمي': 'Intraoral scan',
  'موديل جبس': 'Stone model',
  'فكين مع تسجيل العضة': 'Two models with bite',
  'طبعة سيليكون': 'Silicone impression',
  'ابحث باسم المريض أو رقم الطلب أو المعمل...': 'Search by patient name, order # or lab...',
  'اكتب اسم المريض.': 'Enter the patient\'s name.',
  'حدد سنًا واحدًا على الأقل من مخطط الأسنان.': 'Select at least one tooth on the chart.',
  'اختر مرحلة تصنيع واحدة على الأقل.': 'Choose at least one production stage.',
  'تعذّر حساب التكلفة. اختر نوع التركيبة والأسنان.': 'Could not compute the cost. Choose a restoration type and teeth.',
  'اكتب عنوان العيادة ليصل المندوب.': 'Enter the clinic address for the courier.',

  // Static demo (GitHub Pages)
  'نسخة عرض ببيانات تجريبية محفوظة في متصفحك فقط.': 'Demo version with sample data stored only in your browser.',
  'إعادة ضبط بيانات العرض': 'Reset demo data',
  'حسابات تجريبية': 'Demo accounts',
  'اضغط على حساب لملء البيانات ثم اضغط «تسجيل الدخول».': 'Tap an account to fill the form, then press Sign in.',
  'طبيب': 'Dentist',
  'مدير المنصة': 'Platform admin',

  // Dashboards v2 (shell + home grids)
  'القائمة': 'Menu',
  'عام': 'General',
  'المساعدة والدعم': 'Help & support',
  'الموقع الرئيسي': 'Main site',
  'اطلب حالة جديدة': 'Order a new case',
  'اختر المعمل وحدد الأسنان في أقل من دقيقة.': 'Pick a lab and the teeth in under a minute.',
  'إنشاء طلب': 'Create order',
  'تاج — الصفحة الرئيسية': 'Taaj — home',
  'فتح القائمة': 'Open menu',
  'إغلاق القائمة': 'Close menu',
  'الرسائل والطلبات': 'Messages & orders',
  'الإشعارات': 'Notifications',
  'لوحة التحكم': 'Dashboard',
  'تابع حالاتك ومراحلها ومدفوعاتها في مكان واحد.': 'Track your cases, stages and payments in one place.',
  'طلب جديد هذا الشهر': 'new this month',
  'تتابع مراحلها لحظة بلحظة': 'Tracked stage by stage',
  'بانتظار موافقتك': 'Awaiting your approval',
  'تصميمات أو أسعار تحتاج ردك': 'Designs or prices that need your answer',
  'تم تسليمها': 'Delivered',
  'هذا الشهر': 'this month',
  'تحليل الطلبات': 'Order analytics',
  'طلباتك المرسلة هذا الأسبوع': 'Orders you sent this week',
  'التذكير': 'Reminder',
  'أحدث الطلبات': 'Latest orders',
  '+ جديد': '+ New',
  'المعامل التي تتعامل معها': 'Labs you work with',
  '+ معمل': '+ Lab',
  'نسبة الإنجاز': 'Completion',
  'من الحالات مكتملة': 'of cases completed',
  'مكتملة': 'Completed',
  'بانتظار': 'Waiting',
  'الوقت المتبقي للتسليم': 'Time left to delivery',
  'الطلبات': 'Orders',
  'أنشئ حالة جديدة وتابع كل طلباتك.': 'Create a new case and follow all your orders.',
  'المدفوعات': 'Payments',
  'ابحث باسم المريض أو رقم الطلب...': 'Search by patient or order #...',
  'الدفعات المستحقة وسجل السداد لكل حالة بالجنيه المصري.': 'Due payments and payment history for every case, in EGP.',
  'تفاصيل الحالة': 'Case details',
  'المراحل والمحادثة والملفات والدفعات.': 'Stages, chat, files and payments.',
  'زوّد ظهور معملك': 'Boost your lab',
  'حملة ممولة تضع معملك أول نتائج البحث.': 'A sponsored campaign puts your lab at the top of search.',
  'إنشاء حملة': 'Create campaign',
  'طلبات معملك وإنتاجه وأرباحه في نظرة واحدة.': 'Your lab\'s orders, production and earnings at a glance.',
  'خدمة جديدة': 'New service',
  'طلبات جديدة': 'New orders',
  'وصلت هذا الأسبوع': 'arrived this week',
  'مراحل يعمل عليها المعمل الآن': 'Stages the lab is working on',
  'أرباح هذا الشهر': 'This month\'s earnings',
  'صافي بعد عمولة المنصة': 'Net after platform commission',
  'رصيد متاح للسحب': 'Available to withdraw',
  'يمكنك سحبه من المحفظة': 'Withdraw it from the wallet',
  'الطلبات الواردة': 'Incoming orders',
  'طلبات وصلت للمعمل هذا الأسبوع': 'Orders received this week',
  'المهمة التالية': 'Next task',
  'كشوف الحساب': 'Statements',
  'إنجاز المراحل': 'Stage completion',
  'من مراحل الإنتاج مكتملة': 'of production stages completed',
  'استقبل الطلبات وحدّث مراحل التصنيع.': 'Receive orders and update production stages.',
  'المحفظة': 'Wallet',
  'الحسابات والمحفظة': 'Accounts & wallet',
  'كشوف الأطباء والمحفظة وسحب الأرباح والإعلانات.': 'Dentist statements, wallet, payouts and ads.',
  'حملة إعلانية': 'Ad campaign',
  'قبول الطلب وتحديث المراحل والمحادثة مع الطبيب.': 'Accept the order, update stages and chat with the dentist.',
  '· الإدارة': '· Admin',
  'تسويات المعامل': 'Lab settlements',
  'راجع المستحقات وأنشئ دفعات التسوية البنكية.': 'Review balances and create bank settlement batches.',
  'فتح التسويات': 'Open settlements',
  'مؤشرات المنصة وتوثيق المعامل والتسويات.': 'Platform metrics, lab verification and settlements.',
  'دفعة تسوية': 'Settlement batch',
  'الإعدادات': 'Settings',
  'المستخدمون': 'Users',
  'حساب جديد هذا الشهر': 'new this month',
  'معمل موثّق': 'verified',
  'طلب هذا الشهر': 'this month',
  'إيراد المنصة': 'Platform revenue',
  'عمولات واشتراكات المعامل': 'Commissions and lab subscriptions',
  'طلبات المنصة': 'Platform orders',
  'الطلبات المسجلة هذا الأسبوع': 'Orders placed this week',
  'بانتظار المراجعة': 'Pending review',
  'لا توجد معامل بانتظار التوثيق': 'No labs waiting for verification',
  'مراجعة المعامل': 'Review labs',
  'بانتظار الموافقة': 'Awaiting approval',
  'أحدث التسجيلات': 'Latest sign-ups',
  'توثيق المعامل': 'Lab verification',
  'من المعامل موثّقة': 'of labs verified',
  'موثّقة': 'Verified',
  'قيد المراجعة': 'Under review',
  'غير موثّقة': 'Not verified',
  'حتى تسوية نهاية الشهر': 'Until month-end settlement',
  'موعد إنشاء دفعات التسوية للمعامل': 'When lab settlement batches are created',
  'لا توجد مهام عاجلة الآن': 'Nothing urgent right now',
  'لا يوجد موعد تسليم قريب': 'No upcoming delivery date',
  'جاري التحميل...': 'Loading...',
  'جاري تحميل الإشعارات...': 'Loading notifications...',
  'يعمل على': 'working on',
  'نشط': 'Active',
  'موقوف': 'Suspended',
  'حالات جارية': 'Active cases',
  'مكتمل': 'Completed',
  'جديد': 'New',
  'طبيب أسنان': 'Dentist',
  'صاحب معمل': 'Lab owner',
  'فني معمل': 'Lab technician',
  'مدير': 'Admin',
  'لا توجد عناصر بعد.': 'Nothing here yet.',
  'لا يوجد بعد.': 'None yet.',

  // Governorates & Badges
  'الكل': 'All',
  'القاهرة الكبرى': 'Greater Cairo',
  'الإسكندرية': 'Alexandria',
  'الدلتا': 'Delta',
  'الصعيد': 'Upper Egypt',
  'القاهرة': 'Cairo',
  'الجيزة': 'Giza',
  'المنصورة': 'Mansoura',
  'طنطا': 'Tanta',
  'أسيوط': 'Assiut',
  'مُموَّل': 'Sponsored',
  'موثّق': 'Verified',
  'موثّق ✓': 'Verified ✓',
  'معمل موثّق ✓': 'Verified Lab ✓',
  'معمل أسنان موثّق': 'Verified Dental Lab',
  'مندوب استلام وتسليم': 'Courier Pickup & Delivery',
  'عرض المتجر والطلب': 'View Storefront & Order',
  'إرسال حالة جديدة لهذا المعمل': 'Send New Case to This Lab',
  'عرض الملف وقائمة الأسعار': 'View Profile & Price List',
  'الدليل الكامل للمعامل ←': 'Full Lab Directory ←',
  'اطلب هذه الخدمة': 'Order This Service',

  // Dashboards Common & Sidebar
  'بوابة الطبيب': 'Dentist Portal',
  'بوابة المعمل': 'Lab Portal',
  'إدارة المنصة — مصر': 'Platform Admin — Egypt',
  'نظرة عامة والرسوم البيانية': 'Overview & Analytics',
  'طلباتي وإرسال حالة جديدة': 'My Orders & New Case',
  'المدفوعات والفواتير': 'Payments & Invoices',
  'الطلبات والمراحل الإنتاجية': 'Orders & Production Stages',
  'الأطباء المتعاملون مع المعمل': 'Partner Dentists',
  'كشف حساب كل طبيب منفصل': 'Per-Dentist Account Ledger',
  'تفاصيل المحفظة وسحب الأرباح': 'Wallet Details & Withdrawals',
  'تعديل قائمة الأسعار (Price List)': 'Edit Price List',
  'إدارة متجر وبروفايل المعمل': 'Manage Lab Storefront & Profile',
  'إدارة الإعلانات وإحصائياتها': 'Sponsored Ads & Analytics',
  'معاينة متجري العام': 'Preview My Public Storefront',
  'مركز القيادة والرسوم البيانية': 'Command Center & Charts',
  'إعدادات العمولات والاشتراكات': 'Commissions & Subscriptions',
  'توثيق المعامل والتسويات البنكية': 'Lab Verification & Bank Settlements',
  'سجل التدقيق الرقابي (Audit Log)': 'Compliance Audit Log',
  'العودة للوحة التحكم': 'Back to Dashboard',
  'الرجوع للـ Dashboard ←': 'Back to Dashboard ←',
  'تعديل متجري': 'Edit My Storefront',

  // Statuses & Stages
  'التصميم': 'Design',
  'التفريز': 'Milling',
  'الطبقات': 'Layering',
  'الجليز': 'Glazing',
  'بانتظار مراجعة المعمل': 'Pending Lab Review',
  'مقترح سعر بانتظار موافقة الطبيب': 'Price Revision Proposed',
  'مقبول ومثبّت السعر': 'Accepted & Price Locked',
  'مرفوض': 'Rejected',
  'قيد التصنيع': 'In Production',
  'جاهز للتسليم': 'Ready for Delivery',
  'مكتمل ومسلّم': 'Completed & Delivered',
  'لم تبدأ': 'Not Started',
  'قيد التنفيذ': 'In Progress',
  'مرفوعة لمراجعة الطبيب': 'Submitted for Dentist Review',
  'مطلوب تعديل': 'Revision Requested',
  'معتمدة من الطبيب': 'Approved by Dentist',
  'متوقفة مؤقتًا': 'Paused / Blocked',
  'غير مستحقة بعد': 'Locked (Not Yet Due)',
  'مستحقة السداد الآن': 'Due for Payment Now',
  'مدفوعة ✓': 'Paid ✓',
  'مستردة': 'Refunded',

  // Dashboard Cards & Labels
  'إجمالي الطلبات': 'Total Orders',
  'حالات قيد التصنيع': 'Cases in Production',
  'إجمالي المسدد': 'Total Paid',
  'دفعات مستحقة الآن': 'Payments Due Now',
  'عمولة الخدمة للطبيب (1%)': 'Dentist Service Fee (1%)',
  'إرسال حالة جديدة +': 'Send New Case +',
  'أحدث الطلبات ومراحلها الحالية': 'Recent Orders & Active Stages',
  'مركز الإشعارات الفورية': 'Live Notifications Center',
  'لا توجد إشعارات جديدة.': 'No new notifications.',
  'لا توجد طلبات بعد.': 'No orders yet.',
  'التفاصيل والمحادثة': 'Details & Chat',
  'إدارة المراحل': 'Manage Stages',
  'إدارة الحالة': 'Manage Case',
  'تنزيل آمن': 'Secure Download',
  'إرسال': 'Send',
  'رفع الملف': 'Upload File',
  'حذف ✕': 'Delete ✕',

  // Table Headers
  'رقم الطلب': 'Order #',
  'الحالة / الخدمة': 'Case / Service',
  'المعمل': 'Laboratory',
  'الطبيب': 'Dentist',
  'الطبيب / العيادة': 'Dentist / Clinic',
  'المحافظة': 'Governorate',
  'الخدمة': 'Service',
  'المراحل': 'Stages',
  'الحالة': 'Status',
  'المدفوع / الإجمالي': 'Paid / Total',
  'إجراء': 'Action',
  'قيمة الطلب المؤكدة': 'Confirmed Order Value',
  'الدفعة الأولى (20%)': 'First Payment (20%)',
  'الدفعة النهائية (80%)': 'Final Payment (80%)',
  'إجراء الدفع': 'Payment Action',
  'مرجع العملية': 'Transaction Ref',
  'المبلغ': 'Amount',
  'المسترد': 'Refunded',
  'التاريخ': 'Date',
  'عدد الطلبات': 'Orders Count',
  'إجمالي المعاملات': 'Total Transactions',
  'المدفوع': 'Paid',
  'المتبقي': 'Remaining',
  'عمولة 2%': '2% Commission',
  'صافي المعمل (98%)': 'Lab Net (98%)',
  'كشف الحساب': 'Statement',
  'إجمالي الدفعة': 'Batch Gross',
  'عمولة المنصة (2%)': 'Platform Fee (2%)',
  'الصافي المحول للمعمل': 'Net Transferred to Lab',
  'مرجع التحويل البنكي / المحفظة': 'Bank / Wallet Transfer Ref',
  'الاشتراك': 'Subscription',
  'إجراءات': 'Actions',

  // Dashboards — shared shell
  'تسجيل الخروج': 'Sign Out',
  'لوحة المتابعة': 'Overview',
  'الطلبات وإنشاء حالة': 'Orders & New Case',
  'استكشاف المعامل': 'Explore Labs',
  'دليل المعامل العام': 'Public Lab Directory',
  'الدقهلية': 'Dakahlia',
  'طلب': 'Order',
  'السعر': 'Price',
  'مرجع الحالة': 'Case Ref',
  'مرجع الحالة:': 'Case ref:',
  '· الأسنان:': '· Teeth:',
  '· اللون:': '· Shade:',
  'ملاحظات الحالة:': 'Case notes:',
  'السعر المؤكد': 'Confirmed Price',
  'جسر زركونيا': 'Zirconia Bridge',
  'جسر زركونيا متعدد الطبقات (5 وحدات)': 'Multilayer Zirconia Bridge (5 units)',
  'زركونيا': 'Zirconia',
  'إيماكس': 'E.max',
  'زرعات': 'Implants',
  'فينير': 'Veneers',
  'سجل التدقيق': 'Audit Log',
  'إنشاء الطلب': 'Order created',
  'إكمال التصميم وبدء مرحلة التفريز': 'Design completed, milling started',
  'لا توجد ملاحظات مسجلة بعد.': 'No notes recorded yet.',
  'لا توجد ملفات مرفوعة بعد.': 'No files uploaded yet.',
  'عرض الكل': 'View All',
  'الطبيب والعيادة': 'Dentist & Clinic',
  'المدفوع:': 'Paid:',
  'المتبقي:': 'Remaining:',
  'كشف حساب الطبيب التفصيلي ←': 'Detailed dentist statement ←',
  'تم رفع التصميم واعتماده من الطبيب.': 'Design uploaded and approved by the dentist.',

  // Admin dashboard
  'تاج · الإدارة': 'Taaj · Admin',
  'الرسوم البيانية والملخص': 'Charts & Summary',
  'قواعد العمل والعمولة': 'Business Rules & Commission',
  'توثيق المعامل والاشتراكات': 'Lab Verification & Subscriptions',
  'التسويات البنكية للمعامل': 'Lab Bank Settlements',
  'سجل التدقيق الأمني': 'Security Audit Log',
  'لوحة مدير المنصة — الإعدادات المالية، توثيق المعامل، التسويات، وسجل التدقيق': 'Platform Admin — financial settings, lab verification, settlements, and audit log',
  'مدير المنصة': 'Platform Admin',
  'الرقابة المالية والتشغيلية الموحدة': 'Unified financial & operational oversight',
  'مركز قيادة منصة تاج — مصر': 'Taaj Command Center — Egypt',
  'مراقبة فورية لدفتر الأستاذ المالي، عمولات المنصة (2% معمل + 1% طبيب)، مصروفات بوابة الدفع، وتوثيق المعامل.': 'Real-time monitoring of the financial ledger, platform commissions (2% lab + 1% dentist), payment gateway costs, and lab verification.',
  'إجمالي المحصل عبر البوابة': 'Total Collected via Gateway',
  'جميع الدفعات المؤكدة (20% / 80%)': 'All confirmed payments (20% / 80%)',
  'إيراد عمولة المنصة (2% معمل + 1% طبيب)': 'Platform Commission Revenue (2% lab + 1% dentist)',
  'صافي العمولة بعد أي استرداد': 'Net commission after any refunds',
  'إيراد اشتراكات المعامل': 'Lab Subscription Revenue',
  'مصروف رسوم بوابة الدفع (تتحمله المنصة)': 'Payment Gateway Fees (borne by the platform)',
  'لا يُخصم من المعمل ولا الطبيب': 'Not charged to the lab or the dentist',
  'هيكل التدفقات المالية للمنصة (ج.م)': 'Platform Cash-Flow Structure (EGP)',
  'مقارنة إجمالي التحصيل، صافي المعامل (98%)، عمولة المنصة (2%)، والاشتراكات': 'Total collected vs. lab net (98%), platform commission (2%), and subscriptions',
  'دفتر الأستاذ المزدوج': 'Double-entry ledger',
  'إجمالي التحصيل': 'Total Collected',
  'عمولات المنصة (2% + 1%)': 'Platform Commissions (2% + 1%)',
  'إيراد الاشتراكات': 'Subscription Revenue',
  'مصروف البوابة': 'Gateway Cost',
  'مؤشر توثيق المعامل والتسويات البنكية': 'Lab Verification & Bank Settlement Index',
  'نسب المعامل الموثقة وحالة دفعات التسوية': 'Verified-lab ratio and settlement batch status',
  'المعامل الموثقة النشطة': 'Active verified labs',
  'إجمالي الطلبات المسجلة': 'Total registered orders',
  'إجمالي المستخدمين والحسابات': 'Total users & accounts',
  'دفعات التسوية البنكية للمعامل': 'Lab bank settlement batches',
  'إعدادات قواعد العمل والعمولة والاشتراك': 'Business Rules, Commission & Subscription Settings',
  'نسبة العمولة بنقاط الأساس (200 = 2%)': 'Commission rate in basis points (200 = 2%)',
  'قيمة الاشتراك الشهري للمعمل (ج.م)': 'Monthly lab subscription (EGP)',
  'مدة الفترة التجريبية المجانية (أيام)': 'Free trial length (days)',
  'نسبة رسوم بوابة الدفع بنقاط الأساس (مصروف منصة)': 'Payment gateway fee in basis points (platform cost)',
  'حفظ إعدادات المنصة': 'Save Platform Settings',
  'المعامل والتوثيق والتسويات': 'Labs, Verification & Settlements',
  'التوثيق': 'Verification',
  'توثيق ✓': 'Verify ✓',
  'إنشاء دفعة تسوية': 'Create Settlement Batch',
  'دفعات التسوية للمعامل (Settlement Batches)': 'Lab Settlement Batches',
  'إجمالي المدفوع': 'Total Paid',
  'العمولة': 'Commission',
  'صافي التسوية': 'Net Settlement',
  'مرجع التحويل البنكي': 'Bank Transfer Ref',
  'لا توجد دفعات تسوية بعد.': 'No settlement batches yet.',
  'سجل التدقيق الأمني والمالي (Audit Logs)': 'Security & Financial Audit Log',

  // Lab dashboard
  'لوحة المعمل والإحصائيات': 'Lab Dashboard & Analytics',
  'الطلبات ومراحل الإنتاج': 'Orders & Production Stages',
  'الأطباء وكشف حساب كل طبيب': 'Dentists & Statements',
  'المحفظة وسحب الأرباح': 'Wallet & Payouts',
  'تعديل قائمة الأسعار (Pricelist)': 'Edit Price List',
  'الإعلانات الممولة وإحصائياتها': 'Sponsored Ads & Stats',
  'ملف المعمل العام (معاينة)': 'Public Lab Profile (Preview)',
  'نظام إدارة الإنتاج الرقمي CAD/CAM': 'Digital CAD/CAM production system',
  'لوحة معمل القاهرة الرقمي للتركيبات': 'Cairo Digital Prosthetics Lab',
  'تحليلات الأداء المالي والإنتاجي، متابعة المراحل، والأطباء المتعاملين مع المعمل.': 'Financial and production analytics, stage tracking, and the dentists working with your lab.',
  'إدارة الطلبات الواردة': 'Manage Incoming Orders',
  'تعديل متجر المعمل': 'Edit Lab Storefront',
  'الواردة للمعمل': 'Received by the lab',
  'الأطباء المتعاملون': 'Partner Dentists',
  'عرض كشف حساب كل طبيب': 'View each dentist’s statement',
  'إجمالي المدفوعات المحصلة': 'Total Payments Collected',
  'عمولة المنصة 2% فقط:': 'Platform commission, 2% only:',
  'رصيد المحفظة المتاح للسحب': 'Wallet Balance Available',
  'طلب سحب الأرباح ←': 'Request payout ←',
  'تحليل الإيرادات وصافي أرباح المعمل (ج.م)': 'Revenue & Lab Net Profit (EGP)',
  'مقارنة إجمالي المحصل من الأطباء بصافي المعمل (98%) بعد عمولة المنصة (2%)': 'Collected from dentists vs. lab net (98%) after the 2% platform commission',
  'مباشر · EGP': 'Live · EGP',
  'إجمالي قيمة الطلبات': 'Total Order Value',
  'المحصل من الأطباء': 'Collected from Dentists',
  'صافي أرباح المعمل (98%)': 'Lab Net Profit (98%)',
  'رصيد المحفظة للسحب': 'Wallet Balance',
  'توزيع الحالات ومراحل الإنتاج والأطباء': 'Cases by Production Stage & Dentist',
  'نسب الحالات حسب المرحلة الحالية وحجم تعاملات الأطباء': 'Case share by current stage and dentist volume',
  'سجل الأطباء': 'Dentist Log',
  'مراحل التصميم الرقمي (Design)': 'Digital Design stages',
  'مراحل التفريز الخماسي (Milling)': '5-axis Milling stages',
  'مراحل الطبقات والجليز': 'Layering & Glazing stages',
  'الأطباء المتعاملون مع المعمل حالياً': 'Dentists Currently Working with the Lab',
  'اضغط على أي طبيب لفتح كشف حسابه التفصيلي ومعاملاته المنفصلة.': 'Click any dentist to open their detailed statement and separate transactions.',
  'فتح حسابات الأطباء والمحفظة': 'Open Dentist Accounts & Wallet',
  'أحدث الطلبات ومراحل التنفيذ': 'Latest Orders & Production Stages',
  'قالب الأسعار الموحد لجميع المعامل': 'Unified price template for all labs',
  'إدارة وتعديل الـ Price List الخاصة بالمعمل': 'Manage and Edit the Lab Price List',
  'التصميم العام لقائمة الأسعار ثابت وموحّد في المنصة؛ يمكنك تعديل اسم الخدمة أو السعر مباشرةً داخل القالب أو إضافة بند جديد.': 'The price list layout is fixed and unified across the platform; edit a service name or price directly in the template, or add a new item.',
  '← العودة للإحصائيات': '← Back to Analytics',
  'معاينة في البروفايل العام': 'Preview on Public Profile',
  '+ إضافة خدمة جديدة داخل القالب الموحد': '+ Add a new service to the unified template',
  'اسم الخدمة': 'Service Name',
  'التصنيف في القالب': 'Template Category',
  'تركيبات الزركونيا والبورسلين (CROWN & BRIDGE)': 'Zirconia & Porcelain (CROWN & BRIDGE)',
  'التركيبات التجميلية والإيماكس (AESTHETIC & E.MAX)': 'Aesthetic & E.max (AESTHETIC & E.MAX)',
  'دعامات الزرعات والطباعة الرقمية (IMPLANTS & DIGITAL)': 'Implant Abutments & Digital Printing (IMPLANTS & DIGITAL)',
  'السعر للوحدة (ج.م)': 'Price per Unit (EGP)',
  'مدة التنفيذ التقديرية (أيام)': 'Estimated Turnaround (days)',
  'إضافة البند لقائمة الأسعار': 'Add Item to Price List',
  'إدارة متجر المعمل الرقمي': 'Digital Lab Storefront',
  'تخصيص ملف ومتجر المعمل (الشعار، Bio، الغلاف، الأجهزة، أعمال الشغل، والمواعيد)': 'Customize the Lab Profile & Storefront (logo, bio, cover, equipment, portfolio, hours)',
  'تحكم في متجرك بالكامل كما يظهر للأطباء في الدليل العام.': 'Control your storefront exactly as dentists see it in the public directory.',
  'معاينة المتجر العام ↗': 'Preview Public Storefront ↗',
  'معاينة حية لواجهة المتجر': 'Live storefront preview',
  '1. الهوية الأساسية، الـ Bio، ومواعيد العمل والتوصيل': '1. Identity, Bio, Working Hours & Delivery',
  'اسم المعمل التجاري (يحدد أحرف الشعار تلقائيًا)': 'Lab trade name (sets the logo initials automatically)',
  'مواعيد العمل الرسمية': 'Official working hours',
  'نبذة تعريفية عن المعمل وتخصصاته (Bio)': 'About the lab and its specialties (Bio)',
  'المدينة / المنطقة': 'City / Area',
  'العنوان التفصيلي للمعمل': 'Full lab address',
  'مناطق التغطية والمندوب (مفصولة بفاصلة)': 'Courier coverage areas (comma-separated)',
  'يوفر المعمل مندوب استلام وتسليم للعيادات': 'The lab offers courier pickup & delivery for clinics',
  'يتيح الاستلام المباشر من مقر المعمل': 'Allows direct pickup from the lab',
  'حفظ بيانات المتجر والـ Bio والمواعيد': 'Save Storefront, Bio & Hours',
  '2. الأجهزة والتقنيات المستخدمة في المعمل': '2. Equipment & Technology Used in the Lab',
  'أضف ماكينات التفريز، الماسحات الضوئية، أفران التلبيد، والطابعات ثلاثية الأبعاد ليراها الأطباء.': 'Add milling machines, scanners, sintering furnaces, and 3D printers for dentists to see.',
  '+ إضافة جهاز جديد للمعمل': '+ Add new equipment',
  'إضافة الجهاز': 'Add Equipment',
  '3. معرض منتجات الشغل والحالات المنفذة (Portfolio)': '3. Portfolio of Completed Work',
  'أضف نماذج شغلك مع درجة اللون (VITA Shade) والوصف الفني لتظهر في متجرك.': 'Add samples of your work with VITA shade and technical notes to show in your storefront.',
  '+ إضافة عمل جديد لمعرض المعمل': '+ Add new work to the portfolio',
  'إضافة لمعرض الأعمال': 'Add to Portfolio',

  // Lab orders & order details
  'جميع الطلبات الواردة للمعمل': 'All Incoming Lab Orders',
  'الخدمة والأسنان': 'Service & Teeth',
  '← العودة لطلبات المعمل': '← Back to Lab Orders',
  'إدارة مراحل الإنتاج (التصميم → التفريز → الطبقات → الجليز)': 'Production Stages (Design → Milling → Layering → Glazing)',
  'لا يُسمح بتخطي أي مرحلة مختارة؛ تبدأ كل مرحلة بعد اكتمال المرحلة السابقة لها.': 'Selected stages cannot be skipped; each stage starts after the previous one is complete.',
  'محادثة الحالة مع الطبيب': 'Case Chat with the Dentist',
  'ملفات الحالة ومخرجات المراحل': 'Case Files & Stage Outputs',
  'رفع مخرج': 'Upload Output',
  'إتمام المرحلة ✓': 'Complete Stage ✓',
  'بدء المرحلة': 'Start Stage',
  'رفع لمراجعة الطبيب': 'Submit for Dentist Review',
  'حالة الدفعات والمندوب': 'Payments & Courier Status',

  // Lab finance
  'تصفية الكشف': 'Filter Statement',
  'الأطباء وكشوف الحساب': 'Dentists & Statements',
  'المحفظة وسحب الفلوس': 'Wallet & Payouts',
  'إدارة الإعلانات': 'Manage Ads',
  'تصدير CSV': 'Export CSV',
  'إجمالي المحصل من الأطباء': 'Total Collected from Dentists',
  'قيمة الدفعات المؤكدة': 'Confirmed payment value',
  'عمولة المنصة (2% فقط)': 'Platform Commission (2% only)',
  'رسوم بوابة الدفع: 0 ج.م على المعمل': 'Payment gateway fees: 0 EGP to the lab',
  'تم تحويله سابقًا:': 'Previously transferred:',
  'قيد التحويل حاليًا:': 'Currently being transferred:',
  'كل الأطباء المتعاملين مع المعمل وحساب كل دكتور منفصل': 'All Partner Dentists, Each with a Separate Account',
  'اضغط على': 'Click',
  '"كشف حساب الطبيب التفصيلي"': '"Detailed Dentist Statement"',
  'لأي دكتور لعرض جميع طلباته ومدفوعاته والمتبقي عليه بشكل منفصل.': 'for any dentist to see all of their orders, payments, and remaining balance separately.',
  'عدد الطلبات (نشط / مكتمل)': 'Orders (active / completed)',
  'إجمالي قيمة الحالات': 'Total Case Value',
  'المدفوع للمعمل': 'Paid to Lab',
  'المتبقي على الطبيب': 'Dentist Balance Due',
  'حساب الطبيب المنفصل': 'Separate Dentist Account',
  'كشف حساب طبيب منفصل': 'Separate dentist statement',
  'طباعة كشف حساب الطبيب': 'Print Dentist Statement',
  'عدد حالات الطبيب': 'Dentist’s Cases',
  'إجمالي حساب الطبيب': 'Dentist Account Total',
  'المدفوع من الطبيب': 'Paid by Dentist',
  'الحالة واللون': 'Case & Shade',
  'إجمالي الطلب': 'Order Total',
  'صافي المعمل': 'Lab Net',
  'محفظة المعمل الرقمية': 'Digital lab wallet',
  'تفاصيل المحفظة وسحب الأرباح (Payout & Withdrawal)': 'Wallet Details & Payouts',
  'اسحب صافي أرباحك المحصلة (98% بعد عمولة الـ 2%) مباشرةً إلى حسابك البنكي أو InstaPay بدون أي رسوم بوابة دفع.': 'Withdraw your collected net earnings (98% after the 2% commission) straight to your bank account or InstaPay, with no gateway fees.',
  'الرصيد المتاح للسحب الفوري الآن': 'Available for instant withdrawal now',
  'قيد التحويل البنكي:': 'Bank transfer pending:',
  'تم تحويله لحسابك:': 'Transferred to your account:',
  'وسيلة سحب الأرباح': 'Payout method',
  'تحويل لحظي InstaPay': 'InstaPay instant transfer',
  'تحويل بنكي (IBAN / رقم حساب)': 'Bank transfer (IBAN / account number)',
  'محفظة شركات إلكترونية': 'Business e-wallet',
  'عنوان InstaPay (IPA) أو رقم الحساب البنكي / IBAN': 'InstaPay address (IPA) or bank account / IBAN',
  'طلب سحب الرصيد المتاح بالكامل الآن': 'Withdraw the Full Available Balance Now',
  'سجل طلبات السحب والتسويات البنكية': 'Payout Requests & Bank Settlements',
  'جميع دفعات السحب وحالتها ورقم مرجع التحويل البنكي المعتمد.': 'All payouts, their status, and the confirmed bank transfer reference.',
  'إجمالي المحصل': 'Total Collected',
  'صافي السحب': 'Net Payout',
  'بيانات التحويل / المرجع': 'Transfer Details / Ref',
  'لا توجد طلبات سحب سابقة بعد.': 'No payout requests yet.',
  'حالة الاشتراك الشهري للمعمل': 'Monthly Lab Subscription',
  'حالة الاشتراك: فترة تجريبية مجانية (شهر أول) —': 'Subscription status: free trial (first month) —',
  'تجديد الاشتراك الشهري (2,000 ج.م)': 'Renew Monthly Subscription (2,000 EGP)',
  'التفصيل المالي الشامل لكل طلب': 'Full Financial Breakdown per Order',
  'مركز التحكم الكامل في الإعلانات': 'Ads control center',
  'إطلاق حملة إعلانية ممولة جديدة': 'Launch a New Sponsored Campaign',
  'يظهر معملك في صدارة نتائج بحث الأطباء بشارة "مُموَّل" واضحة دون التأثير على مصداقية التقييم العضوي.': 'Your lab appears at the top of dentists’ search results with a clear "Sponsored" badge, without affecting organic rating credibility.',
  'عنوان الحملة الإعلانية': 'Campaign Title',
  'الميزانية المخصصة (ج.م)': 'Budget (EGP)',
  'مدة الحملة (بالأيام)': 'Campaign Length (days)',
  'إطلاق وتفعيل الإعلان الآن': 'Launch Ad Now',
  'مؤشر عائد الإعلانات (المشاهدات مقابل الزيارات والطلبات)': 'Ad Performance (impressions vs. visits and orders)',
  'إحصائيات حقيقية لعدد الأطباء الذين شاهدوا الإعلان ودخلوا البروفايل وطلبوا حالات': 'Real numbers of dentists who saw the ad, visited the profile, and placed cases',
  'ظهور الإعلان للأطباء': 'Ad impressions to dentists',
  'زيارات بروفايل المعمل': 'Lab profile visits',
  'أطباء تفاعلوا مع المتجر': 'Dentists who engaged with the storefront',
  'طلبات ناتجة عن الإعلان': 'Orders from the ad',
  'حملاتك الإعلانية الحالية والسابقة (تحكم كامل: تعديل، إيقاف/تشغيل، تمديد، وإحصائيات)': 'Your Current & Past Campaigns (edit, pause/resume, extend, stats)',
  'نشط الآن في صدارة الدليل ✓': 'Live at the top of the directory ✓',
  'موضع الظهور: DirectoryTop': 'Placement: DirectoryTop',
  'حملة دليل المعامل — القاهرة': 'Lab Directory Campaign — Cairo',
  '· الميزانية الكلية:': '· Total budget:',
  'إيقاف مؤقت للإعلان': 'Pause Ad',
  '+ تمديد المدة والميزانية': '+ Extend Duration & Budget',
  'تعديل عنوان الإعلان': 'Edit Ad Title',
  'شاهد الإعلان (Impressions)': 'Saw the ad (Impressions)',
  'دخلوا البروفايل (Profile Visits)': 'Visited the profile (Profile Visits)',
  'نسبة النقر (CTR)': 'Click-through rate (CTR)',
  'أطباء تواصلوا مع المعمل': 'Dentists who contacted the lab',
  'طلبات محققة خلال الحملة': 'Orders won during the campaign',

  // Doctor dashboard, orders, order details, finance
  'بوابة العيادة الرقمية وتتبع المراحل': 'Digital clinic portal & stage tracking',
  'تابع مراحل حالاتك الحالية واعتمد التصميمات المرفوعة من المعامل.': 'Follow your current cases stage by stage and approve designs uploaded by labs.',
  '+ إنشاء طلب حالة جديد': '+ New Case Order',
  'جميع الحالات المسجلة': 'All registered cases',
  'قيد التصنيع أو المراجعة': 'In Production or Review',
  'تتبع مباشر للمراحل': 'Live stage tracking',
  'المدفوع حتى الآن': 'Paid So Far',
  'عمولة الخدمة (1%):': 'Service fee (1%):',
  'دفعات مستحقة حاليًا': 'Payments Due Now',
  'مرتبطة باعتماد المراحل (20% / 80%)': 'Tied to stage approval (20% / 80%)',
  'مؤشر المدفوعات والدفعات المستحقة للعيادة (ج.م)': 'Clinic Payments & Amounts Due (EGP)',
  'متابعة الدفعات المسددة (20% دفعة أولى بعد التصميم + 80% عند الإتمام) وعمولة الخدمة (1%)': 'Payments made (20% after design + 80% on completion) and the 1% service fee',
  'دفعة أولى 20%': 'First payment 20%',
  'إجمالي الحالات المؤكدة': 'Total Confirmed Cases',
  'دفعات مستحقة السداد': 'Payments Due',
  'المتبقي عند الإتمام': 'Remaining on Completion',
  'توزيع حالات العيادة حسب مرحلة الإنتاج': 'Clinic Cases by Production Stage',
  'نسب الحالات في التصميم، التفريز، الطبقات، والجليز': 'Share of cases in design, milling, layering, and glazing',
  'تتبع الحالات': 'Track Cases',
  '01 · التصميم الرقمي (Design)': '01 · Digital Design',
  '02 · التفريز الخماسي (Milling)': '02 · 5-axis Milling',
  '03 · بناء الطبقات (Layering)': '03 · Layering',
  '04 · الجليز والتشطيب (Glazing)': '04 · Glazing & Finishing',
  'أحدث الطلبات ومراحل الإنتاج': 'Latest Orders & Production Stages',
  'كل الطلبات': 'All Orders',
  'المدفوع / المستحق': 'Paid / Due',
  'الإشعارات والتنبيهات': 'Notifications & Alerts',
  'بيانات المشروع والحالة': 'Project & Case Details',
  'جديد (New)': 'New',
  'تحميل ملف مسح (Load)': 'Load Scan File',
  'تكرار (Duplicate)': 'Duplicate',
  'حفظ وإرسال الطلب (Save)': 'Save & Send Order',
  'أدخل مرجع الحالة الداخلي بالعيادة وحدد المعمل والخدمة وأرقام الأسنان من المخطط التفاعلي.': 'Enter your clinic’s internal case reference, then choose the lab, service, and teeth on the interactive chart.',
  'تاريخ الإنشاء:': 'Created:',
  'موعد التسليم المطلوب (Due):': 'Requested due date:',
  'الطبيب / العيادة (Client)': 'Dentist / Clinic (Client)',
  'معرّف العيادة (ID)': 'Clinic ID',
  'اسم / مرجع الحالة (Case Name)': 'Case Name / Ref',
  'السعر الاسترشادي (ج.م)': 'Indicative Price (EGP)',
  'المعمل والفني المختص (Technician / Lab)': 'Lab / Technician',
  'معمل القاهرة الرقمي للتركيبات (القاهرة)': 'Cairo Digital Prosthetics Lab (Cairo)',
  'معمل النيل الدقيق CAD/CAM (الجيزة)': 'Nile Precision CAD/CAM Lab (Giza)',
  'أليكس سيرام ستوديو (الإسكندرية)': 'Alex Ceram Studio (Alexandria)',
  'معمل الدلتا الرقمي (المنصورة)': 'Delta Digital Lab (Mansoura)',
  'كود المعمل (ID)': 'Lab ID',
  'طريقة الاستلام والتسليم': 'Pickup & Delivery',
  'مندوب المعمل (استلام وتسليم للعيادة)': 'Lab courier (pickup & delivery to the clinic)',
  'تسليم واستلام مباشر من مقر المعمل': 'Direct drop-off & pickup at the lab',
  'عنوان العيادة': 'Clinic Address',
  'ملاحظات وتعليمات الحالة (Notes)': 'Case Notes & Instructions',
  'التعليمات الفنية للمعمل': 'Technical instructions for the lab',
  'البيانات ومراحل الإنتاج وحالة المشروع (Data and status)': 'Data, Production Stages & Status',
  'المشروع جاهز للحفظ والإرسال': 'Project ready to save and send',
  'مراحل الإنتاج المطلوبة (تُحفظ بالترتيب وتُربط بالدفعتين 20% بعد التصميم و80% عند الإتمام):': 'Required production stages (kept in order and tied to the 20% after design and 80% on completion payments):',
  '01 · التصميم (Design)': '01 · Design',
  '02 · التفريز (Milling)': '02 · Milling',
  '03 · الطبقات (Layering)': '03 · Layering',
  '04 · الجليز (Glazing)': '04 · Glazing',
  'لم يتم إرفاق ملف مسح أولي بعد (يمكنك الضغط على «تحميل ملف مسح» بالأعلى).': 'No initial scan attached yet (use "Load Scan File" above).',
  'إرسال الطلب للمعمل الآن ←': 'Send Order to the Lab Now ←',
  'التشخيص والخامات ومخطط الأسنان': 'Indication, Materials & Tooth Chart',
  'نوع التركيبة والخامة (Default)': 'Restoration type & material (Default)',
  'تاج زركونيا متعدد الطبقات (Multilayer Zirconia)': 'Multilayer Zirconia Crown',
  'تاج وفينير إيماكس (IPS e.max Press/CAD)': 'E.max Crown & Veneer (IPS e.max Press/CAD)',
  'دعامة زرعة تيتانيوم مخصصة (Custom Abutment)': 'Custom Titanium Implant Abutment',
  'جسر زركونيا تجميلي (Zirconia Bridge)': 'Aesthetic Zirconia Bridge',
  'واقي ليلي رقمي (3D Printed Splint)': 'Digital Night Guard (3D Printed Splint)',
  'تاج PMMA مؤقت طويل الأمد (PMMA Temp)': 'Long-term PMMA Temporary Crown',
  'حالة مريض (Patient)': 'Patient case',
  'تعدد الدعامات (Multi-die)': 'Multi-die',
  'الأسنان المحددة (': 'Selected teeth (',
  'القواطع العلوية (11, 21)': 'Upper centrals (11, 21)',
  'ابتسامة أمامية علوية (13–23)': 'Upper anterior smile (13–23)',
  'الأضراس الأولى (6s)': 'First molars (6s)',
  'مسح التحديد': 'Clear Selection',
  'درجة لون الأسنان (Tooth shade)': 'Tooth shade',
  'وضع المسح أو الطبعة (Scan mode)': 'Scan or impression mode',
  'مسح فموي رقمي (Intraoral Scan STL/PLY)': 'Intraoral scan (STL/PLY)',
  'موديل جبس واحد (One stone model only)': 'One stone model only',
  'فكين مع تسجيل العضة (Two models in occlusion)': 'Two models in occlusion',
  'طبعة سيليكون تقليدية (Silicone Impression)': 'Conventional silicone impression',
  'سجل طلبات الحالات السابقة والجارية': 'Past & Current Case Orders',
  'فتح الحالة': 'Open Case',
  '← العودة لقائمة الطلبات': '← Back to Orders',
  'عمولة الخدمة للطبيب (1%):': 'Dentist service fee (1%):',
  'الخط الزمني لمراحل التصنيع واعتماد الطبيب': 'Production Timeline & Dentist Approval',
  'محادثة الحالة الخاصة': 'Private Case Chat',
  'ملفات الحالة المحمية (STL / PLY / OBJ / صور / PDF)': 'Protected Case Files (STL / PLY / OBJ / images / PDF)',
  'رفع ملف': 'Upload File',
  'دفعات الحالة (20% دفعة أولى / 80% دفعة نهائية)': 'Case Payments (20% first / 80% final)',
  'سجل التدقيق غير القابل للتعديل': 'Tamper-proof Audit Log',
  'الحساب المالي للطبيب وإيصالات الدفع (بالجنيه المصري EGP)': 'Dentist Account & Payment Receipts (EGP)',
  'إجمالي قيمة الطلبات المؤكدة': 'Total Confirmed Order Value',
  'دفعات مؤكدة عبر الخادم': 'Payments confirmed by the server',
  'دفعات مستحقة السداد الآن': 'Payments Due Now',
  'الرصيد المتبقي': 'Remaining Balance',
  'يُستحق عند اكتمال المراحل': 'Due as stages complete',
  'جدول الدفعات حسب الطلب (20% دفعة أولى / 80% دفعة نهائية + 1% عمولة)': 'Payment Schedule per Order (20% first / 80% final + 1% fee)',
  'الخدمة والمعمل': 'Service & Lab',
  'إجمالي الطلب (+1% عمولة)': 'Order Total (+1% fee)',
  'سجل المعاملات والإيصالات': 'Transactions & Receipts',
  'مرجع المعاملة': 'Transaction Ref',
  'تاريخ التأكيد': 'Confirmed On',
};

const SUBSTRING_AR_TO_EN = [
  // Dashboard strings assembled at runtime (stage statuses, notifications, statements, counts)
  ['اعتماد مرحلة التصميم واستحقاق الدفعة الأولى', 'Design approved, first payment due'],
  ['قبول الطلب وتثبيت السعر', 'Order accepted, price locked'],
  ['كشف حساب المعاملات المنفصل:', 'Separate transaction statement:'],
  ['حالة الاشتراك:', 'Subscription status:'],
  ['قيمة التجديد الشهري:', 'Monthly renewal:'],
  ['تاريخ البدء:', 'Start date:'],
  ['ينتهي في:', 'Ends:'],
  ['شهر بعد التجربة', 'month after trial'],
  ['اقترح المعمل سعر', 'The lab proposed a price of'],
  ['بانتظار موافقتك.', 'awaiting your approval.'],
  ['تحديث مرحلة', 'Stage update:'],
  ['مقترح سعر', 'Price proposal'],
  ['للطلب', 'for order'],
  ['الحالة الجديدة:', 'New status:'],
  ['مدير المنصة', 'Platform Admin'],
  ['البريد:', 'Email:'],
  ['المحافظة:', 'Governorate:'],
  ['إجمالي الطلبات:', 'Total orders:'],
  ['مرجع:', 'Ref:'],
  ['لون ', 'Shade '],
  ['عمولة 1%', '1% fee'],
  ['مكتمل ومسلّم', 'Completed & delivered'],
  ['قيد التنفيذ', 'In progress'],
  ['لم تبدأ', 'Not started'],
  ['جسر زركونيا متعدد الطبقات', 'Multilayer Zirconia Bridge'],
  ['جسر زركونيا', 'Zirconia Bridge'],
  ['وحدات', 'units'],
  ['التصميم:', 'Design:'],
  ['التفريز:', 'Milling:'],
  ['الطبقات:', 'Layering:'],
  ['الجليز:', 'Glazing:'],
  ['التصميم ←', 'Design ←'],
  ['← التفريز', '← Milling'],
  ['← الطبقات', '← Layering'],
  ['← الجليز', '← Glazing'],
  ['نشط /', 'active /'],
  ['نشط)', 'active)'],
  ['مكتمل)', 'completed)'],
  ['الدفعة الأولى 20% والدفعة النهائية 80%', 'First payment 20% & final payment 80%'],
  ['20% عند اعتماد التصميم + 80% عند إتمام التصنيع', '20% on Design approval + 80% on final completion'],
  ['20% دفعة أولى عند اعتماد التصميم و80% عند إتمام التصنيع', '20% initial payment on Design approval & 80% on completion'],
  ['عمولة الخدمة للطبيب (1%)', 'Dentist Service Fee (1%)'],
  ['عمولة المنصة على المعمل: 2% (صافي المعمل 98%)', 'Lab Platform Commission: 2% (98% Lab Net)'],
  ['السعر المؤكد للحالة', 'Confirmed Case Price'],
  ['كشف حساب الطبيب التفصيلي', 'Detailed Dentist Statement'],
  ['العودة للوحة التحكم', 'Back to Dashboard'],
  ['مسجل الدخول كـ', 'Signed in as'],
  ['تقييمًا موثقًا', 'verified reviews'],
  ['إشعارات', 'Notifications'],
  ['مرحلة', 'Stage'],
  ['طلبات', 'Orders'],
  ['أطباء متعاملين', 'Partner Dentists'],
  ['أطباء', 'Dentists'],
  ['معامل', 'Labs'],
  ['حالة', 'case'],
  ['ج.م', 'EGP'],
  ['يوم عمل', 'business days'],
  ['أيام', 'days'],
  ['مرحبًا ', 'Welcome, '],
  ['لوحة ', 'Dashboard — '],
  ['طلب ', 'Order '],
  ['الدفعة 1 (20%)', 'Milestone 1 (20%)'],
  ['الدفعة 2 (80%)', 'Milestone 2 (80%)'],
  ['سداد الدفعة 1', 'Pay Milestone 1'],
  ['سداد الدفعة 2', 'Pay Milestone 2'],
  ['ادفع الآن', 'Pay Now'],
  ['لا توجد دفعة مستحقة', 'No payment due'],
  ['اعتماد التصميم ✓', 'Approve Design ✓'],
  ['طلب تعديل', 'Request Revision'],
  ['بدء المرحلة', 'Start Stage'],
  ['رفع لمراجعة الطبيب', 'Submit for Dentist Review'],
  ['إتمام المرحلة ✓', 'Complete Stage ✓'],
  // Generic labels last, so longer phrases above win first
  ['الطبيب:', 'Dentist:'],
  ['المعمل:', 'Lab:'],
];

// Count units after a number ("٨ حسابات", "٣٥٧ مشاهدة"); anchored to the digits so free text is untouched.
const COUNT_UNITS_AR_TO_EN = { 'حسابات': 'accounts', 'دفعات': 'batches', 'مشاهدة': 'views', 'زيارة': 'visits', 'دكاترة': 'dentists', 'طبيب': 'dentists' };
const COUNT_UNIT_RE = new RegExp('(\\d[\\d,.]*)\\s+(' + Object.keys(COUNT_UNITS_AR_TO_EN).join('|') + ')', 'g');

const PLACEHOLDER_AR_TO_EN = {
  'مثال: تاج زركونيا': 'e.g. Zirconia Crown',
  'ابحث باسم المعمل أو الخدمة (زركونيا، إيماكس)...': 'Search by lab name or service (Zirconia, E.max)...',
  'ابحث برقم الطلب أو اسم المريض...': 'Search by order # or patient ref...',
  'ابحث برقم الطلب أو المرجع...': 'Search by order # or reference...',
  'ابحث برقم الطلب أو اسم الطبيب...': 'Search by order # or dentist name...',
  'اكتب رسالة فنية بخصوص هذه الحالة...': 'Write a technical message regarding this case...',
  'اكتب اسم المريض أو مرجع الحالة...': 'Enter patient name or case reference...',
  'اكتب ملاحظات الحواف، الإطباق، أو متطلبات البورسلين...': 'Enter margin, occlusion, or porcelain instructions...',
  // Dashboards
  'ابحث برقم الطلب أو اسم الطبيب أو اسم الحالة...': 'Search by order #, dentist, or case name...',
  'ابحث برقم الطلب أو اسم الطبيب أو مرجع الحالة...': 'Search by order #, dentist, or case ref...',
  'ابحث برقم الطلب (مثال: LS-000187) أو اسم الحالة...': 'Search by order # (e.g. LS-000187) or case name...',
  'ابحث برقم الطلب (LS-000187) أو اسم الحالة أو المعمل...': 'Search by order # (LS-000187), case name, or lab...',
  'مثال: تاج زركونيا متجانس': 'e.g. Monolithic Zirconia Crown',
  'السبت – الخميس · 9:00 ص – 9:00 م': 'Sat – Thu · 9:00 AM – 9:00 PM',
  'مثال: 14 شارع عباس العقاد، مدينة نصر': 'e.g. 14 Abbas El Akkad St, Nasr City',
  'مدينة نصر, مصر الجديدة, التجمع الخامس, المعادي': 'Nasr City, Heliopolis, Fifth Settlement, Maadi',
  'اسم الجهاز (مثال: ماكينة تفريز 5 محاور)': 'Equipment name (e.g. 5-axis milling machine)',
  'الموديل (مثال: Roland DWX-52D / 3Shape E4)': 'Model (e.g. Roland DWX-52D / 3Shape E4)',
  'عنوان العمل (مثال: 6 فينير إيماكس)': 'Work title (e.g. 6 E.max veneers)',
  'اللون (مثال: BL2 / A1)': 'Shade (e.g. BL2 / A1)',
  'وصف المرحلة والتشطيب (مثال: تجميل قوس علوي مع جليز نهائي)': 'Stage & finish notes (e.g. full upper arch with final glaze)',
  'اكتب رسالة للطبيب...': 'Write a message to the dentist...',
  'cairo.lab@instapay أو EG89000...': 'cairo.lab@instapay or EG89000...',
  'مثال: عروض تركيبات الزركونيا والإيماكس الرقمية': 'e.g. Digital zirconia & E.max offers',
  'مثال: CASE-2026-108': 'e.g. CASE-2026-108',
  'عنوان الاستلام': 'Pickup address',
  'اكتب تعليمات الحواف (Margin line)، نقاط التماس، الإطباق، أو تصميم الابتسامة...': 'Write margin line, contacts, occlusion, or smile design instructions...',
  'اكتب رسالة للمعمل بخصوص هذه الحالة...': 'Write a message to the lab about this case...',
};

// English mode: Arabic-Indic digits, separators and ص/م times become Latin digits and AM/PM.
function latinizeNumbers(str) {
  return str
    .replace(/[\u0660-\u0669]/g, (d) => String(d.charCodeAt(0) - 0x0660))
    .replace(/\u066C/g, ',')
    .replace(/\u066B/g, '.')
    .replace(/(\d)\u060C/g, '$1,')
    .replace(/\u200F/g, '')
    .replace(/(\d{1,2}:\d{2}(?::\d{2})?)\s*\u0635/g, '$1 AM')
    .replace(/(\d{1,2}:\d{2}(?::\d{2})?)\s*\u0645(?![\u0600-\u06FF.])/g, '$1 PM');
}

export function getTheme() {
  return localStorage.getItem(THEME_KEY) === 'dark' ? 'dark' : 'light';
}

export function setTheme(theme) {
  const next = theme === 'dark' ? 'dark' : 'light';
  localStorage.setItem(THEME_KEY, next);
  document.documentElement.dataset.theme = next;
  updateToggleButtons();
}

export function getLang() {
  return localStorage.getItem(LANG_KEY) === 'en' ? 'en' : 'ar';
}

export function setLang(lang) {
  const next = lang === 'en' ? 'en' : 'ar';
  localStorage.setItem(LANG_KEY, next);
  document.documentElement.lang = next;
  document.documentElement.dir = next === 'en' ? 'ltr' : 'rtl';
  applyDomTranslations(document.body);
  updateToggleButtons();
}

function translateStringToEn(str) {
  const trimmed = str.trim();
  if (!trimmed) return str;
  if (EXACT_AR_TO_EN[trimmed]) {
    return latinizeNumbers(str.replace(trimmed, EXACT_AR_TO_EN[trimmed]));
  }
  let out = str;
  for (const [ar, en] of SUBSTRING_AR_TO_EN) {
    if (out.includes(ar)) {
      out = out.split(ar).join(en);
    }
  }
  out = latinizeNumbers(out);
  return out
    .replace(COUNT_UNIT_RE, (m, n, unit) => n + ' ' + COUNT_UNITS_AR_TO_EN[unit])
    .replace(/\b(\d+) (case|Stage)\b/g, (m, n, unit) => (n === '1' ? m : n + ' ' + (unit === 'case' ? 'cases' : 'stages')));
}

const originalTextMap = new WeakMap();
const originalPlaceholderMap = new WeakMap();

export function applyDomTranslations(root = document.body) {
  if (!root) return;
  const lang = getLang();

  root.querySelectorAll?.('input[placeholder], textarea[placeholder]').forEach((el) => {
    if (!originalPlaceholderMap.has(el)) {
      originalPlaceholderMap.set(el, el.getAttribute('placeholder') || '');
    }
    const orig = originalPlaceholderMap.get(el);
    const target = lang === 'en'
      ? (PLACEHOLDER_AR_TO_EN[orig] || translateStringToEn(orig))
      : orig;
    if (el.getAttribute('placeholder') !== target) {
      el.setAttribute('placeholder', target);
    }
  });

  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
    acceptNode(node) {
      const parent = node.parentElement;
      if (!parent) return NodeFilter.FILTER_REJECT;
      const tag = parent.tagName;
      if (tag === 'SCRIPT' || tag === 'STYLE' || tag === 'NOSCRIPT' || parent.closest('.pref-toggles')) {
        return NodeFilter.FILTER_REJECT;
      }
      return NodeFilter.FILTER_ACCEPT;
    },
  });

  let current;
  while ((current = walker.nextNode())) {
    const val = current.nodeValue || '';
    if (!val.trim()) continue;

    if (!originalTextMap.has(current)) {
      originalTextMap.set(current, val);
    }
    const orig = originalTextMap.get(current);
    const target = lang === 'en' ? translateStringToEn(orig) : orig;
    if (current.nodeValue !== target) {
      current.nodeValue = target;
    }
  }
}

function updateToggleButtons() {
  const theme = getTheme();
  const lang = getLang();
  const themeLabel = theme === 'dark'
    ? (lang === 'en' ? '☀️ Light' : '☀️ فاتح')
    : (lang === 'en' ? '🌙 Dark' : '🌙 داكن');
  const langLabel = lang === 'en' ? '🌐 عربي' : '🌐 EN';

  document.querySelectorAll('[data-pref-theme]').forEach((btn) => {
    const pressed = String(theme === 'dark');
    if (btn.getAttribute('aria-pressed') !== pressed) btn.setAttribute('aria-pressed', pressed);
    if (btn.textContent !== themeLabel) btn.textContent = themeLabel;
  });
  document.querySelectorAll('[data-pref-lang]').forEach((btn) => {
    const pressed = String(lang === 'en');
    if (btn.getAttribute('aria-pressed') !== pressed) btn.setAttribute('aria-pressed', pressed);
    if (btn.textContent !== langLabel) btn.textContent = langLabel;
  });
}

export function mountPreferenceToggles() {
  const targets = [
    document.querySelector('.site-header .nav-actions'),
    document.querySelector('.dash-topbar > div:last-child'),
    document.querySelector('.auth-top'),
  ].filter(Boolean);

  targets.forEach((container) => {
    if (container.querySelector('.pref-toggles')) return;
    const wrap = document.createElement('div');
    wrap.className = 'pref-toggles';
    wrap.innerHTML = `
      <button type="button" class="pref-btn" data-pref-theme title="تبديل المظهر الفاتح / الداكن · Toggle Dark Mode">🌙 داكن</button>
      <button type="button" class="pref-btn" data-pref-lang title="تبديل اللغة العربية / الإنجليزية · Switch Language">🌐 EN</button>
    `;
    wrap.querySelector('[data-pref-theme]').addEventListener('click', () => {
      setTheme(getTheme() === 'dark' ? 'light' : 'dark');
    });
    wrap.querySelector('[data-pref-lang]').addEventListener('click', () => {
      setLang(getLang() === 'en' ? 'ar' : 'en');
    });
    container.prepend(wrap);
  });

  updateToggleButtons();
}

export function initPreferences() {
  document.documentElement.dataset.theme = getTheme();
  const lang = getLang();
  document.documentElement.lang = lang;
  document.documentElement.dir = lang === 'en' ? 'ltr' : 'rtl';

  mountPreferenceToggles();
  if (lang === 'en') {
    applyDomTranslations(document.body);
  }
}

// Static front-end demo (GitHub Pages has no API): reveal the notice written in each page.
if (/\.github\.io$/.test(window.location.hostname)) {
  document.querySelectorAll('.demo-notice, .demo-accounts').forEach((el) => { el.hidden = false; });
}
