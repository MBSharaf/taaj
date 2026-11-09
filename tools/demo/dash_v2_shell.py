"""One-off conversion of the 9 dashboard pages to the v2 shell (sidebar groups + promo card, topbar, page header).
Idempotent: pages already marked dash-v2 are skipped. Run from the repo root: python tools/demo/dash_v2_shell.py"""
import re, pathlib

W = pathlib.Path(__file__).resolve().parents[2] / 'src' / 'LabStage.Api' / 'wwwroot'

SVG = {
    'menu': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M4 7h16M4 12h16M4 17h10"/></svg>',
    'search': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg>',
    'mail': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><rect x="3" y="5" width="18" height="14" rx="3"/><path d="m4 7 8 6 8-6"/></svg>',
    'bell': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M6 10a6 6 0 1 1 12 0c0 5 2 6 2 6H4s2-1 2-6"/><path d="M10 20a2 2 0 0 0 4 0"/></svg>',
    'help': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><circle cx="12" cy="12" r="9"/><path d="M9.5 9.5a2.5 2.5 0 1 1 3.5 2.3c-.6.3-1 .9-1 1.6V14M12 17h.01"/></svg>',
    'home': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><path d="m3 11 9-7 9 7v9a1 1 0 0 1-1 1h-5v-6H9v6H4a1 1 0 0 1-1-1z"/></svg>',
    'logout': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M15 4h3a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-3M10 8l-4 4 4 4M6 12h10"/></svg>',
    'plus': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><path d="M12 5v14M5 12h14"/></svg>',
    'spark': '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M12 2l2.2 6.8L21 11l-6.8 2.2L12 20l-2.2-6.8L3 11l6.8-2.2z"/></svg>',
}

ROLE = {
    'doctor': dict(search='/doctor-orders.html', mail='/doctor-orders.html', bell='/doctor-dashboard.html#notifications',
                   promo=('اطلب حالة جديدة', 'اختر المعمل وحدد الأسنان في أقل من دقيقة.', 'إنشاء طلب', '/doctor-orders.html'),
                   placeholder='ابحث باسم المريض أو رقم الطلب...'),
    'lab': dict(search='/lab-orders.html', mail='/lab-orders.html', bell='/lab-dashboard.html',
                promo=('زوّد ظهور معملك', 'حملة ممولة تضع معملك أول نتائج البحث.', 'إنشاء حملة', '/lab-finance.html#campaigns'),
                placeholder='ابحث برقم الطلب أو اسم الطبيب...'),
    'admin': dict(search=None, mail='#audit', bell='#audit',
                  promo=('تسويات المعامل', 'راجع المستحقات وأنشئ دفعات التسوية البنكية.', 'فتح التسويات', '#settlements'),
                  placeholder=''),
}

PAGES = {
    'doctor-dashboard': ('doctor', 'لوحة التحكم', 'تابع حالاتك ومراحلها ومدفوعاتها في مكان واحد.', ('حالة جديدة', '/doctor-orders.html'), ('استكشاف المعامل', '/labs')),
    'doctor-orders': ('doctor', 'الطلبات', 'أنشئ حالة جديدة وتابع كل طلباتك.', ('حالة جديدة', '#create-order-form'), ('المدفوعات', '/doctor-finance.html')),
    'doctor-finance': ('doctor', 'المدفوعات والفواتير', 'الدفعات المستحقة وسجل السداد لكل حالة بالجنيه المصري.', ('حالة جديدة', '/doctor-orders.html'), ('كل الطلبات', '/doctor-orders.html')),
    'doctor-order-details': ('doctor', 'تفاصيل الحالة', 'المراحل والمحادثة والملفات والدفعات.', ('حالة جديدة', '/doctor-orders.html'), ('كل الطلبات', '/doctor-orders.html')),
    'lab-dashboard': ('lab', 'لوحة التحكم', 'طلبات معملك وإنتاجه وأرباحه في نظرة واحدة.', ('خدمة جديدة', '/lab-dashboard.html#pricelist'), ('كل الطلبات', '/lab-orders.html')),
    'lab-orders': ('lab', 'الطلبات ومراحل الإنتاج', 'استقبل الطلبات وحدّث مراحل التصنيع.', ('خدمة جديدة', '/lab-dashboard.html#pricelist'), ('المحفظة', '/lab-finance.html#wallet')),
    'lab-finance': ('lab', 'الحسابات والمحفظة', 'كشوف الأطباء والمحفظة وسحب الأرباح والإعلانات.', ('حملة إعلانية', '/lab-finance.html#campaigns'), ('كل الطلبات', '/lab-orders.html')),
    'lab-order-details': ('lab', 'تفاصيل الحالة', 'قبول الطلب وتحديث المراحل والمحادثة مع الطبيب.', ('خدمة جديدة', '/lab-dashboard.html#pricelist'), ('كل الطلبات', '/lab-orders.html')),
    'admin-dashboard': ('admin', 'لوحة التحكم', 'مؤشرات المنصة وتوثيق المعامل والتسويات.', ('دفعة تسوية', '#settlements'), ('الإعدادات', '#settings')),
}

def ic(name):
    return f'<span class="nav-ic" aria-hidden="true">{SVG[name]}</span>'

for name, (role, title, sub, primary, outline) in PAGES.items():
    f = W / f'{name}.html'
    s = f.read_text(encoding='utf-8')
    if 'dash-v2' in s:
        print('skip', name); continue
    R = ROLE[role]

    s = s.replace('<body class="page-dash"', '<body class="page-dash dash-v2"', 1)
    s = re.sub(r'(<link rel="stylesheet" href="/assets/css/glass\.css\?v=\d+">)', r'\1\n  <link rel="stylesheet" href="/assets/css/dash-v2.css?v=1">', s, count=1)
    s = re.sub(r'dashboard\.js\?v=(\d+)', lambda m: f'dashboard.js?v={int(m.group(1)) + 1}', s)

    # --- sidebar ---
    s = re.sub(r'<aside class="dash-sidebar"', '<aside class="dash-sidebar" id="dash-sidebar"', s, count=1)
    s = re.sub(r'<nav class="dash-nav"[^>]*>', '<p class="dash-nav-label">القائمة</p>\n      <nav class="dash-nav" aria-label="القائمة">', s, count=1)
    # count badge on the orders link
    s = re.sub(r'(<a href="/(?:doctor|lab)-orders\.html"[^>]*>)([^<]*)(</a>)', r'\1\2<span class="nav-badge num" data-nav-count hidden></span>\3', s, count=1)
    general = ('\n      <p class="dash-nav-label">عام</p>\n      <nav class="dash-nav" aria-label="عام">\n'
               f'        <a href="/contact.html">{ic("help")}المساعدة والدعم</a>\n'
               f'        <a href="/">{ic("home")}الموقع الرئيسي</a>\n'
               f'        <button type="button" id="logout-btn">{ic("logout")}تسجيل الخروج</button>\n      </nav>')
    aside = s[s.index('<aside'):s.index('</aside>')]
    nav_end = aside.index('</nav>') + len('</nav>')
    new_aside = aside[:nav_end] + general + aside[nav_end:]
    new_aside = re.sub(r'\s*<div class="dash-sidebar-foot">.*?</button>\s*</div>', '', new_aside, flags=re.S)   # old foot (user name + logout)
    t, d, b, h = R['promo']
    promo = (f'\n    <div class="dash-promo">\n      <span class="dash-promo-icon" aria-hidden="true">{SVG["spark"]}</span>\n'
             f'      <p class="dash-promo-title">{t}</p>\n      <p class="dash-promo-text">{d}</p>\n'
             f'      <a class="dash-promo-btn" href="{h}">{b}</a>\n    </div>\n  ')
    new_aside = new_aside.rstrip() + promo
    s = s.replace(aside, new_aside, 1)
    s = s.replace('</aside>', '</aside>\n  <button class="dash-scrim" type="button" aria-label="إغلاق القائمة" hidden></button>', 1)
    assert s.count('id="logout-btn"') == 1, name

    # --- topbar ---
    hs, he = s.index('<header class="dash-topbar">'), s.index('</header>', s.index('<header class="dash-topbar">')) + len('</header>')
    old = s[hs:he]
    fm = re.search(r'<form class="dash-search"(.*?)>\s*(<input[^>]*>)\s*(<button[^>]*>.*?</button>)\s*</form>', old, re.S)
    if fm:
        form = f'<form class="dash-search"{fm.group(1)}>\n        {SVG["search"]}\n        {fm.group(2)}\n        <kbd class="dash-kbd">⌘ F</kbd>\n        {fm.group(3)}\n      </form>'
    elif R['search']:
        form = (f'<form class="dash-search" action="{R["search"]}" method="get" role="search">\n        {SVG["search"]}\n'
                f'        <input class="input" name="q" type="search" placeholder="{R["placeholder"]}" aria-label="بحث">\n'
                f'        <kbd class="dash-kbd">⌘ F</kbd>\n        <button class="btn btn-primary btn-sm" type="submit">بحث</button>\n      </form>')
    else:
        form = '<span class="dash-search-spacer"></span>'
    toolbar = ''
    tb = re.search(r'<div style="display:flex;gap:var\(--sp-2\);flex-wrap:wrap">(.*?)</div>', old, re.S)
    if tb:
        toolbar = f'\n    <div class="dash-toolbar">{tb.group(1)}</div>'
    new_header = f'''<header class="dash-topbar">
      <button class="dash-menu-btn" type="button" aria-controls="dash-sidebar" aria-expanded="false" aria-label="فتح القائمة">{SVG["menu"]}</button>
      {form}
      <div class="dash-top-actions">
        <a class="dash-icon-btn" href="{R["mail"]}" aria-label="الرسائل والطلبات">{SVG["mail"]}</a>
        <a class="dash-icon-btn" href="{R["bell"]}" aria-label="الإشعارات">{SVG["bell"]}<span class="dash-icon-dot num" id="notif-count">0</span></a>
        <div class="dash-profile"><span class="dash-avatar" id="top-user-avatar" aria-hidden="true">ت</span><span class="dash-profile-text"><strong id="top-user-name">...</strong><span id="top-user-email"></span></span></div>
      </div>
    </header>

    <div class="dash-pagehead">
      <div>
        <h1>{title}</h1>
        <p>{sub}</p>
      </div>
      <div class="dash-pagehead-actions">
        <a class="pill-btn pill-btn-primary" href="{primary[1]}">{SVG["plus"]}{primary[0]}</a>
        <a class="pill-btn pill-btn-outline" href="{outline[1]}">{outline[0]}</a>
      </div>
    </div>{toolbar}'''
    # drop any old notification pill elsewhere (ids must stay unique)
    s = s[:hs] + new_header + s[he:]
    assert s.count('id="notif-count"') == 1 and s.count('id="top-user-name"') == 1, name
    f.write_text(s, encoding='utf-8')
    print('converted', name)
