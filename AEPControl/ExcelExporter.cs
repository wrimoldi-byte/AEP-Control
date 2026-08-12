using System.IO.Compression;
using System.Text;
using System.Xml;

namespace AEPControl;

public static class ExcelExporter
{
    private const string SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelationshipsNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static void Export(string path, IEnumerable<FlightData> flights)
    {
        var arrivals = flights
            .Where(f => f.Movimiento.Equals("Llegada", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => SortTime(f.Hora)).ThenBy(f => f.Vuelo)
            .ToList();

        var departures = flights
            .Where(f => f.Movimiento.Equals("Salida", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => SortTime(f.Hora)).ThenBy(f => f.Vuelo)
            .ToList();

        if (File.Exists(path)) File.Delete(path);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        WriteTextEntry(archive, "[Content_Types].xml", ContentTypesXml());
        WriteTextEntry(archive, "_rels/.rels", RootRelationshipsXml());
        WriteTextEntry(archive, "xl/workbook.xml", WorkbookXml());
        WriteTextEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml());
        WriteTextEntry(archive, "xl/styles.xml", StylesXml());
        WriteWorksheet(archive, arrivals, departures);
    }

    private static void WriteWorksheet(ZipArchive archive, List<FlightData> arrivals, List<FlightData> departures)
    {
        var entry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
        using var stream = entry.Open();
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false };
        using var xw = XmlWriter.Create(stream, settings);

        xw.WriteStartDocument(true);
        xw.WriteStartElement("worksheet", SpreadsheetNs);
        xw.WriteStartElement("sheetViews", SpreadsheetNs);
        xw.WriteStartElement("sheetView", SpreadsheetNs);
        xw.WriteAttributeString("workbookViewId", "0");
        xw.WriteEndElement();
        xw.WriteEndElement();

        xw.WriteStartElement("cols", SpreadsheetNs);
        WriteCol(xw, 1, 1, 14);   // Arribo
        WriteCol(xw, 2, 2, 12);   // Tipo avion
        WriteCol(xw, 3, 4, 11);   // ETA/PAX
        WriteCol(xw, 5, 5, 25);   // Especial llegada
        WriteCol(xw, 6, 6, 14);   // Salida
        WriteCol(xw, 7, 7, 11);   // ETD
        WriteCol(xw, 8, 13, 12);  // Matricula..INF
        WriteCol(xw, 14, 14, 27); // Especial salida
        WriteCol(xw, 15, 18, 11); // PALL..ETO
        xw.WriteEndElement();

        xw.WriteStartElement("sheetData", SpreadsheetNs);

        // Título
        WriteRowStart(xw, 1, 26);
        WriteCell(xw, "A1", DateTime.Now.ToString("dd/MM/yyyy") + " - AEP CONTROL", 3);
        WriteRowEnd(xw);

        // Encabezados principales similares al modelo operativo.
        WriteRowStart(xw, 2, 34);
        string[] headers =
        {
            "ARRIBO", "TIPO AVIÓN", "ETA", "PAXS", "ESPECIAL",
            "SALIDA", "ETD", "MATRÍCULA", "ABE", "CONF", "PAXS", "SVCS", "INF",
            "ESPECIAL", "PALL E", "PALL UPG", "GPR +10", "ETO"
        };
        for (var i = 0; i < headers.Length; i++)
            WriteCell(xw, ColumnName(i + 1) + "2", headers[i], 2);
        WriteRowEnd(xw);

        var rows = Math.Max(arrivals.Count, departures.Count);
        for (var i = 0; i < rows; i++)
        {
            var row = i + 3;
            WriteRowStart(xw, row, 38);

            if (i < arrivals.Count)
            {
                var f = arrivals[i];
                WriteCell(xw, $"A{row}", FlightLabel(f), 4);
                WriteCellIfValue(xw, $"B{row}", f.Equipo, 1);
                WriteCellIfValue(xw, $"C{row}", f.Hora, 1);
                WriteNumberIfKnown(xw, $"D{row}", f.Total, f.Premium != 0 || f.Economy != 0, 1);
                WriteCellIfValue(xw, $"E{row}", SpecialText(f), 1);
            }

            if (i < departures.Count)
            {
                var f = departures[i];
                WriteCell(xw, $"F{row}", FlightLabel(f), 4);
                WriteCellIfValue(xw, $"G{row}", f.Hora, 1);
                // H, I, J quedan vacías: matrícula, ABE, CONF no se leen todavía.
                WriteNumberIfKnown(xw, $"K{row}", f.Total, f.Premium != 0 || f.Economy != 0, 1);
                // L SVCS: sin dato general todavía.
                WriteNumberIfKnown(xw, $"M{row}", f.INF, f.EspecialesLeidos, 1);
                WriteCellIfValue(xw, $"N{row}", SpecialText(f, excludeInf: true), 1);
                // O:R quedan vacías: PALL E, PALL UPG, GPR +10, ETO.
            }

            WriteRowEnd(xw);
        }

        // Una línea vacía con borde si no hay vuelos, para conservar el formato visual.
        if (rows == 0)
        {
            WriteRowStart(xw, 3, 38);
            for (var c = 1; c <= 18; c++) WriteBlankCell(xw, ColumnName(c) + "3", 1);
            WriteRowEnd(xw);
        }

        xw.WriteEndElement(); // sheetData

        xw.WriteStartElement("mergeCells", SpreadsheetNs);
        xw.WriteAttributeString("count", "1");
        xw.WriteStartElement("mergeCell", SpreadsheetNs);
        xw.WriteAttributeString("ref", "A1:R1");
        xw.WriteEndElement();
        xw.WriteEndElement();

        xw.WriteStartElement("pageMargins", SpreadsheetNs);
        xw.WriteAttributeString("left", "0.25");
        xw.WriteAttributeString("right", "0.25");
        xw.WriteAttributeString("top", "0.5");
        xw.WriteAttributeString("bottom", "0.5");
        xw.WriteAttributeString("header", "0.2");
        xw.WriteAttributeString("footer", "0.2");
        xw.WriteEndElement();

        xw.WriteEndElement();
        xw.WriteEndDocument();
    }

    private static string FlightLabel(FlightData f)
        => string.IsNullOrWhiteSpace(f.Destino) ? f.Vuelo : $"{f.Vuelo}\n{f.Destino}";

    private static string SpecialText(FlightData f, bool excludeInf = false)
    {
        if (!f.EspecialesLeidos) return string.Empty;
        var parts = new List<string>();
        if (f.WCHR > 0) parts.Add($"WCHR {f.WCHR}");
        if (f.WCHS > 0) parts.Add($"WCHS {f.WCHS}");
        if (f.WCHC > 0) parts.Add($"WCHC {f.WCHC}");
        if (f.AVIH > 0) parts.Add($"AVIH {f.AVIH}");
        if (!excludeInf && f.INF > 0) parts.Add($"INF {f.INF}");
        return string.Join(" / ", parts);
    }

    private static int SortTime(string value)
    {
        if (TimeSpan.TryParse(value, out var time)) return (int)time.TotalMinutes;
        return int.MaxValue;
    }

    private static void WriteRowStart(XmlWriter xw, int row, int height)
    {
        xw.WriteStartElement("row", SpreadsheetNs);
        xw.WriteAttributeString("r", row.ToString());
        xw.WriteAttributeString("ht", height.ToString());
        xw.WriteAttributeString("customHeight", "1");
    }

    private static void WriteRowEnd(XmlWriter xw) => xw.WriteEndElement();

    private static void WriteCellIfValue(XmlWriter xw, string reference, string? value, int style)
    {
        if (!string.IsNullOrWhiteSpace(value)) WriteCell(xw, reference, value, style);
    }

    private static void WriteNumberIfKnown(XmlWriter xw, string reference, int value, bool known, int style)
    {
        if (!known) return;
        xw.WriteStartElement("c", SpreadsheetNs);
        xw.WriteAttributeString("r", reference);
        xw.WriteAttributeString("s", style.ToString());
        xw.WriteStartElement("v", SpreadsheetNs);
        xw.WriteString(value.ToString());
        xw.WriteEndElement();
        xw.WriteEndElement();
    }

    private static void WriteCell(XmlWriter xw, string reference, string value, int style)
    {
        xw.WriteStartElement("c", SpreadsheetNs);
        xw.WriteAttributeString("r", reference);
        xw.WriteAttributeString("s", style.ToString());
        xw.WriteAttributeString("t", "inlineStr");
        xw.WriteStartElement("is", SpreadsheetNs);
        xw.WriteStartElement("t", SpreadsheetNs);
        if (value.StartsWith(' ') || value.EndsWith(' ') || value.Contains('\n'))
            xw.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
        xw.WriteString(value);
        xw.WriteEndElement();
        xw.WriteEndElement();
        xw.WriteEndElement();
    }

    private static void WriteBlankCell(XmlWriter xw, string reference, int style)
    {
        xw.WriteStartElement("c", SpreadsheetNs);
        xw.WriteAttributeString("r", reference);
        xw.WriteAttributeString("s", style.ToString());
        xw.WriteEndElement();
    }

    private static void WriteCol(XmlWriter xw, int min, int max, double width)
    {
        xw.WriteStartElement("col", SpreadsheetNs);
        xw.WriteAttributeString("min", min.ToString());
        xw.WriteAttributeString("max", max.ToString());
        xw.WriteAttributeString("width", width.ToString(System.Globalization.CultureInfo.InvariantCulture));
        xw.WriteAttributeString("customWidth", "1");
        xw.WriteEndElement();
    }

    private static string ColumnName(int number)
    {
        var result = string.Empty;
        while (number > 0)
        {
            number--;
            result = (char)('A' + number % 26) + result;
            number /= 26;
        }
        return result;
    }

    private static void WriteTextEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string ContentTypesXml() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
</Types>
""";

    private static string RootRelationshipsXml() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>
""";

    private static string WorkbookXml() => $"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="{SpreadsheetNs}" xmlns:r="{RelationshipsNs}">
  <sheets><sheet name="AEP Control" sheetId="1" r:id="rId1"/></sheets>
</workbook>
""";

    private static string WorkbookRelationshipsXml() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>
""";

    private static string StylesXml() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <fonts count="3">
    <font><sz val="10"/><name val="Calibri"/></font>
    <font><b/><sz val="10"/><name val="Calibri"/></font>
    <font><b/><sz val="13"/><name val="Calibri"/></font>
  </fonts>
  <fills count="3">
    <fill><patternFill patternType="none"/></fill>
    <fill><patternFill patternType="gray125"/></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FF9DC3DF"/><bgColor indexed="64"/></patternFill></fill>
  </fills>
  <borders count="2">
    <border><left/><right/><top/><bottom/><diagonal/></border>
    <border><left style="thin"><color rgb="FF707070"/></left><right style="thin"><color rgb="FF707070"/></right><top style="thin"><color rgb="FF707070"/></top><bottom style="thin"><color rgb="FF707070"/></bottom><diagonal/></border>
  </borders>
  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
  <cellXfs count="5">
    <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
    <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf>
    <xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf>
    <xf numFmtId="0" fontId="2" fillId="2" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
    <xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf>
  </cellXfs>
</styleSheet>
""";
}
