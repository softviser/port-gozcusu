using Xunit;

namespace PortGozcusu.Tests;

public class PortActionsTests
{
    private const string Host = "192.168.1.20";

    [Fact]
    public void Rdp_port_opens_remote_desktop()
    {
        var a = Assert.Single(PortActions.ForPort(Host, 3389));
        Assert.Equal("mstsc.exe", a.FileName);
        Assert.Equal("/v:192.168.1.20:3389", a.Arguments);
    }

    [Fact]
    public void Ssh_port_uses_given_port()
    {
        var a = Assert.Single(PortActions.ForPort(Host, 22));
        Assert.Contains("ssh -p 22 192.168.1.20", a.Arguments);
    }

    [Theory]
    [InlineData(80, "http://192.168.1.20/")]
    [InlineData(8080, "http://192.168.1.20:8080/")]
    [InlineData(443, "https://192.168.1.20/")]
    [InlineData(8443, "https://192.168.1.20:8443/")]
    public void Web_ports_open_in_browser(int port, string url)
    {
        Assert.Contains(PortActions.ForPort(Host, port), a => a.FileName == url);
    }

    [Fact]
    public void Smb_opens_shares_without_browser()
    {
        var a = Assert.Single(PortActions.ForPort(Host, 445));
        Assert.Equal(@"\\192.168.1.20", a.Arguments);
    }

    [Fact]
    public void Unknown_port_offers_http_try()
    {
        var a = Assert.Single(PortActions.ForPort(Host, 12345));
        Assert.Equal("http://192.168.1.20:12345/", a.FileName);
    }

    [Fact]
    public void Host_actions_run_inside_the_app()
    {
        var actions = PortActions.ForHost(Host);
        Assert.All(actions, a => Assert.True(a.ShowOutput));
        Assert.Contains(actions, a => a.FileName == "ping.exe" && a.Arguments == "-n 4 192.168.1.20");
        Assert.Contains(actions, a => a.FileName == "tracert.exe" && a.Arguments == "-d 192.168.1.20");
    }

    [Fact]
    public void Interactive_tools_still_open_their_own_window()
    {
        Assert.False(Assert.Single(PortActions.ForPort(Host, 22)).ShowOutput);
    }
}
