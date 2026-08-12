using System.IO.Compression;
using System.Text;
using System.Xml;

namespace AEPControl;

public static class PassengerDocumentExcelExporter
{
    private const string SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelationshipsNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static void Export(string path, IReadOnlyList<PassengerDocument> documents)
    {
        if (File.Exists(path)) File.Delete(path);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        WriteTextEntry(archive, "[Content_Types].xml", ContentTypesXml());
        WriteTextEntry(archive, "_rels/.rels", RootRelationshipsXml());
        WriteTextEntry(archive, "xl/workbook.xml", WorkbookXml());
        WriteTextEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml());
        WriteTextEntry(archive, "xl/styles.xml", StylesXml());
        WriteWorksheet(archive, documents);
    }

    private static void WriteWorksheet(ZipArchive archive, IReadOnlyList<PassengerDocument> documents)
    {
        var entry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var xw = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false });

        xw.WriteStartDocument(true);
        xw.WriteStartElement("worksheet", SpreadsheetNs);

        xw.WriteStartElement("cols", SpreadsheetNs);
        WriteCol(xw, 1, 1, 16);
        WriteCol(xw, 2, 2, 14);
        WriteCol(xw, 3, 3, 22);
        WriteCol(xw, 4, 4, 16);
        WriteCol(xw, 5, 7, 16);
        WriteCol(xw, 8, 8, 24);
        WriteCol(xw, 9, 9, 28);
        xw.WriteEndElement();

        xw.WriteStartElement("sheetData", SpreadsheetNs);

        WriteRowStart(xw, 1, 28);
        WriteCell(xw, "A1", $"DOCUMENTACIÓN PAX - {DateTime.Now:dd/MM/yyyy}", 3);
        WriteRowEnd(xw);

        string[] headers =
        {
            "TIPO DOCUMENTO", "PAÍS EMISOR", "NÚMERO DOCUMENTO", "NACIONALIDAD",
            "FECHA NACIMIENTO", "SEXO", "VENCIMIENTO", "APELLIDO", "NOMBRES"
        };

        WriteRowStart(xw, 2, 32);
        for (var i = 0; i < headers.Length; i++)
            WriteCell(xw, ColumnName(i + 1) + "2", headers[i], 2);
        WriteRowEnd(xw);

        for (var i = 0; i < documents.Count; i++)
        {
            var d = documents[i];
            var row = i + 3;
            WriteRowStart(xw, row, 24);
            WriteCell(xw, $"A{row}", d.DocumentTypeName, 1);
            WriteCell(xw, $"B{row}", d.IssuingCountry, 1);
            WriteCell(xw, $"C{row}", d.DocumentNumber, 1);
            WriteCell(xw, $"D{row}", d.Nationality, 1);
            WriteCell(xw, $"E{row}", d.BirthDateText, 1);
            WriteCell(xw, $"F{row}", d.Sex, 1);
            WriteCell(xw, $"G{row}", d.ExpirationDateText, 1);
            WriteCell(xw, $"H{row}", d.Surname, 1);
            WriteCell(xw, $"I{row}", d.GivenNames, 1);
            WriteRowEnd(xw);
        }

        xw.WriteEndElement(); // sheetData

        xw.WriteStartElement("mergeCells", SpreadsheetNs);
        xw.WriteAttributeString("count", "1");
        xw.WriteStartElement("mergeCell", SpreadsheetNs);
        xw.WriteAttributeString("ref", "A1:I1");
        xw.WriteEndElement();
        xw.WriteEndElement();

        xw.WriteEndElement();
        xw.WriteEndDocument();
    }

    private static void WriteRowStart(XmlWriter xw, int row, int height)
    {
        xw.WriteStartElement("row", SpreadsheetNs);
        xw.WriteAttributeString("r", row.ToString());
        xw.WriteAttributeString("ht", height.ToString());
        xw.WriteAttributeString("customHeight", "1");
    }

    private static void WriteRowEnd(XmlWriter xw) => xw.WriteEndElement();

    private static void WriteCell(XmlWriter xw, string reference, string? value, int style)
    {
        xw.WriteStartElement("c", SpreadsheetNs);
        xw.WriteAttributeString("r", reference);
        xw.WriteAttributeString("s", style.ToString());
        xw.WriteAttributeString("t", "inlineStr");
        xw.WriteStartElement("is", SpreadsheetNs);
        xw.WriteStartElement("t", SpreadsheetNs);
        xw.WriteString(value ?? string.Empty);
        xw.WriteEndElement();
        xw.WriteEndElement();
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
  <sheets><sheet name="Documentación PAX" sheetId="1" r:id="rId1"/></sheets>
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
    <border><left style="thin"/><right style="thin"/><top style="thin"/><bottom style="thin"/><diagonal/></border>
  </borders>
  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
  <cellXfs count="4">
    <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
    <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyAlignment="1"><alignment vertical="center"/></xf>
    <xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf>
    <xf numFmtId="0" fontId="2" fillId="2" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
  </cellXfs>
</styleSheet>
""";
}
