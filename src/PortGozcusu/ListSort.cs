using System.Collections;
using System.Net;

namespace PortGozcusu;

/// <summary>Sütun başlığına tıklanınca liste satırlarını sıralar; sayı, IP ve tarihleri doğru sırada karşılaştırır.</summary>
internal sealed class ListSort : IComparer
{
    public int Column { get; set; }
    public bool Descending { get; set; }

    public int Compare(object? x, object? y)
    {
        var a = ((ListViewItem)x!).SubItems[Column].Text;
        var b = ((ListViewItem)y!).SubItems[Column].Text;
        int r = CompareText(a, b);
        return Descending ? -r : r;
    }

    public static int CompareText(string a, string b)
    {
        // Sıra önemli: "80" IPAddress olarak da çözülebildiği için önce tam sayı denenir.
        if (int.TryParse(a, out var ia) && int.TryParse(b, out var ib))
            return ia.CompareTo(ib);
        if (IsIPv4(a, out var ipa) && IsIPv4(b, out var ipb))
            return Scanner.IpSortKey(ipa).CompareTo(Scanner.IpSortKey(ipb));
        if (DateTime.TryParse(a, out var da) && DateTime.TryParse(b, out var db))
            return da.CompareTo(db);
        return string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);
    }

    private static bool IsIPv4(string s, out IPAddress ip) =>
        IPAddress.TryParse(s, out ip!) && s.Count(c => c == '.') == 3;
}
