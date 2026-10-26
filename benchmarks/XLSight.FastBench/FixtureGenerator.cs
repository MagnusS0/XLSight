using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace XLSight.FastBench;

/// <summary>
/// Writes the synthetic workbooks the manifest names. Output depends only on fixed seeds, fixed
/// ZIP timestamps and the runtime's deflate implementation; the manifest pins each file's SHA-256
/// so a runtime that compresses differently is detected rather than silently measured.
/// </summary>
internal static class FixtureGenerator
{
    private static readonly DateTimeOffset FixedTimestamp = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static void Generate(string generator, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        switch (generator)
        {
            case "strings_repeated":
                WriteStringsRepeated(outputPath, 60_000);
                break;
            case "strings_repeated_x10":
                WriteStringsRepeated(outputPath, 600_000);
                break;
            case "strings_unique":
                WriteStringsUnique(outputPath);
                break;
            case "mixed_sparse":
                WriteMixedSparse(outputPath, 25_000);
                break;
            case "mixed_sparse_2k":
                WriteMixedSparse(outputPath, 2_000);
                break;
            case "mixed_sparse_x8":
                WriteMixedSparse(outputPath, 200_000);
                break;
            case "merged_regions":
                WriteMergedRegions(outputPath);
                break;
            default:
                throw new InvalidOperationException($"Unknown fixture generator '{generator}'.");
        }
    }

    // `rows` data rows (60,000 in the fast suite) × 8 columns of a typical exported table: low- and medium-cardinality shared
    // strings (8 regions, 50 categories, 2,000 products), integers, decimals, a styled date column
    // and a Y/N flag. Exercises the shared-string path, grouping, distinct values and dates.
    private static void WriteStringsRepeated(string path, int rows)
    {
        var rng = new SplitMix64(0x5EED_0001);
        var sst = new SharedStrings();
        string[] headers = ["Id", "Category", "Region", "Product", "Value", "Qty", "Flag", "Date"];
        string[] regions = ["EMEA", "APAC", "NA", "LATAM", "MEA", "ANZ", "Nordics", "DACH"];
        string[] categories = [.. Enumerable.Range(0, 50).Select(static i => $"Category {i:D2}")];
        string[] products = [.. Enumerable.Range(0, 2_000).Select(static i => i % 7 == 0 ? $"Bolts & Nuts #{i:D4}" : $"Product {i:D4} widget")];

        var sheet = new SheetWriter();
        sheet.BeginRow(1);
        for (int c = 0; c < headers.Length; c++)
        {
            sheet.Shared(c + 1, 1, sst.Add(headers[c]));
        }

        sheet.EndRow();
        for (int r = 2; r <= rows + 1; r++)
        {
            sheet.BeginRow(r);
            sheet.Number(1, r, r - 1);
            sheet.Shared(2, r, sst.Add(categories[(int)rng.Next(50)]));
            sheet.Shared(3, r, sst.Add(regions[(int)rng.Next(8)]));
            sheet.Shared(4, r, sst.Add(products[(int)rng.Next(2_000)]));
            sheet.Number(5, r, Math.Round(rng.NextDouble() * 1_000, 2));
            sheet.Number(6, r, 1 + (long)rng.Next(50));
            sheet.Shared(7, r, sst.Add(rng.Next(3) == 0 ? "N" : "Y"));
            sheet.Number(8, r, 43_831 + (long)rng.Next(1_500), style: 1);
            sheet.EndRow();
        }

        WritePackage(path, [("Data", sheet.Finish($"A1:H{rows + 1}"))], sst.Finish());
    }

    // 40,000 rows × 4 columns where every cell is a distinct shared string: ASCII identifiers,
    // longer sentences, non-ASCII text with surrogate pairs, and XML-escaped markup. Every 10th
    // string is rich text split into runs. Stresses shared-string load memory and decoding.
    private static void WriteStringsUnique(string path)
    {
        const int rows = 40_000;
        var rng = new SplitMix64(0x5EED_0002);
        var sst = new SharedStrings();
        string[] words = ["alpha", "bravo", "charlie", "delta", "echo", "foxtrot", "golf", "hotel", "india", "juliet", "kilo", "lima"];
        var sheet = new SheetWriter();
        sheet.BeginRow(1);
        sheet.Shared(1, 1, sst.Add("Key"));
        sheet.Shared(2, 1, sst.Add("Sentence"));
        sheet.Shared(3, 1, sst.Add("Unicode"));
        sheet.Shared(4, 1, sst.Add("Markup"));
        sheet.EndRow();
        var sentence = new StringBuilder();
        for (int r = 2; r <= rows + 1; r++)
        {
            sheet.BeginRow(r);
            sheet.Shared(1, r, sst.AddUnique($"ID-{r:D7}-{rng.Next(uint.MaxValue):x8}"));
            sentence.Clear();
            int wordCount = 4 + (int)rng.Next(6);
            for (int w = 0; w < wordCount; w++)
            {
                sentence.Append(words[(int)rng.Next((ulong)words.Length)]).Append(' ');
            }

            sentence.Append(r.ToString(CultureInfo.InvariantCulture));
            sheet.Shared(2, r, r % 10 == 0 ? sst.AddRich(sentence.ToString()) : sst.AddUnique(sentence.ToString()));
            sheet.Shared(3, r, sst.AddUnique($"Ünïcödé {r} 漢字 \U0001F600 ç{rng.Next(1000)}"));
            sheet.Shared(4, r, sst.AddUnique($"<tag a=\"{r}\"> & 'x' {rng.Next(1_000_000)}"));
            sheet.EndRow();
        }

        WritePackage(path, [("Data", sheet.Finish("A1:D40001"))], sst.Finish());
    }

    // `rows` rows (25,000 in the fast suite) across A:Z with ~40% fill plus an occasional far column (AZ): integers,
    // decimals, dates, booleans, errors, inline strings, shared strings, numeric and string
    // formulas with cached values. Exercises sparse rows and every cell-type branch.
    private static void WriteMixedSparse(string path, int rows)
    {
        var rng = new SplitMix64(0x5EED_0003);
        var sst = new SharedStrings();
        string[] labels = [.. Enumerable.Range(0, 200).Select(static i => $"Label {i}")];
        string[] errors = ["#N/A", "#DIV/0!", "#VALUE!", "#REF!"];
        var sheet = new SheetWriter();
        for (int r = 1; r <= rows; r++)
        {
            sheet.BeginRow(r);
            for (int c = 1; c <= 26; c++)
            {
                if (rng.Next(10) >= 4)
                {
                    continue;
                }

                switch ((c + r) % 9)
                {
                    case 0: sheet.Number(c, r, (long)rng.Next(100_000)); break;
                    case 1: sheet.Number(c, r, Math.Round((rng.NextDouble() - 0.5) * 1e6, 4)); break;
                    case 2: sheet.Number(c, r, 40_000 + (rng.NextDouble() * 9_000), style: 2); break;
                    case 3: sheet.Boolean(c, r, rng.Next(2) == 0); break;
                    case 4: sheet.InlineString(c, r, $"inline {rng.Next(5_000)} & co"); break;
                    case 5: sheet.Shared(c, r, sst.Add(labels[(int)rng.Next(200)])); break;
                    case 6: sheet.Formula(c, r, $"SUM(A{r}:C{r})*2", Math.Round(rng.NextDouble() * 500, 3)); break;
                    case 7: sheet.StringFormula(c, r, $"CONCAT(\"k\",A{r})", $"k{rng.Next(10_000)}"); break;
                    default:
                        if (rng.Next(20) == 0)
                        {
                            sheet.Error(c, r, errors[(int)rng.Next(4)]);
                        }
                        else
                        {
                            sheet.Number(c, r, rng.NextDouble());
                        }

                        break;
                }
            }

            if (r % 97 == 0)
            {
                sheet.Number(52, r, r);
            }

            sheet.EndRow();
        }

        WritePackage(path, [("Mixed", sheet.Finish($"A1:AZ{rows}"))], sst.Finish());
    }

    // A 2,500-row report-style sheet whose post-sheetData section carries 2,500 merged regions
    // and 250 data validations: the metadata that full analysis walks after the cell data.
    private static void WriteMergedRegions(string path)
    {
        const int rows = 2_500;
        var rng = new SplitMix64(0x5EED_0004);
        var sst = new SharedStrings();
        var sheet = new SheetWriter();
        for (int r = 1; r <= rows; r++)
        {
            sheet.BeginRow(r);
            sheet.Shared(1, r, sst.Add($"Section {r / 50}"));
            sheet.Number(3, r, Math.Round(rng.NextDouble() * 100, 2));
            sheet.Number(4, r, (long)rng.Next(1_000));
            sheet.EndRow();
        }

        string xml = sheet.Finish($"A1:D{rows}");
        var tail = new StringBuilder();
        tail.Append(CultureInfo.InvariantCulture, $"<mergeCells count=\"{rows}\">");
        for (int r = 1; r <= rows; r++)
        {
            tail.Append(CultureInfo.InvariantCulture, $"<mergeCell ref=\"A{r}:B{r}\"/>");
        }

        tail.Append("</mergeCells><dataValidations count=\"250\">");
        for (int i = 0; i < 250; i++)
        {
            int r = 1 + (i * 10);
            tail.Append(CultureInfo.InvariantCulture, $"<dataValidation type=\"whole\" operator=\"between\" allowBlank=\"1\" sqref=\"D{r}:D{r + 9}\"><formula1>0</formula1><formula2>1000</formula2></dataValidation>");
        }

        tail.Append("</dataValidations>");
        xml = xml.Replace("</sheetData></worksheet>", "</sheetData>" + tail + "</worksheet>", StringComparison.Ordinal);
        WritePackage(path, [("Report", xml)], sst.Finish());
    }

    private static void WritePackage(string path, (string Name, string Xml)[] sheets, string sharedStrings)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        var contentTypes = new StringBuilder("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/><Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/>""");
        var workbook = new StringBuilder("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>""");
        var rels = new StringBuilder("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""");
        for (int i = 0; i < sheets.Length; i++)
        {
            int n = i + 1;
            contentTypes.Append(CultureInfo.InvariantCulture, $"""<Override PartName="/xl/worksheets/sheet{n}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>""");
            workbook.Append(CultureInfo.InvariantCulture, $"""<sheet name="{sheets[i].Name}" sheetId="{n}" r:id="rId{n}"/>""");
            rels.Append(CultureInfo.InvariantCulture, $"""<Relationship Id="rId{n}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{n}.xml"/>""");
        }

        int styleId = sheets.Length + 1;
        rels.Append(CultureInfo.InvariantCulture, $"""<Relationship Id="rId{styleId}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/><Relationship Id="rId{styleId + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/></Relationships>""");
        contentTypes.Append("</Types>");
        workbook.Append("</sheets></workbook>");

        WriteEntry(zip, "[Content_Types].xml", contentTypes.ToString());
        WriteEntry(zip, "_rels/.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
        WriteEntry(zip, "xl/workbook.xml", workbook.ToString());
        WriteEntry(zip, "xl/_rels/workbook.xml.rels", rels.ToString());
        WriteEntry(zip, "xl/styles.xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts><fills count="1"><fill><patternFill patternType="none"/></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="3"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="14" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="22" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs></styleSheet>""");
        WriteEntry(zip, "xl/sharedStrings.xml", sharedStrings);
        for (int i = 0; i < sheets.Length; i++)
        {
            WriteEntry(zip, $"xl/worksheets/sheet{i + 1}.xml", sheets[i].Xml);
        }
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = FixedTimestamp;
        using Stream stream = entry.Open();
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes);
    }

    private static string Escape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static string ColumnName(int column)
    {
        Span<char> buffer = stackalloc char[3];
        int index = buffer.Length;
        while (column > 0)
        {
            column--;
            buffer[--index] = (char)('A' + (column % 26));
            column /= 26;
        }

        return new string(buffer[index..]);
    }

    private sealed class SheetWriter
    {
        private readonly StringBuilder _xml = new(1 << 20);
        private readonly StringBuilder _rowXml = new(1024);
        private int _row;

        public void BeginRow(int row)
        {
            _row = row;
            _rowXml.Clear();
        }

        public void EndRow()
        {
            if (_rowXml.Length == 0)
            {
                return;
            }

            _xml.Append(CultureInfo.InvariantCulture, $"<row r=\"{_row}\">").Append(_rowXml).Append("</row>");
        }

        private StringBuilder Cell(int column, int row, string? type, int style = 0)
        {
            _rowXml.Append("<c r=\"").Append(ColumnName(column)).Append(row.ToString(CultureInfo.InvariantCulture)).Append('"');
            if (style != 0)
            {
                _rowXml.Append(CultureInfo.InvariantCulture, $" s=\"{style}\"");
            }

            if (type is not null)
            {
                _rowXml.Append(" t=\"").Append(type).Append('"');
            }

            return _rowXml.Append('>');
        }

        public void Number(int column, int row, double value, int style = 0) =>
            Cell(column, row, null, style).Append("<v>").Append(value.ToString("R", CultureInfo.InvariantCulture)).Append("</v></c>");

        public void Number(int column, int row, long value, int style = 0) =>
            Cell(column, row, null, style).Append("<v>").Append(value.ToString(CultureInfo.InvariantCulture)).Append("</v></c>");

        public void Shared(int column, int row, int index) =>
            Cell(column, row, "s").Append("<v>").Append(index.ToString(CultureInfo.InvariantCulture)).Append("</v></c>");

        public void Boolean(int column, int row, bool value) =>
            Cell(column, row, "b").Append(value ? "<v>1</v></c>" : "<v>0</v></c>");

        public void Error(int column, int row, string code) =>
            Cell(column, row, "e").Append("<v>").Append(Escape(code)).Append("</v></c>");

        public void InlineString(int column, int row, string value) =>
            Cell(column, row, "inlineStr").Append("<is><t>").Append(Escape(value)).Append("</t></is></c>");

        public void Formula(int column, int row, string formula, double cached) =>
            Cell(column, row, null).Append("<f>").Append(Escape(formula)).Append("</f><v>")
                .Append(cached.ToString("R", CultureInfo.InvariantCulture)).Append("</v></c>");

        public void StringFormula(int column, int row, string formula, string cached) =>
            Cell(column, row, "str").Append("<f>").Append(Escape(formula)).Append("</f><v>").Append(Escape(cached)).Append("</v></c>");

        public string Finish(string dimension) =>
            $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><dimension ref="{dimension}"/><sheetData>{_xml}</sheetData></worksheet>""";
    }

    private sealed class SharedStrings
    {
        private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);
        private readonly StringBuilder _xml = new(1 << 20);
        private int _count;
        private int _references;

        public int Add(string value)
        {
            _references++;
            if (_index.TryGetValue(value, out int existing))
            {
                return existing;
            }

            int id = Append(value, rich: false);
            _index.Add(value, id);
            return id;
        }

        public int AddUnique(string value)
        {
            _references++;
            return Append(value, rich: false);
        }

        public int AddRich(string value)
        {
            _references++;
            return Append(value, rich: true);
        }

        private int Append(string value, bool rich)
        {
            if (rich)
            {
                int split = value.Length / 2;
                _xml.Append("<si><r><t xml:space=\"preserve\">").Append(Escape(value[..split]))
                    .Append("</t></r><r><rPr><b/><sz val=\"11\"/></rPr><t xml:space=\"preserve\">")
                    .Append(Escape(value[split..])).Append("</t></r></si>");
            }
            else
            {
                _xml.Append("<si><t>").Append(Escape(value)).Append("</t></si>");
            }

            return _count++;
        }

        public string Finish() =>
            $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="{_references}" uniqueCount="{_count}">{_xml}</sst>""";
    }

    private struct SplitMix64(ulong seed)
    {
        private ulong _state = seed;

        public ulong NextRaw()
        {
            ulong z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public ulong Next(ulong exclusiveMax) => NextRaw() % exclusiveMax;

        public double NextDouble() => (NextRaw() >> 11) * (1.0 / (1UL << 53));
    }
}
