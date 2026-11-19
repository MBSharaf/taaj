// First visit only: the page is shown in the system font (so nothing jumps while it loads). A moment after it has
// loaded, this downloads the brand fonts into the browser cache; from the next page on, the inline script in <head>
// applies them at once (see SecurityMiddleware.InlineScript).
(async () => {
  try {
    const css = await (await fetch('/assets/css/fonts.css?v=3')).text();
    const urls = [...new Set([...css.matchAll(/url\(([^)]+\.woff2[^)]*)\)/g)].map((m) => m[1]))]
      .filter((u) => !u.includes('-ext-'));
    await Promise.all(urls.map((u) => fetch(u, { priority: 'low' }).catch(() => {})));
    localStorage.setItem('taaj-fonts', '1');
  } catch { /* fonts are a nicety: the system font stays */ }
})();
