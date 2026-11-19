using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LabStage.Api.Frontend;

/// <summary>
/// Joins the style sheets each kind of page needs into one small file (assets/css/bundles/*.css), so a phone downloads one
/// style sheet instead of four or five before it can draw the page. The source files in assets/css stay the ones to edit:
/// in Development the bundles are rebuilt at start-up, and every page is pointed at the new version (?v=content hash).
/// A test fails if a committed bundle is out of date, so a change can't reach main without its bundle.
/// </summary>
public static partial class CssBundles
{
    public static readonly IReadOnlyDictionary<string, string[]> Sets = new Dictionary<string, string[]>
    {
        ["home"] = ["tokens.css", "base.css", "home.css", "glass.css", "showcase.css"],
        ["site"] = ["tokens.css", "base.css", "marketplace.css", "glass.css"],
        ["auth"] = ["tokens.css", "base.css", "auth.css", "glass.css"],
        ["dash"] = ["tokens.css", "base.css", "dashboard.css", "glass.css", "dash-v2.css"],
    };

    /// <summary>The bundle text for <paramref name="name"/>, built from the files in <paramref name="cssDir"/>.</summary>
    public static string Build(string cssDir, string name)
    {
        var sb = new StringBuilder($"/* Built from {string.Join(", ", Sets[name])} by CssBundles.cs. Edit those files, not this one. */\n");
        foreach (var file in Sets[name])
            sb.Append(Minify(File.ReadAllText(Path.Combine(cssDir, file)))).Append('\n');
        return sb.ToString();
    }

    public static string Version(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..10];

    /// <summary>
    /// Compares the bundles and page links under <paramref name="webRoot"/> with the sources. With <paramref name="write"/>,
    /// fixes what is stale. Returns the files that were (or would be) changed.
    /// </summary>
    public static List<string> Sync(string webRoot, bool write)
    {
        var changed = new List<string>();
        var cssDir = Path.Combine(webRoot, "assets", "css");
        var outDir = Path.Combine(cssDir, "bundles");
        var versions = new Dictionary<string, string>();
        foreach (var name in Sets.Keys)
        {
            var text = Build(cssDir, name);
            versions[name] = Version(text);
            var path = Path.Combine(outDir, name + ".css");
            if (File.Exists(path) && File.ReadAllText(path).ReplaceLineEndings("\n") == text) continue;
            changed.Add(path);
            if (write) { Directory.CreateDirectory(outDir); File.WriteAllText(path, text); }
        }
        foreach (var page in Directory.EnumerateFiles(webRoot, "*.html", SearchOption.AllDirectories))
        {
            var html = File.ReadAllText(page);
            var updated = BundleLink().Replace(html, m =>
                versions.TryGetValue(m.Groups[1].Value, out var v) ? $"/assets/css/bundles/{m.Groups[1].Value}.css?v={v}" : m.Value);
            if (updated == html) continue;
            changed.Add(page);
            if (write) File.WriteAllText(page, updated);
        }
        return changed;
    }

    /// <summary>Drops comments and extra white space. Conservative on purpose: nothing that could change a selector or value.</summary>
    public static string Minify(string css)
    {
        css = Comment().Replace(css, "");
        css = Space().Replace(css, " ");
        css = AroundPunctuation().Replace(css, "$1");
        return css.Replace(";}", "}").Trim();
    }

    [GeneratedRegex(@"/assets/css/bundles/([a-z0-9-]+)\.css(?:\?v=[0-9a-z]+)?")]
    private static partial Regex BundleLink();
    [GeneratedRegex(@"/\*[\s\S]*?\*/")]
    private static partial Regex Comment();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();
    [GeneratedRegex(@"\s*([{};,])\s*")]
    private static partial Regex AroundPunctuation();
}
