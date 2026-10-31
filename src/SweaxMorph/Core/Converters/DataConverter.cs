using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using ClosedXML.Excel;
using ExcelDataReader;
using Tomlyn;
using Tomlyn.Model;
using YamlDotNet.Serialization;

namespace SweaxMorph.Core.Converters;

/// <summary>
/// Data and tables, in-process: JSON ⇄ YAML ⇄ TOML ⇄ XML ⇄ JSON Lines, and lists of records ⇄ CSV, TSV,
/// Excel, Markdown and HTML tables. Numbers stay numbers, Turkish stays readable, nothing is lost silently.
/// </summary>
public sealed class DataConverter : Converter
{
    public override string Name => "SweaxMorph";

    static readonly string[] Trees = ["json", "yaml", "toml", "xml", "jsonl"];
    static readonly string[] TablesIn = ["csv", "tsv", "xlsx", "xls", "xlsm", "xlsb"];
    static readonly string[] TablesOut = ["csv", "tsv", "xlsx"];

    public override IEnumerable<Edge> Edges()
    {
        foreach (var e in Cross(Trees, Trees, 1)) yield return e;
        foreach (var e in Cross(TablesIn, Trees, 2)) yield return e;
        foreach (var e in Cross(Trees, TablesOut, 2)) yield return e;
        foreach (var e in Cross(TablesIn, TablesOut, 1)) yield return e.From is "xls" or "xlsb" or "xlsm" && e.To == "xlsx" ? e with { Cost = 3 } : e;
        foreach (var e in Cross(TablesIn, ["md", "html"], 2)) yield return e;
        foreach (var e in Cross(Trees, ["md", "html"], 3)) yield return e;
    }

    public override Task<string[]> Run(Step s) => Task.Run(() =>
    {
        s.Progress.Report(0.1);
        object? data = Read(s.Input, s.From.Id);
        s.Progress.Report(0.6);
        string outFile = s.Out();
        Write(data, outFile, s.To.Id);
        s.Progress.Report(1);
        return new[] { outFile };
    }, s.Cancel);

    // ── reading: everything becomes plain dictionaries, lists and values ─────

    static object? Read(string file, string format)
    {
        switch (format)
        {
            case "json":
                return Plain(JsonNode.Parse(File.ReadAllText(file), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }));
            case "jsonl":
                return File.ReadLines(file).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => Plain(JsonNode.Parse(l))).ToList();
            case "yaml":
            {
                var d = new DeserializerBuilder().WithAttemptingUnquotedStringTypeDeserialization().Build();
                return Plain(d.Deserialize<object?>(File.ReadAllText(file)));
            }
            case "toml":
                return Plain(Toml.ToModel(File.ReadAllText(file)));
            case "xml":
            {
                var doc = XDocument.Load(file);
                return new Dictionary<string, object?> { [doc.Root!.Name.LocalName] = FromXml(doc.Root) };
            }
            case "csv" or "tsv":
                return Records(Csv.Parse(File.ReadAllText(file), format == "tsv" ? '\t' : Csv.Sniff(File.ReadAllText(file))));
            default:
                return Workbook(file);
        }
    }

    static object? Plain(object? o) => o switch
    {
        null => null,
        JsonObject jo => jo.ToDictionary(p => p.Key, p => Plain(p.Value)),
        JsonArray ja => ja.Select(Plain).ToList(),
        JsonValue jv => jv.GetValueKind() switch
        {
            JsonValueKind.String => jv.GetValue<string>(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => jv.TryGetValue<long>(out long l) ? l : jv.GetValue<double>(),
            _ => null,
        },
        TomlTable t => t.ToDictionary(p => p.Key, p => Plain(p.Value)),
        TomlTableArray ta => ta.Select(x => Plain(x)).ToList(),
        TomlArray a => a.Select(Plain).ToList(),
        TomlDateTime dt => dt.ToString(),
        IDictionary<object, object?> d => d.ToDictionary(p => p.Key?.ToString() ?? "", p => Plain(p.Value)),
        string str => str,
        System.Collections.IEnumerable list => list.Cast<object?>().Select(Plain).ToList(),
        int i => (long)i,
        _ => o,
    };

    static object? FromXml(XElement e)
    {
        bool leaf = !e.HasElements && !e.HasAttributes;
        if (leaf) return Typed(e.Value);
        var d = new Dictionary<string, object?>();
        foreach (var a in e.Attributes().Where(a => !a.IsNamespaceDeclaration)) d["@" + a.Name.LocalName] = Typed(a.Value);
        foreach (var group in e.Elements().GroupBy(c => c.Name.LocalName))
        {
            var items = group.Select(FromXml).ToList();
            d[group.Key] = items.Count == 1 ? items[0] : items;
        }
        string text = string.Concat(e.Nodes().OfType<XText>().Select(t => t.Value)).Trim();
        if (text.Length > 0) d["#text"] = Typed(text);
        return d;
    }

    /// <summary>"42" → 42, "3.5" → 3.5, "true" → true; anything else stays text (leading zeros too: "007").</summary>
    static object? Typed(string v)
    {
        if (v.Length == 0) return v;
        if (v is "true" or "TRUE" or "True") return true;
        if (v is "false" or "FALSE" or "False") return false;
        bool leadingZero = v.Length > 1 && v[0] == '0' && v[1] != '.';
        if (!leadingZero && long.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l)) return l;
        if (!leadingZero && v.Contains('.') && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && !double.IsInfinity(d)) return d;
        return v;
    }

    static List<object?> Records(List<string[]> rows)
    {
        var list = new List<object?>();
        if (rows.Count == 0) return list;
        var header = Headers(rows[0]);
        foreach (var r in rows.Skip(1))
        {
            if (r.All(string.IsNullOrEmpty)) continue;
            var d = new Dictionary<string, object?>();
            for (int i = 0; i < header.Length; i++) d[header[i]] = i < r.Length ? Typed(r[i]) : "";
            list.Add(d);
        }
        return list;
    }

    static string[] Headers(string[] row)
    {
        var seen = new HashSet<string>();
        var names = new string[row.Length];
        for (int i = 0; i < row.Length; i++)
        {
            string n = string.IsNullOrWhiteSpace(row[i]) ? $"column{i + 1}" : row[i].Trim();
            string unique = n;
            for (int k = 2; !seen.Add(unique); k++) unique = $"{n}_{k}";
            names[i] = unique;
        }
        return names;
    }

    /// <summary>A workbook: one sheet becomes a list of records; several become { "Sheet": [...] }.</summary>
    static object? Workbook(string file)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var fs = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(fs);
        var sheets = new Dictionary<string, object?>();
        do
        {
            var raw = new List<object?[]>();
            while (reader.Read())
            {
                var row = new object?[reader.FieldCount];
                for (int i = 0; i < row.Length; i++) row[i] = reader.GetValue(i);
                raw.Add(row);
            }
            var rows = raw.Select(r => r.Select(Cell).ToArray()).ToList();
            if (rows.Count == 0 || rows.All(r => r.All(string.IsNullOrEmpty))) continue;
            var header = Headers(rows[0]);
            var recs = new List<object?>();
            foreach (var r in raw.Skip(1))
            {
                if (r.All(c => c == null || c is DBNull || c.ToString() == "")) continue;
                var d = new Dictionary<string, object?>();
                for (int i = 0; i < header.Length; i++) d[header[i]] = i < r.Length ? Native(r[i]) : null;
                recs.Add(d);
            }
            sheets[reader.Name ?? $"Sheet{sheets.Count + 1}"] = recs;
        } while (reader.NextResult());
        return sheets.Count == 1 ? sheets.Values.First() : sheets;
    }

    static string Cell(object? c) => c switch { null or DBNull => "", double d => d.ToString(CultureInfo.InvariantCulture), DateTime dt => dt.ToString("s"), _ => c.ToString() ?? "" };

    static object? Native(object? c) => c switch
    {
        null or DBNull => null,
        double d when d == Math.Floor(d) && Math.Abs(d) < 1e15 => (long)d,
        DateTime dt => dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("yyyy-MM-dd") : dt.ToString("s"),
        _ => c,
    };

    // ── writing ─────────────────────────────────────────────────────────────

    static readonly JsonSerializerOptions JsonOut = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    static void Write(object? data, string file, string format)
    {
        var utf8 = new UTF8Encoding(false);
        switch (format)
        {
            case "json":
                File.WriteAllText(file, JsonSerializer.Serialize(data, JsonOut), utf8);
                break;
            case "jsonl":
                var lines = (data is List<object?> l ? l : [data]).Select(x => JsonSerializer.Serialize(x, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                File.WriteAllLines(file, lines, utf8);
                break;
            case "yaml":
                File.WriteAllText(file, new SerializerBuilder().DisableAliases().Build().Serialize(data), utf8);
                break;
            case "toml":
                var root = data is Dictionary<string, object?> d ? d : new Dictionary<string, object?> { ["items"] = data };
                File.WriteAllText(file, Toml.FromModel(ToToml(root)), utf8);
                break;
            case "xml":
                XElement top = data is Dictionary<string, object?> { Count: 1 } single && single.Values.First() is not List<object?>
                    ? ToXml(single.Keys.First(), single.Values.First())
                    : ToXml("root", data);
                new XDocument(new XDeclaration("1.0", "utf-8", null), top).Save(file);
                break;
            case "csv" or "tsv":
            {
                var (head, rows) = Table(data);
                File.WriteAllText(file, Csv.Write(head, rows, format == "tsv" ? '\t' : ','), new UTF8Encoding(true)); // BOM: Excel then reads Turkish right
                break;
            }
            case "xlsx":
                Excel(data, file);
                break;
            case "md":
            {
                var (head, rows) = Table(data);
                var sb = new StringBuilder();
                string Esc(string v) => v.Replace("|", "\\|").Replace("\r", "").Replace("\n", "<br>");
                sb.Append("| ").Append(string.Join(" | ", head.Select(Esc))).AppendLine(" |");
                sb.Append('|').Append(string.Concat(head.Select(_ => " --- |"))).AppendLine();
                foreach (var r in rows) sb.Append("| ").Append(string.Join(" | ", r.Select(c => Esc(Text(c))))).AppendLine(" |");
                File.WriteAllText(file, sb.ToString(), utf8);
                break;
            }
            case "html":
            {
                var (head, rows) = Table(data);
                var sb = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><title>")
                    .Append(WebUtility.HtmlEncode(Path.GetFileNameWithoutExtension(file))).Append("</title><style>")
                    .Append(DocumentConverter.Css).Append("body{max-width:none}</style></head><body><table><thead><tr>");
                foreach (var h in head) sb.Append("<th>").Append(WebUtility.HtmlEncode(h)).Append("</th>");
                sb.Append("</tr></thead><tbody>");
                foreach (var r in rows)
                {
                    sb.Append("<tr>");
                    foreach (var c in r) sb.Append(c is long or double ? "<td style=\"text-align:right\">" : "<td>").Append(WebUtility.HtmlEncode(Text(c))).Append("</td>");
                    sb.Append("</tr>");
                }
                File.WriteAllText(file, sb.Append("</tbody></table></body></html>").ToString(), utf8);
                break;
            }
        }
    }

    static TomlTable ToToml(Dictionary<string, object?> d)
    {
        var t = new TomlTable();
        foreach (var (k, v) in d)
        {
            if (v == null) continue; // TOML has no null
            t[k] = ToTomlValue(v);
        }
        return t;
    }

    static object ToTomlValue(object v) => v switch
    {
        Dictionary<string, object?> d => ToToml(d),
        List<object?> l when l.Count > 0 && l.All(x => x is Dictionary<string, object?>) => TableArray(l),
        List<object?> l => Array(l),
        int i => (long)i,
        _ => v,
    };

    static TomlTableArray TableArray(List<object?> l)
    {
        var ta = new TomlTableArray();
        foreach (var x in l) ta.Add(ToToml((Dictionary<string, object?>)x!));
        return ta;
    }

    static TomlArray Array(List<object?> l)
    {
        var a = new TomlArray();
        foreach (var x in l) if (x != null) a.Add(ToTomlValue(x));
        return a;
    }

    static XElement ToXml(string name, object? v)
    {
        var e = new XElement(XmlName(name));
        switch (v)
        {
            case Dictionary<string, object?> d:
                foreach (var (k, val) in d)
                {
                    if (k.StartsWith('@')) e.SetAttributeValue(XmlName(k[1..]), Text(val));
                    else if (k == "#text") e.Add(new XText(Text(val)));
                    else if (val is List<object?> list) foreach (var item in list) e.Add(ToXml(k, item));
                    else e.Add(ToXml(k, val));
                }
                break;
            case List<object?> l:
                foreach (var item in l) e.Add(ToXml("item", item));
                break;
            case null:
                break;
            default:
                e.Value = Text(v);
                break;
        }
        return e;
    }

    static string XmlName(string s)
    {
        if (s.Length == 0) return "_";
        string n = XmlConvert.EncodeLocalName(s.Replace(' ', '_'));
        return char.IsDigit(n[0]) ? "_" + n : n;
    }

    static string Text(object? v) => v switch
    {
        null => "",
        bool b => b ? "true" : "false",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        string s => s,
        Dictionary<string, object?> or List<object?> => JsonSerializer.Serialize(v, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
    };

    /// <summary>Finds the table inside any data: a list of records, a record of lists, or key/value pairs.</summary>
    static (List<string> head, List<object?[]> rows) Table(object? data)
    {
        if (data is Dictionary<string, object?> d)
        {
            var lists = d.Values.OfType<List<object?>>().ToList();
            if (d.Count == 1 && lists.Count == 1) data = lists[0];
            else if (lists.Count > 0 && d.Values.All(v => v is List<object?>)) data = lists[0]; // workbook: first sheet
            else return (["key", "value"], d.Select(p => new object?[] { p.Key, p.Value }).ToList());
        }
        if (data is not List<object?> list) return (["value"], [[data]]);
        var head = new List<string>();
        var seen = new HashSet<string>();
        foreach (var item in list)
            if (item is Dictionary<string, object?> r)
                foreach (var k in r.Keys) if (seen.Add(k)) head.Add(k);
        if (head.Count == 0) return (["value"], list.Select(x => new object?[] { x }).ToList());
        var rows = list.Select(item => item is Dictionary<string, object?> r
            ? head.Select(h => r.TryGetValue(h, out var v) ? v : null).ToArray()
            : new object?[] { item }).ToList();
        return (head, rows);
    }

    static void Excel(object? data, string file)
    {
        using var wb = new XLWorkbook();
        var sheets = data is Dictionary<string, object?> d && d.Count > 1 && d.Values.All(v => v is List<object?>)
            ? d.Select(p => (p.Key, p.Value)).ToList()
            : [("Data", data)];
        foreach (var (name, sheetData) in sheets)
        {
            var (head, rows) = Table(sheetData);
            string safe = new string(name.Where(c => c is not ('[' or ']' or '*' or '?' or '/' or '\\' or ':')).ToArray());
            var ws = wb.Worksheets.Add(safe.Length == 0 ? "Sheet" : safe.Length > 31 ? safe[..31] : safe);
            for (int c = 0; c < head.Count; c++) ws.Cell(1, c + 1).Value = head[c];
            for (int r = 0; r < rows.Count; r++)
                for (int c = 0; c < rows[r].Length; c++)
                {
                    var cell = ws.Cell(r + 2, c + 1);
                    switch (rows[r][c])
                    {
                        case null: break;
                        case long l: cell.Value = l; break;
                        case double x: cell.Value = x; break;
                        case bool b: cell.Value = b; break;
                        case var v: cell.Value = Text(v); break;
                    }
                }
            var header = ws.Range(1, 1, 1, Math.Max(1, head.Count));
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1EEF9");
            if (rows.Count > 0 && head.Count > 0) ws.Range(1, 1, rows.Count + 1, head.Count).SetAutoFilter();
            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents(1, Math.Min(rows.Count + 1, 500), 6, 60);
        }
        wb.SaveAs(file);
    }
}

/// <summary>RFC 4180 CSV: quotes, commas and line breaks inside cells, and the separator Excel used.</summary>
public static class Csv
{
    /// <summary>Comma, semicolon (Excel in Turkish and most of Europe) or tab — whichever splits the first lines evenly.</summary>
    public static char Sniff(string text)
    {
        var lines = text.Split('\n').Take(10).ToArray();
        char best = ',';
        int bestScore = -1;
        foreach (var sep in new[] { ',', ';', '\t', '|' })
        {
            var counts = lines.Where(l => l.Length > 0).Select(l => l.Count(c => c == sep)).ToArray();
            if (counts.Length == 0 || counts[0] == 0) continue;
            int score = counts.Count(c => c == counts[0]) * 10 + counts[0];
            if (score > bestScore) { bestScore = score; best = sep; }
        }
        return best;
    }

    public static List<string[]> Parse(string text, char sep)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = false;
                }
                else cell.Append(c);
                continue;
            }
            if (c == '"' && cell.Length == 0) quoted = true;
            else if (c == sep) { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString()); cell.Clear();
                rows.Add(row.ToArray()); row.Clear();
            }
            else if (c != '\uFEFF') cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row.ToArray()); }
        return rows;
    }

    public static string Write(List<string> head, List<object?[]> rows, char sep)
    {
        var sb = new StringBuilder();
        string Q(object? v)
        {
            string s = v switch
            {
                null => "",
                double d => d.ToString("R", CultureInfo.InvariantCulture),
                bool b => b ? "true" : "false",
                Dictionary<string, object?> or List<object?> => JsonSerializer.Serialize(v),
                _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
            };
            return s.IndexOfAny([sep, '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
        sb.AppendJoin(sep, head.Select(Q)).Append("\r\n");
        foreach (var r in rows) sb.AppendJoin(sep, r.Select(Q)).Append("\r\n");
        return sb.ToString();
    }
}
