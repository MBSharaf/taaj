# LabStage — Blue Glass UI redesign (2026-11-07)

## What changed (frontend only, `src/LabStage.Api/wwwroot`)
- New palette "Lagoon": cobalt #2F6BFF (primary), royal #1B47D6, azure #4DA8FF, aqua #7FE3FF, abyss #050B24, ice #EEF4FF, night #0B1A45. Defined in `assets/css/tokens.css`.
- Glassmorphism layer for the whole platform: `assets/css/glass.css` (loaded last on every page) + `assets/js/core/glass.js` (pointer light on cards).
- Fonts: Alexandria (headings) + IBM Plex Sans Arabic (body), from Google Fonts.
- Motion: GSAP 3 + ScrollTrigger + Lenis smooth scroll, vendored in `assets/vendor/gsap` and `assets/vendor/lenis`. Homepage scenes in `assets/js/pages/home.js`; scroll reveals in `assets/js/core/site.js`.
- 3D crown (`assets/js/pages/crown3d.js`) re-lit in blue with a glass material for the Design stage.
- Rule kept: all page content is written in the HTML files; JS only adds behaviour, animation and data.

## Backup
Original green UI: `backup/wwwroot-before-redesign/`. To roll back, copy it over `src/LabStage.Api/wwwroot`.

## Design file
Figma: https://www.figma.com/design/i72zti30rwA6Zai2qgfo7k (Foundations, Components, Home — Desktop).

## Run
`cd src/LabStage.Api` then `dotnet run --urls http://localhost:5080`. Static files are served directly, so a browser refresh (Ctrl+F5) picks up frontend edits.

## Known follow-ups
- Backend: `System.Security.Cryptography.Pkcs` 9.0.11 has known vulnerabilities (NU1902/NU1903); update it.
- Dashboards (doctor, lab, admin) still need a full glass polish pass.
