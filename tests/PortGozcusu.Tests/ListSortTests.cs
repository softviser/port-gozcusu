using System.Globalization;
using Xunit;

namespace PortGozcusu.Tests;

public class ListSortTests
{
    [Fact]
    public void Ports_sort_numerically()
    {
        Assert.True(ListSort.CompareText("80", "443") < 0);
        Assert.True(ListSort.CompareText("8080", "443") > 0);
    }

    [Fact]
    public void Addresses_sort_by_value_not_text()
    {
        Assert.True(ListSort.CompareText("192.168.1.9", "192.168.1.10") < 0);
        Assert.True(ListSort.CompareText("10.0.0.200", "9.255.255.255") > 0);
    }

    [Fact]
    public void Dates_sort_chronologically()
    {
        var older = new DateTime(2026, 9, 30, 23, 0, 0).ToString("g", CultureInfo.CurrentCulture);
        var newer = new DateTime(2026, 10, 1, 1, 0, 0).ToString("g", CultureInfo.CurrentCulture);
        Assert.True(ListSort.CompareText(older, newer) < 0);
    }

    [Fact]
    public void Text_sorts_case_insensitively()
    {
        Assert.True(ListSort.CompareText("nas.lan", "Router.lan") < 0);
        Assert.Equal(0, ListSort.CompareText("SMB", "smb"));
    }
}
