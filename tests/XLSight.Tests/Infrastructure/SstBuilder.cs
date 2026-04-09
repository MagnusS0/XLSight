using System.Globalization;
using System.Text;
using XLSight.Internal.Metadata;

namespace XLSight.Tests.Infrastructure;

internal static class SstBuilder
{
    internal static SharedStringTable Make(params string[] strings)
    {
        if (strings.Length == 0) { return SharedStringTable.Empty; }

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"{strings.Length}\" uniqueCount=\"{strings.Length}\">");
        foreach (var s in strings)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<si><t>{System.Security.SecurityElement.Escape(s)}</t></si>");
        }
        sb.Append("</sst>");

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        return SharedStringsByteParser.Parse(stream);
    }
}
