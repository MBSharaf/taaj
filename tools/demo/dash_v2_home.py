"""Writes the v2 home grid (KPI row, analytics / reminder / list, team / gauge / timer) into the doctor, lab and admin
dashboards. Text and layout are HTML; assets/js/pages/dash-home.js only fills the ids. Idempotent (skips pages that
already contain id="home-bars")."""
import pathlib, re

W = pathlib.Path(__file__).resolve().parents[2] / 'src' / 'LabStage.Api' / 'wwwroot'
ARROW = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M7 17 17 7M9 7h8v8"/></svg>'
PLAY = '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M8 5.5v13l10.5-6.5z"/></svg>'
STOP = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><path d="M20 12a8 8 0 1 1-2.3-5.6M20 4v5h-5"/></svg>'
OPEN = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="5" width="18" height="15" rx="3"/><path d="M3 10h18M8 3v4M16 3v4"/></svg>'
DAYS = ['س', 'ح', 'ن', 'ث', 'ر', 'خ', 'ج']  # Saturday .. Friday

def kpi(i, label, href, trend, primary=False):
    return f'''      <article class="kpi-card{' kpi-card-primary' if primary else ''}">
        <div class="kpi-head"><span class="kpi-label">{label}</span><a class="kpi-arrow" href="{href}" aria-label="{label}">{ARROW}</a></div>
        <strong class="kpi-value num" id="kpi-{i}">0</strong>
        <p class="kpi-trend">{trend}</p>
      </article>'''

def trend(i, text):
    return f'<span class="kpi-chip" aria-hidden="true">↗</span><span class="num" id="kpi-{i}-n">0</span> <span>{text}</span>'

def sub(text):
    return f'<span>{text}</span>'

def home(c):
    bars = '\n'.join(f'          <div class="pill-col"><span class="pill-bar is-empty" data-day="{i}"><span class="pill-tip" hidden></span></span><span class="pill-day">{d}</span></div>' for i, d in enumerate(DAYS))
    kpis = '\n'.join(kpi(i + 1, *k, primary=i == 0) for i, k in enumerate(c['kpis']))
    return f'''<section class="kpi-row" id="overview" aria-label="ملخص">
{kpis}
    </section>

    <div class="home-row home-row-2">
      <section class="dcard dcard-analytics">
        <div class="dcard-head"><h2>{c['chart']}</h2><span class="dcard-sub">{c['chart_sub']}</span></div>
        <div class="pill-chart" id="home-bars" role="img" aria-label="{c['chart']}">
{bars}
        </div>
      </section>
      <section class="dcard dcard-reminder">
        <h2>{c['rem']}</h2>
        <div>
          <p class="reminder-title" id="rem-title">{c['rem_empty']}</p>
          <p class="reminder-time" id="rem-time"></p>
        </div>
        <a class="pill-btn pill-btn-dark" id="rem-link" href="{c['rem_href']}">{OPEN}{c['rem_btn']}</a>
      </section>
      <section class="dcard dcard-list">
        <div class="dcard-head"><h2>{c['list']}</h2><a class="pill-btn pill-btn-outline pill-btn-sm" href="{c['list_href']}">{c['list_btn']}</a></div>
        <ul class="mini-list" id="home-list"><li class="muted">جاري التحميل...</li></ul>
      </section>
    </div>

    <div class="home-row home-row-3">
      <section class="dcard dcard-team">
        <div class="dcard-head"><h2>{c['team']}</h2><a class="pill-btn pill-btn-outline pill-btn-sm" href="{c['team_href']}">{c['team_btn']}</a></div>
        <ul class="team-list" id="home-team"><li class="muted">جاري التحميل...</li></ul>
      </section>
      <section class="dcard dcard-gauge">
        <h2>{c['gauge']}</h2>
        <div class="gauge">
          <svg viewBox="0 0 200 112" aria-hidden="true">
            <defs><pattern id="g-hatch" width="6" height="6" patternUnits="userSpaceOnUse" patternTransform="rotate(45)"><rect width="6" height="6" fill="var(--d-panel)"/><rect width="2.2" height="6" fill="var(--d-hatch)"/></pattern></defs>
            <path class="g-track" d="M18 100 A82 82 0 0 1 182 100" fill="none" stroke-width="26" stroke-linecap="round"/>
            <path id="g-pending" d="M18 100 A82 82 0 0 1 182 100" fill="none" stroke="url(#g-hatch)" stroke-width="26" stroke-linecap="round" pathLength="100" stroke-dasharray="0 100"/>
            <path class="g-progress" id="g-progress" d="M18 100 A82 82 0 0 1 182 100" fill="none" stroke-width="26" stroke-linecap="round" pathLength="100" stroke-dasharray="0 100"/>
            <path class="g-done" id="g-done" d="M18 100 A82 82 0 0 1 182 100" fill="none" stroke-width="26" stroke-linecap="round" pathLength="100" stroke-dasharray="0 100"/>
          </svg>
          <div class="gauge-center"><strong class="num" id="g-pct">0%</strong><span>{c['gauge_label']}</span></div>
        </div>
        <ul class="gauge-legend"><li><i class="lg-done"></i>{c['legend'][0]}</li><li><i class="lg-progress"></i>{c['legend'][1]}</li><li><i class="lg-pending"></i>{c['legend'][2]}</li></ul>
      </section>
      <section class="dcard dcard-timer">
        <h2>{c['timer']}</h2>
        <p class="timer-digits num" id="timer-digits">00:00:00</p>
        <p class="timer-sub" id="timer-sub">{c['timer_empty']}</p>
        <div class="timer-actions">
          <a class="timer-btn" id="timer-open" href="{c['rem_href']}" aria-label="{c['timer_open']}">{PLAY}</a>
          <button class="timer-btn timer-btn-alt" type="button" id="timer-refresh" aria-label="تحديث">{STOP}</button>
        </div>
      </section>
    </div>
'''

DOCTOR = dict(
    kpis=[('إجمالي الطلبات', '/doctor-orders.html', trend(1, 'طلب جديد هذا الشهر')),
          ('قيد التصنيع', '/doctor-orders.html', sub('تتابع مراحلها لحظة بلحظة')),
          ('بانتظار موافقتك', '/doctor-orders.html', sub('تصميمات أو أسعار تحتاج ردك')),
          ('تم تسليمها', '/doctor-orders.html', trend(4, 'هذا الشهر'))],
    chart='تحليل الطلبات', chart_sub='طلباتك المرسلة هذا الأسبوع',
    rem='التذكير', rem_empty='لا توجد مهام عاجلة الآن', rem_btn='فتح الحالة', rem_href='/doctor-orders.html',
    list='أحدث الطلبات', list_btn='+ جديد', list_href='/doctor-orders.html',
    team='المعامل التي تتعامل معها', team_btn='+ معمل', team_href='/labs',
    gauge='نسبة الإنجاز', gauge_label='من الحالات مكتملة', legend=('مكتملة', 'قيد التنفيذ', 'بانتظار'),
    timer='الوقت المتبقي للتسليم', timer_empty='لا يوجد موعد تسليم قريب', timer_open='فتح الحالة',
)
LAB = dict(
    kpis=[('طلبات جديدة', '/lab-orders.html', trend(1, 'وصلت هذا الأسبوع')),
          ('قيد التصنيع', '/lab-orders.html', sub('مراحل يعمل عليها المعمل الآن')),
          ('أرباح هذا الشهر', '/lab-finance.html#wallet', sub('صافي بعد عمولة المنصة')),
          ('رصيد متاح للسحب', '/lab-finance.html#wallet', sub('يمكنك سحبه من المحفظة'))],
    chart='الطلبات الواردة', chart_sub='طلبات وصلت للمعمل هذا الأسبوع',
    rem='المهمة التالية', rem_empty='لا توجد مهام عاجلة الآن', rem_btn='إدارة الحالة', rem_href='/lab-orders.html',
    list='أحدث الطلبات', list_btn='كل الطلبات', list_href='/lab-orders.html',
    team='الأطباء المتعاملون', team_btn='كشوف الحساب', team_href='/lab-finance.html#dentists',
    gauge='إنجاز المراحل', gauge_label='من مراحل الإنتاج مكتملة', legend=('مكتملة', 'قيد التنفيذ', 'لم تبدأ'),
    timer='الوقت المتبقي للتسليم', timer_empty='لا يوجد موعد تسليم قريب', timer_open='فتح الحالة',
)
ADMIN = dict(
    kpis=[('المستخدمون', '#labs', trend(1, 'حساب جديد هذا الشهر')),
          ('المعامل', '#labs', trend(2, 'معمل موثّق')),
          ('الطلبات', '#audit', trend(3, 'طلب هذا الشهر')),
          ('إيراد المنصة', '#settlements', sub('عمولات واشتراكات المعامل'))],
    chart='طلبات المنصة', chart_sub='الطلبات المسجلة هذا الأسبوع',
    rem='بانتظار المراجعة', rem_empty='لا توجد معامل بانتظار التوثيق', rem_btn='مراجعة المعامل', rem_href='#labs',
    list='بانتظار الموافقة', list_btn='عرض الكل', list_href='#labs',
    team='أحدث التسجيلات', team_btn='دليل المعامل', team_href='/labs',
    gauge='توثيق المعامل', gauge_label='من المعامل موثّقة', legend=('موثّقة', 'قيد المراجعة', 'غير موثّقة'),
    timer='حتى تسوية نهاية الشهر', timer_empty='', timer_open='فتح التسويات',
)
ADMIN['timer_empty'] = 'موعد إنشاء دفعات التسوية للمعامل'

def patch(name, start_marker, end_finder, block, extra=''):
    f = W / f'{name}.html'
    s = f.read_text(encoding='utf-8')
    if 'id="home-bars"' in s:
        print('skip', name); return
    a = s.index(start_marker)
    b = end_finder(s, a)
    s = s[:a] + block + extra + s[b:]
    f.write_text(s, encoding='utf-8')
    print('home written', name)

NOTIF = '''
    <section class="dcard" id="notifications">
      <div class="dcard-head"><h2>الإشعارات والتنبيهات</h2></div>
      <div id="notif-list" class="notif-list"><p class="muted">جاري تحميل الإشعارات...</p></div>
    </section>
  '''
patch('doctor-dashboard', '<section class="greeting-banner">', lambda s, a: s.index('</main>', a), home(DOCTOR), NOTIF)
patch('lab-dashboard', '<section class="greeting-banner">',
      lambda s, a: s.rindex('<section class="card">', a, s.index('أحدث الطلبات ومراحل التنفيذ', a)), home(LAB), '\n      ')

def admin_end(s, a):
    i = s.index('id="admin-ops-chart"', a)
    i = s.index('</section>', i) + len('</section>')
    return s.index('</div>', i) + len('</div>')
patch('admin-dashboard', '<section class="stats-grid" id="overview">', admin_end, home(ADMIN))
