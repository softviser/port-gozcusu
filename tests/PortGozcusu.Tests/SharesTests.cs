using Xunit;

namespace PortGozcusu.Tests;

public class SharesTests
{
    [Fact]
    public void Local_machine_lists_shares_without_admin_shares()
    {
        var result = Shares.List("127.0.0.1");
        // Bu makinede Sunucu hizmeti kapalıysa hata metni döner; ikisi de kabul.
        if (result.Error == null)
            Assert.DoesNotContain(result.Names, n => n.EndsWith('$'));
        else
            Assert.Empty(result.Names);
    }

    [Fact]
    public async Task Unreachable_host_returns_error_instead_of_hanging()
    {
        // 192.0.2.0/24 belgelendirme için ayrılmıştır, hiçbir cihaz yanıt vermez.
        var started = DateTime.UtcNow;
        var map = await Shares.ListAsync(new[] { "192.0.2.1" }, CancellationToken.None);
        Assert.NotNull(map["192.0.2.1"].Error);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(8));
    }
}
