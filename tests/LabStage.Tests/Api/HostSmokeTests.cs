using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabStage.Tests.Api;

public class HostSmokeTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_endpoint_is_up_and_sends_security_headers()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/v1/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Homepage_is_served_as_html_with_arabic_rtl()
    {
        var html = await factory.CreateClient().GetStringAsync("/");
        Assert.Contains("lang=\"ar\"", html);
        Assert.Contains("dir=\"rtl\"", html);
    }
}
