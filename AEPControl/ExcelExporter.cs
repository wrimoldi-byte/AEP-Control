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
        using var xw = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false });

        xw.WriteStartDocument(true);
        xw.WriteStartElement("worksheet", SpreadsheetNs);

        xw.WriteStartElement("sheetViews", SpreadsheetNs);
        xw.WriteStartElement("sheetView", SpreadsheetNs);
        xw.WriteAttributeString("workbookViewId", "0");
        xw.WriteAttributeString("showGridLines", "0");
        xw.WriteStartElement("pane", SpreadsheetNs);
        xw.WriteAttributeString("ySplit", "3");
        xw.WriteAttributeString("topLeftCell", "A4");
        xw.WriteAttributeString("activePane", "bottomLeft");
        xw.WriteAttributeString("state", "frozen");
        xw.WriteEndElement();
        xw.WriteEndElement();
        xw.WriteEndElement();

        xw.WriteStartElement("cols", SpreadsheetNs);
        WriteCol(xw, 1, 1, 14);
        WriteCol(xw, 2, 2, 11);
        WriteCol(xw, 3, 4, 10);
        WriteCol(xw, 5, 5, 28);
        WriteCol(xw, 6, 6, 14);
        WriteCol(xw, 7, 7, 10);
        WriteCol(xw, 8, 10, 12);
        WriteCol(xw, 11, 13, 10);
        WriteCol(xw, 14, 14, 30);
        WriteCol(xw, 15, 18, 10);
        xw.WriteEndElement();

        xw.WriteStartElement("sheetData", SpreadsheetNs);

        WriteRowStart(xw, 1, 30);
        WriteCell(xw, "A1", $"AEP · TURNO {CurrentShift()} | {DateTime.Now:dd/MM/yyyy}", 3);
        WriteRowEnd(xw);

        WriteRowStart(xw, 2, 24);
        WriteCell(xw, "A2", "✈  ARRIBOS", 5);
        WriteCell(xw, "F2", "✈  SALIDAS", 5);
        WriteRowEnd(xw);

        WriteRowStart(xw, 3, 30);
        string[] headers =
        {
            "ARRIBO", "AVIÓN", "ETA", "PAX (PE/ECO)", "ESPECIAL",
            "SALIDA", "ETD", "MATRÍCULA", "ABE", "CONF", "PAX (PE/ECO)", "SVCS", "INF",
            "ESPECIAL", "PALL E", "PALL UPG", "GPR +10", "ETO"
        };
        for (var i = 0; i < headers.Length; i++)
            WriteCell(xw, ColumnName(i + 1) + "3", headers[i], 2);
        WriteRowEnd(xw);

        var rows = Math.Max(arrivals.Count, departures.Count);
        for (var i = 0; i < rows; i++)
        {
            var row = i + 4;
            var bodyStyle = i % 2 == 0 ? 1 : 6;
            WriteRowStart(xw, row, 38);

            if (i < arrivals.Count)
            {
                var f = arrivals[i];
                WriteCell(xw, $"A{row}", FlightLabel(f), 4);
                WriteCellOrDash(xw, $"B{row}", f.Equipo, bodyStyle);
                WriteCellOrDash(xw, $"C{row}", f.Hora, bodyStyle);
                WriteCellOrDash(xw, $"D{row}", f.Booking, bodyStyle);
                WriteCellOrDash(xw, $"E{row}", SpecialText(f), 9);
            }
            else
                WriteStyledBlanks(xw, row, 1, 5, bodyStyle);

            if (i < departures.Count)
            {
                var f = departures[i];
                WriteCell(xw, $"F{row}", FlightLabel(f), 4);
                WriteCellOrDash(xw, $"G{row}", f.Hora, 7);
                // H/I/J quedan disponibles para MATRÍCULA, ABE y CONF cuando incorporemos esas lecturas.
                WriteCellOrDash(xw, $"H{row}", string.Empty, bodyStyle);
                WriteCellOrDash(xw, $"I{row}", string.Empty, bodyStyle);
                WriteCellOrDash(xw, $"J{row}", string.Empty, bodyStyle);
                WriteCellOrDash(xw, $"K{row}", f.Booking, bodyStyle);
                // L (SVCS) queda disponible para la lectura general de servicios.
                WriteCellOrDash(xw, $"L{row}", string.Empty, bodyStyle);
                WriteNumberOrDash(xw, $"M{row}", f.INF, f.EspecialesLeidos, bodyStyle);
                WriteCellOrDash(xw, $"N{row}", SpecialText(f, excludeDedicated: true), 9);
                WriteCellOrDash(xw, $"O{row}", string.Empty, bodyStyle);
                WriteCellOrDash(xw, $"P{row}", string.Empty, bodyStyle);
                WriteCellOrDash(xw, $"Q{row}", string.Empty, bodyStyle);
                WriteNumberOrDash(xw, $"R{row}", f.ETO, f.EspecialesLeidos, bodyStyle);
            }
            else
                WriteStyledBlanks(xw, row, 6, 18, bodyStyle);

            WriteRowEnd(xw);
        }

        if (rows == 0)
        {
            WriteRowStart(xw, 4, 38);
            for (var c = 1; c <= 18; c++) WriteBlankCell(xw, ColumnName(c) + "4", 1);
            WriteRowEnd(xw);
        }

        var lastDataRow = 3 + Math.Max(rows, 1);
        var footerRow = lastDataRow + 2;
        WriteRowStart(xw, footerRow, 22);
        WriteCell(xw, $"A{footerRow}", "LECTURA RÁPIDA  ·  Azul: vuelos y operación  ·  Ámbar: horario ETD  ·  PAX: PE/Economy", 10);
        WriteRowEnd(xw);

        xw.WriteEndElement();

        xw.WriteStartElement("autoFilter", SpreadsheetNs);
        xw.WriteAttributeString("ref", $"A3:R{lastDataRow}");
        xw.WriteEndElement();

        xw.WriteStartElement("mergeCells", SpreadsheetNs);
        xw.WriteAttributeString("count", "4");
        WriteMerge(xw, "A1:R1");
        WriteMerge(xw, "A2:E2");
        WriteMerge(xw, "F2:R2");
        WriteMerge(xw, $"A{footerRow}:R{footerRow}");
        xw.WriteEndElement();

        xw.WriteStartElement("pageMargins", SpreadsheetNs);
        xw.WriteAttributeString("left", "0.25");
        xw.WriteAttributeString("right", "0.25");
        xw.WriteAttributeString("top", "0.5");
        xw.WriteAttributeString("bottom", "0.5");
        xw.WriteAttributeString("header", "0.2");
        xw.WriteAttributeString("footer", "0.2");
        xw.WriteEndElement();

        xw.WriteStartElement("pageSetup", SpreadsheetNs);
        xw.WriteAttributeString("orientation", "landscape");
        xw.WriteAttributeString("fitToWidth", "1");
        xw.WriteAttributeString("fitToHeight", "0");
        xw.WriteEndElement();

        xw.WriteEndElement();
        xw.WriteEndDocument();
    }

    private static string FlightLabel(FlightData f)
        => string.IsNullOrWhiteSpace(f.Destino) ? f.Vuelo : $"{f.Vuelo}\n{f.Destino}";

    private static string SpecialText(FlightData f, bool excludeDedicated = false)
    {
        if (!f.EspecialesLeidos) return string.Empty;

        var parts = new List<string>();
        Add(parts, "WCHR", f.WCHR);
        Add(parts, "WCHS", f.WCHS);
        Add(parts, "WCHC", f.WCHC);
        Add(parts, "AVIH", f.AVIH);
        if (!excludeDedicated) Add(parts, "INF", f.INF);
        if (!excludeDedicated) Add(parts, "ETO", f.ETO);
        Add(parts, "UMNR", f.UMNR);
        Add(parts, "PETC", f.PETC);
        Add(parts, "DEAF", f.DEAF);
        Add(parts, "BLND", f.BLND);
        Add(parts, "MAAS", f.MAAS);
        Add(parts, "STCR", f.STCR);
        Add(parts, "MEDA", f.MEDA);
        Add(parts, "WCLB", f.WCLB);
        Add(parts, "WCMP", f.WCMP);
        Add(parts, "SVAN", f.SVAN);
        Add(parts, "ESAN", f.ESAN);
        Add(parts, "INAD", f.INAD);
        Add(parts, "DEPA", f.DEPA);
        Add(parts, "DEPU", f.DEPU);

        foreach (var item in f.ExtraSpecialCounts.OrderBy(x => x.Key))
        {
            if (excludeDedicated &&
                (item.Key.Equals("INF", StringComparison.OrdinalIgnoreCase) ||
                 item.Key.Equals("ETO", StringComparison.OrdinalIgnoreCase))) continue;
            Add(parts, item.Key, item.Value);
        }

        return string.Join(" / ", parts);
    }

    private static void Add(List<string> parts, string code, int value)
    {
        if (value > 0) parts.Add($"{code} {value}");
    }

    private static int SortTime(string value)
    {
        if (TimeSpan.TryParse(value, out var time)) return (int)time.TotalMinutes;
        if (value.Length == 4 && int.TryParse(value, out var hhmm))
            return (hhmm / 100) * 60 + hhmm % 100;
        return int.MaxValue;
    }

    private static string CurrentShift()
    {
        var hour = DateTime.Now.Hour;
        if (hour < 13) return "MAÑANA";
        if (hour < 21) return "TARDE";
        return "NOCHE";
    }

    private static void WriteRowStart(XmlWriter xw, int row, int height)
    {
        xw.WriteStartElement("row", SpreadsheetNs);
        xw.WriteAttributeString("r", row.ToString());
        xw.WriteAttributeString("ht", height.ToString());
        xw.WriteAttributeString("customHeight", "1");
    }

    private static void WriteRowEnd(XmlWriter xw) => xw.WriteEndElement();

    private static void WriteCellOrDash(XmlWriter xw, string reference, string? value, int style)
    {
        WriteCell(xw, reference, string.IsNullOrWhiteSpace(value) ? "—" : value, style);
    }

    private static void WriteNumberOrDash(XmlWriter xw, string reference, int value, bool known, int style)
    {
        if (!known)
        {
            WriteCell(xw, reference, "—", style);
            return;
        }
        xw.WriteStartElement("c", SpreadsheetNs);
        xw.WriteAttributeString("r", reference);
        xw.WriteAttributeString("s", style.ToString());
        xw.WriteStartElement("v", SpreadsheetNs);
        xw.WriteString(value.ToString());
        xw.WriteEndElement();
        xw.WriteEndElement();
    }

    private static void WriteStyledBlanks(XmlWriter xw, int row, int firstColumn, int lastColumn, int style)
    {
        for (var column = firstColumn; column <= lastColumn; column++)
            WriteCell(xw, $"{ColumnName(column)}{row}", "—", style);
    }

    private static void WriteMerge(XmlWriter xw, string range)
    {
        xw.WriteStartElement("mergeCell", SpreadsheetNs);
        xw.WriteAttributeString("ref", range);
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
  <fonts count="7">
    <font><sz val="10"/><color rgb="FF243849"/><name val="Calibri"/></font>
    <font><b/><sz val="10"/><color rgb="FF243849"/><name val="Calibri"/></font>
    <font><b/><sz val="15"/><color rgb="FFFFFFFF"/><name val="Calibri"/></font>
    <font><b/><sz val="10"/><color rgb="FFFFFFFF"/><name val="Calibri"/></font>
    <font><b/><sz val="10"/><color rgb="FF0B4C70"/><name val="Calibri"/></font>
    <font><b/><sz val="10"/><color rgb="FF9C6500"/><name val="Calibri"/></font>
    <font><i/><sz val="9"/><color rgb="FF6E8295"/><name val="Calibri"/></font>
  </fonts>
  <fills count="9">
    <fill><patternFill patternType="none"/></fill>
    <fill><patternFill patternType="gray125"/></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FF172D4A"/><bgColor indexed="64"/></patternFill></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FF2F73A3"/><bgColor indexed="64"/></patternFill></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FFDDEBF7"/><bgColor indexed="64"/></patternFill></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FFD9E8F5"/><bgColor indexed="64"/></patternFill></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FFF4F7FA"/><bgColor indexed="64"/></patternFill></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FFFFF2CC"/><bgColor indexed="64"/></patternFill></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FFFCE4D6"/><bgColor indexed="64"/></patternFill></fill>
  </fills>
  <borders count="2">
    <border><left/><right/><top/><bottom/><diagonal/></border>
    <border><left style="thin"><color rgb="FFD7E2EA"/></left><right style="thin"><color rgb="FFD7E2EA"/></right><top style="thin"><color rgb="FFD7E2EA"/></top><bottom style="thin"><color rgb="FFD7E2EA"/></bottom><diagonal/></border>
  </borders>
  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
  <cellXfs count="11">
    <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
    <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyFont="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf>
    <xf numFmtId="0" fontId="3" fillId="3" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf>
    <xf numFmtId="0" fontId="2" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
    <xf numFmtId="0" fontId="4" fillId="4" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf>
    <xf numFmtId="0" fontId="1" fillId="5" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
    <xf numFmtId="0" fontId="0" fillId="6" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf>
    <xf numFmtId="0" fontId="5" fillId="7" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
    <xf numFmtId="0" fontId="1" fillId="8" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf>
    <xf numFmtId="0" fontId="4" fillId="5" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf>
    <xf numFmtId="0" fontId="6" fillId="6" borderId="0" xfId="0" applyFont="1" applyFill="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
  </cellXfs>
</styleSheet>
""";
}
