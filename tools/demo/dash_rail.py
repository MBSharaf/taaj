"""Adds the icon-rail markup to the 9 dashboard pages: logo mark, pin button, sliding active indicator, dashed "+" button,
and the mobile bottom bar. Idempotent (skips pages that already have class="dash-bottombar")."""
import pathlib, re

W = pathlib.Path(__file__).resolve().parents[2] / 'src' / 'LabStage.Api' / 'wwwroot'
I = {
    'home': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linejoin="round"><rect x="3" y="3" width="7.5" height="7.5" rx="2"/><rect x="13.5" y="3" width="7.5" height="7.5" rx="2"/><rect x="3" y="13.5" width="7.5" height="7.5" rx="2"/><rect x="13.5" y="13.5" width="7.5" height="7.5" rx="2"/></svg>',
    'orders': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linejoin="round"><path d="M7.5 4.5c2-1.6 3.4-1 4.5-.3 1.1-.7 2.5-1.3 4.5.3 2 1.7 1.6 5.2.6 7.9-.7 2-1 4.3-1.9 5.9-.6 1-1.7 1-2.1-.2l-.9-3.3c-.2-.8-1.5-.8-1.7 0l-.9 3.3c-.4 1.2-1.5 1.2-2.1.2-.9-1.6-1.2-3.9-1.9-5.9-1-2.7-1.4-6.2.6-7.9z"/></svg>',
    'wallet': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round"><rect x="3" y="6" width="18" height="13" rx="3"/><path d="M3 10h18M16 14.5h2"/></svg>',
    'labs': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><path d="m15.5 8.5-2 5-5 2 2-5z"/></svg>',
    'shield': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linejoin="round"><path d="M12 3 5 6v6c0 4.2 3 7.8 7 9 4-1.2 7-4.8 7-9V6z"/><path d="m9 12 2 2 4-4"/></svg>',
    'gear': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9"><circle cx="12" cy="12" r="3"/><path d="M19 12a7 7 0 0 0-.1-1.2l2-1.6-2-3.4-2.4.9a7 7 0 0 0-2-1.2L14 3h-4l-.5 2.5a7 7 0 0 0-2 1.2l-2.4-.9-2 3.4 2 1.6a7 7 0 0 0 0 2.4l-2 1.6 2 3.4 2.4-.9a7 7 0 0 0 2 1.2L10 21h4l.5-2.5a7 7 0 0 0 2-1.2l2.4.9 2-3.4-2-1.6c.1-.4.1-.8.1-1.2z"/></svg>',
    'more': '<svg viewBox="0 0 24 24" fill="currentColor"><circle cx="5" cy="12" r="2"/><circle cx="12" cy="12" r="2"/><circle cx="19" cy="12" r="2"/></svg>',
    'plus': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><path d="M12 5v14M5 12h14"/></svg>',
    'pin': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="m9 6 6 6-6 6"/></svg>',
}
# bottom bar: (label, href, icon, match) x2, plus, (label, href, icon, match) x1, more
ROLES = {
    'doctor': ([('الرئيسية', '/doctor-dashboard.html', 'home', 'doctor-dashboard'), ('الطلبات', '/doctor-orders.html', 'orders', 'doctor-order')],
               ('حالة جديدة', '/doctor-orders.html#create-order-form'),
               [('المدفوعات', '/doctor-finance.html', 'wallet', 'doctor-finance')]),
    'lab': ([('الرئيسية', '/lab-dashboard.html', 'home', 'lab-dashboard'), ('الطلبات', '/lab-orders.html', 'orders', 'lab-order')],
            ('خدمة جديدة', '/lab-dashboard.html#pricelist'),
            [('المحفظة', '/lab-finance.html#wallet', 'wallet', 'lab-finance')]),
    'admin': ([('الرئيسية', '/admin-dashboard.html#overview', 'home', 'admin-dashboard'), ('المعامل', '/admin-dashboard.html#labs', 'shield', '')],
              ('دفعة تسوية', '/admin-dashboard.html#settlements'),
              [('الإعدادات', '/admin-dashboard.html#settings', 'gear', '')]),
}
PROMO = {'doctor': ('اطلب حالة جديدة', '/doctor-orders.html'), 'lab': ('إنشاء حملة', '/lab-finance.html#campaigns'), 'admin': ('فتح التسويات', '#settlements')}

for f in sorted(W.glob('*.html')):
    name = f.stem
    role = name.split('-')[0]
    if role not in ROLES or 'dash-v2' not in f.read_text(encoding='utf-8'):
        continue
    s = f.read_text(encoding='utf-8')
    if 'class="dash-bottombar"' in s:
        print('skip', name); continue
    # logo mark (shown when the rail is collapsed)
    s = re.sub(r'(<a class="brand"[^>]*>)', r'\1<img class="brand-rail-mark" src="/assets/brand/taaj-icon.svg" alt="" width="44" height="44">', s, count=1)
    # pin button right after the brand link
    i = s.index('</a>', s.index('<a class="brand"')) + 4
    s = s[:i] + f'\n      <button class="dash-pin" type="button" aria-pressed="false" aria-label="تثبيت القائمة مفتوحة">{I["pin"]}</button>' + s[i:]
    # sliding active indicator inside the main nav
    s = s.replace('<nav class="dash-nav" aria-label="القائمة">', '<nav class="dash-nav dash-nav-main" aria-label="القائمة">\n        <span class="rail-indicator" aria-hidden="true"></span>', 1)
    # dashed "+" (collapsed rail) before the promo card
    label, href = PROMO[role]
    s = s.replace('    <div class="dash-promo">', f'    <a class="dash-rail-plus" href="{href}" aria-label="{label}">{I["plus"]}</a>\n    <div class="dash-promo">', 1)
    # mobile bottom bar
    left, (plabel, phref), right = ROLES[role]
    def item(lbl, h, ic, match):
        cur = ' aria-current="page"' if match and name.startswith(match) else ''
        return f'    <a class="bb-item" href="{h}"{cur}>{I[ic]}<span class="bb-label">{lbl}</span></a>\n'
    bar = ('  <nav class="dash-bottombar" aria-label="القائمة السريعة">\n    <span class="bb-indicator" aria-hidden="true"></span>\n'
           + ''.join(item(*x) for x in left)
           + f'    <a class="bb-plus" href="{phref}" aria-label="{plabel}">{I["plus"]}</a>\n'
           + ''.join(item(*x) for x in right)
           + f'    <button class="bb-item bb-more dash-menu-btn" type="button" aria-controls="dash-sidebar" aria-expanded="false">{I["more"]}<span class="bb-label">المزيد</span></button>\n  </nav>\n')
    s = s.replace('<button class="dash-scrim"', bar + '  <button class="dash-scrim"', 1)
    f.write_text(s, encoding='utf-8')
    print('rail markup', name)
