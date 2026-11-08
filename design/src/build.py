"""Builds design/labstage-app.html from the Labstage design system files and the prototype source.

Usage: python design/src/build.py <design-system project folder>
The design system folder holds tokens.json and components/bundle.{js,css}.
"""
import json, os, sys

ds = sys.argv[1]
here = os.path.dirname(os.path.abspath(__file__))
out = os.path.join(os.path.dirname(here), "labstage-app.html")

def read(p):
    with open(p, encoding="utf-8") as f:
        return f.read()

tok = json.loads(read(os.path.join(ds, "tokens.json")))
light, dark, root = [], [], []
for fam in ("color", "shadow"):
    for t in tok[fam]["tokens"]:
        v = t["value"]
        if isinstance(v, str):
            light.append("--%s:%s;" % (t["name"], v))
        else:
            light.append("--%s:%s;" % (t["name"], v["light"]))
            dark.append("--%s:%s;" % (t["name"], v["dark"]))
for fam in ("spacing", "radius", "layout"):
    for t in tok[fam]["tokens"]:
        root.append("--%s:%s;" % (t["name"], t["value"]))
for k, v in tok["type"]["families"].items():
    root.append("--font-%s:%s;" % (k, v))
tokens_css = (':root,[data-theme="light"]{%s color-scheme:light}[data-theme="dark"]{%s color-scheme:dark}:root{%s}'
              % ("".join(light), "".join(dark), "".join(root)))

bundle_css = read(os.path.join(ds, "components", "bundle.css"))
bundle_js = read(os.path.join(ds, "components", "bundle.js"))
app_css = read(os.path.join(here, "app.css"))
app_js = read(os.path.join(here, "app.js"))
for name, src in (("bundle.js", bundle_js), ("app.js", app_js)):
    if "</script" in src.lower():
        sys.exit(name + " contains </script")

html = "\n".join([
    "<title>Labstage App</title>",
    '<meta name="claude:viewport-fit-sides" content="cover">',
    "<style>", tokens_css, bundle_css, app_css, "</style>",
    '<div id="app"></div>',
    '<script src="https://cdnjs.cloudflare.com/ajax/libs/react/18.3.1/umd/react.production.min.js"></script>',
    '<script src="https://cdnjs.cloudflare.com/ajax/libs/react-dom/18.3.1/umd/react-dom.production.min.js"></script>',
    '<script src="https://cdnjs.cloudflare.com/ajax/libs/three.js/r128/three.min.js"></script>',
    "<script>", bundle_js, "</script>",
    "<script>", app_js, "</script>",
])
with open(out, "w", encoding="utf-8", newline="\n") as f:
    f.write(html)
print("wrote", out, len(html), "bytes")
