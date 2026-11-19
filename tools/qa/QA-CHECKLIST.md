# Taaj — UI / UX QA checklist

Run it after any change to pages, CSS or translations, and before every release.

## One command

```powershell
# 1) start the site with demo data (Development), in src/LabStage.Api:
$env:ASPNETCORE_ENVIRONMENT="Development"; dotnet run --no-launch-profile --urls http://localhost:5080
# 2) in another terminal, from the repo root (first time only: cd tools/qa; npm i playwright-core axe-core):
node tools/qa/ui_matrix.js
```

Results: `tools/qa/out/ui-matrix.md` (summary + every issue), `tools/qa/out/ui-matrix.json` (all cells),
screenshots at 390 px and 1440 px in `tools/qa/out/shots/`. Takes about 30 minutes.
Faster partial runs: `ONLY=doctor` (roles), `PAGES=finance` (page name contains), `W=390,1440`, `LANGS=en`,
`THEMES=dark`, `AXE=0`, `SHOTS=0`. `BASE=` points at another server (for example an old copy, to compare).

## The matrix

- **Roles and pages**
  - public: home, labs, lab profile, how it works, about, contact, login, register, forgot/reset password, 4 legal pages
  - dentist: dashboard, orders + new case, order details, finance, clinics, account, test payment
  - lab owner: dashboard, orders, order details, finance, settings, account, storefront
  - lab technician (invited staff account, created automatically): dashboard, orders, order details, account
  - admin: dashboard (all sections), account
- **Languages**: Arabic (RTL) and English (LTR)
- **Themes**: light and dark
- **Widths**: 360, 390 (phones), 768 (tablet), 1024, 1440 (desktop)

## What every cell checks

1. **Layout**: no sideways scrolling; lists the element that sticks out.
2. **Translation**: in English, no Arabic left in visible text, placeholders, tooltips or screen-reader labels.
   Text that came from the server (names, addresses, chat) is user data and is counted separately.
3. **Colour contrast (WCAG AA)**: 4.5:1 for text, 3:1 for large text, measured against what is really behind the
   text (all screens of the page, both themes).
4. **Tap targets on phones**: every button, link and field at least 24 px (aim for 44 px).
5. **Mobile menu (360/390/768)**: opens inside the screen, scrolls, has a visible close button, has a backdrop,
   the page behind does not scroll, closes with Esc / backdrop / choosing a link; the bottom bar never covers content.
6. **Errors**: no JavaScript errors, no failed requests (4xx/5xx).
7. **Accessibility (axe-core, 390 + 1440)**: no critical or serious violations.

## Visual review (by eye, with the screenshots)

- Colours come from the theme tokens (`--d-*` in dash-v2.css, `--ink`, `--bg`… in tokens.css), never fixed hex
  values that only work in one theme.
- Same spacing for the same kind of element; cards line up; nothing touches the screen edge on phones.
- Dark theme uses the Taaj navy surfaces, not grey or black.
- RTL and LTR mirror correctly (icons, arrows, drawer side, number direction).
- States: empty lists, long names, many rows (paging), validation errors, finance locked/unlocked,
  notification panel, chat, modals and toasts.

## Done means

Zero overflow, zero drawer issues, zero JS errors / failed requests, zero untranslated interface text,
zero serious axe issues; any remaining contrast item is listed with a reason.
