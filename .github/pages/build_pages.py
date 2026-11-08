"""Build the static GitHub Pages demo from src/LabStage.Api/wwwroot.

The ASP.NET app serves the site at the domain root and has server routes (/labs, /labs/{slug}).
GitHub Pages serves a project site under /<repo>/ with static files only, so this script copies
wwwroot to _site and rewrites root-absolute links to /<repo>/..., mapping the server routes to their
HTML files. API calls (/api/...) are left alone: they fail on Pages and the pages show the demo notice.
Usage: python3 .github/pages/build_pages.py [repo-name]
"""
import pathlib, re, shutil, sys

REPO = sys.argv[1] if len(sys.argv) > 1 else 'taaj'
ROOT = pathlib.Path(__file__).resolve().parents[2]
SRC = ROOT / 'src' / 'LabStage.Api' / 'wwwroot'
OUT = ROOT / '_site'
P = f'/{REPO}/'

def map_path(path):
    """path starts with '/' (no scheme). Return the Pages URL."""
    if path.startswith('//') or path.startswith('/api/') or path.startswith(P):
        return path
    m = re.match(r'^/labs(/[^?#"\'`]*)?([?#].*)?$', path)
    if m:
        slug = (m.group(1) or '').strip('/')
        return P + ('lab-profile.html' if slug else 'labs.html') + (m.group(2) or '')
    return P + path[1:]

ATTR = re.compile(r'((?:href|src|action|content)=)(["\'])(/[^"\']*)\2')
# JS / CSS string literals and url() that point at site files or pages
LIT = re.compile(r'''(["'`(])(/(?:assets/|legal/|labs\b|[A-Za-z0-9_-]+\.(?:html|ico|svg|png|xml|txt))[^"'`)\s]*)''')

def rewrite(text):
    # JS template links to a lab page: /labs/${slug} -> /lab-profile.html?slug=${slug} (prefixed below)
    text = re.sub(r'/labs/(\$\{[^}]*\})', lambda m: '/lab-profile.html?slug=' + m.group(1), text)
    text = ATTR.sub(lambda m: m.group(1) + m.group(2) + map_path(m.group(3)) + m.group(2), text)
    text = LIT.sub(lambda m: m.group(1) + map_path(m.group(2)), text)
    return text

if OUT.exists():
    shutil.rmtree(OUT)
shutil.copytree(SRC, OUT)
count = 0
for f in OUT.rglob('*'):
    if f.suffix in {'.html', '.css', '.js', '.xml', '.txt', '.webmanifest'} and 'vendor' not in f.parts:
        s = f.read_text(encoding='utf-8')
        t = rewrite(s)
        if t != s:
            f.write_text(t, encoding='utf-8')
            count += 1
(OUT / '.nojekyll').write_text('')
shutil.copyfile(OUT / 'index.html', OUT / '404.html')
print(f'built {OUT} for {P}: rewrote {count} files')
