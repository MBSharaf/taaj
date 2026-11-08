# Taaj — design system

**Taaj (تاج)** is the platform that connects dentists with dental labs in Egypt and the Arab world: orders, cases, stage reviews and payments in one place. The name means *crown*, the piece a dental lab is best known for, and the brand promise is the same: precise work, delivered with confidence.

Domain: **taajlab.com**. Arabic is the default language (RTL); English is the second.

## Logo

The idea: **the dot is the tooth.** In the English wordmark the dot of the **j** is replaced by the tooth-crown mark; in the Arabic wordmark the two dots of **ت** become the same mark (its two roots stand for the two dots). One symbol, both scripts.

- `taaj-lockup-*` — bilingual lockup, English + Arabic. Use on the landing page, decks, invoices.
- `taaj-wordmark-en-*` / `taaj-wordmark-ar-*` — single-language wordmarks. Use the Arabic one when the UI is in Arabic.
- `taaj-icon.svg` — app icon / favicon (white mark on the cobalt tile, `radius-xl` scaled).
- `taaj-mark.svg` — the tooth-crown mark alone, for avatars, loaders, watermarks.
- `-light` files go on light grounds (`bg`, `surface`); `-dark` files go on `abyss` / `night`.

Rules: keep clear space equal to the mark's height on every side. Minimum size: wordmark 72px wide, icon 16px. Never recolor the wordmark ink to a bright blue, never outline it, never put the mark gradient on the letters, never rotate or stretch.

## Voice

Calm, precise and professional, like a good lab technician. Short sentences. Egyptian-friendly Modern Standard Arabic in the UI (not slang), plain English.

- Do: «تم إرسال التصميم للمراجعة» / "Design sent for review".
- Do: lead with the case and the next step: «الحالة #1042 — في انتظار موافقتك على التصميم».
- Don't: exclamation marks, emoji in UI text, hype ("الأفضل في مصر!!").
- Money is always EGP with the amount in `mono` (`amount` style): ‎`1,250.00 ج.م`.

## Visual foundations

- **Colour.** Palette "Lagoon": cobalt `primary` on a pale ice `bg` in light mode, `abyss` ground in dark mode. `azure` and `aqua` are light, not text: glows, the mark gradient, charts. One `primary` button per view. Status colours (`success`, `warning`, `danger`) only ever mean status and always sit on their `-soft` fill.
- **Material.** Glassmorphism is the base surface: `surface-glass` + `glass-border` + backdrop blur 22px + `shadow-glass`, floating over a slow blue aurora (radial gradients of `primary`, `azure`, `aqua` at 25–45% on `bg`). Use solid `surface` for dense data (tables, forms) so text stays crisp.
- **Type.** `display` family (Alexandria) for headings and the wordmark; `sans` (IBM Plex Sans Arabic) for everything you read; `mono` (IBM Plex Mono) for amounts and case numbers. Both main families cover Arabic and Latin. Load from Google Fonts.
- **Shape.** Soft and rounded: `radius-md` controls, `radius-lg` cards, `radius-pill` for chips and stage dots. Spacing is a 4px base (`space-1`…`space-8`).
- **Motion.** GSAP for scroll reveals (fade + 16px rise, 0.6s, `power3.out`), Lenis smooth scroll. Respect `prefers-reduced-motion`.
- **Focus.** 2px `focus-ring` outline with 2px offset on every interactive element.

## Iconography

Rounded line icons, 1.75px stroke, 24px grid (Lucide / Phosphor regular match well). The tooth-crown mark is the only filled symbol; don't use it as a generic icon.

## Using this system

Colours are semantic tokens with a light and a dark theme. Components are plain HTML + CSS (`components/bundle.css`, classes prefixed `tj-`); page content is written in HTML, JavaScript only adds behaviour. Workflow stages are Design → Milling → Layering → Glazing; show them with `StageTracker`.
