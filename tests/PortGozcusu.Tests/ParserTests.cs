using Xunit;

namespace PortGozcusu.Tests;

public class ParserTests
{
    [Fact]
    public void Ports_list_and_ranges_are_merged_and_sorted()
    {
        Assert.Equal(new[] { 22, 80, 81, 82, 443 }, TargetParser.ParsePorts("443, 80-82,22,81"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("90-80")]
    [InlineData("abc")]
    [InlineData("")]
    public void Invalid_ports_are_rejected(string text)
    {
        Assert.Throws<FormatException>(() => TargetParser.ParsePorts(text));
    }

    [Fact]
    public void Cidr_excludes_network_and_broadcast()
    {
        var hosts = TargetParser.ParseHosts("192.168.1.0/30").Select(h => h.ToString());
        Assert.Equal(new[] { "192.168.1.1", "192.168.1.2" }, hosts);
    }

    [Fact]
    public void Short_and_full_ranges_are_supported()
    {
        Assert.Equal(3, TargetParser.ParseHosts("10.0.0.5-7").Count);
        Assert.Equal(3, TargetParser.ParseHosts("10.0.0.254-10.0.1.0").Count);
    }

    [Fact]
    public void Duplicates_are_removed()
    {
        Assert.Single(TargetParser.ParseHosts("10.0.0.1, 10.0.0.1-1"));
    }

    [Theory]
    [InlineData("10.0.0.0/16")]
    [InlineData("10.0.0.9-10.0.0.1")]
    [InlineData("10.0.0")]
    public void Too_wide_or_invalid_targets_are_rejected(string text)
    {
        Assert.Throws<FormatException>(() => TargetParser.ParseHosts(text));
    }
}
