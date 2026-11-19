using System.Text.RegularExpressions;
using LabStage.Api.Frontend;
using LabStage.Api.Security;

namespace LabStage.Tests.Api;

/// <summary>Checks on the files in wwwroot that the speed work depends on.</summary>
public class FrontendAssetsTests
{
    private static string WebRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "LabStage.Api", "wwwroot");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("wwwroot not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void Css_bundles_and_page_links_are_up_to_date()
    {
        var stale = CssBundles.Sync(WebRoot(), write: false);
        Assert.True(stale.Count == 0,
            "Run the site once in Development (it rebuilds the bundles) and commit: " + string.Join(", ", stale));
    }

    [Fact]
    public void Every_page_uses_the_one_inline_script_the_csp_allows()
    {
        foreach (var page in Directory.EnumerateFiles(WebRoot(), "*.html", SearchOption.AllDirectories))
        {
            var scripts = Regex.Matches(File.ReadAllText(page), @"<script>(.*?)</script>", RegexOptions.Singleline);
            foreach (Match m in scripts)
                Assert.True(m.Groups[1].Value == SecurityMiddleware.InlineScript, $"{Path.GetFileName(page)} has an inline script the CSP blocks");
        }
    }

    [Fact]
    public void Minify_keeps_selectors_and_values()
    {
        Assert.Equal("a :hover{color:red}.b,.c{margin: calc(1px + 2px)}",
            CssBundles.Minify("/* x */ a :hover { color:red; }\n.b , .c { margin: calc(1px + 2px); }"));
    }
}
