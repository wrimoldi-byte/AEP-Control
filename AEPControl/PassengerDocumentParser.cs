using System.Globalization;
using System.Text.RegularExpressions;

namespace AEPControl;

public static class PassengerDocumentParser
{
    private static readonly Regex DocumentRegex = new(
        @"(?<![A-Z0-9])(?<type>[IP])\s*/\s*(?<issuer>[A-Z]{2,3})\s*/\s*(?<number>[A-Z0-9]{4,20})\s*/\s*(?<nationality>[A-Z]{2,3})\s*/\s*(?<birth>\d{2}[A-Z]{3}\d{4})\s*/\s*(?<sex>[FMXU])\s*/\s*(?<expiration>\d{2}[A-Z]{3}\d{4})\s*/\s*(?<surname>[^/\r\n]+?)\s*/\s*(?<names>[^/\r\n]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] SabreDateFormats = ["ddMMMyyyy"];

    public static List<PassengerDocument> Parse(string ocrText)
    {
        var normalized = Normalize(ocrText);
        var documents = new List<PassengerDocument>();

        foreach (Match match in DocumentRegex.Matches(normalized))
        {
            if (!TryParseDate(match.Groups["birth"].Value, out var birthDate) ||
                !TryParseDate(match.Groups["expiration"].Value, out var expirationDate))
                continue;

            var document = new PassengerDocument
            {
                DocumentType = match.Groups["type"].Value,
                IssuingCountry = match.Groups["issuer"].Value,
                DocumentNumber = match.Groups["number"].Value,
                Nationality = match.Groups["nationality"].Value,
                BirthDate = birthDate,
                Sex = match.Groups["sex"].Value,
                ExpirationDate = expirationDate,
                Surname = CleanName(match.Groups["surname"].Value),
                GivenNames = CleanName(match.Groups["names"].Value)
            };

            if (!documents.Any(existing =>
                    existing.DocumentType == document.DocumentType &&
                    existing.DocumentNumber == document.DocumentNumber))
                documents.Add(document);
        }

        return documents;
    }

    private static bool TryParseDate(string value, out DateTime date) =>
        DateTime.TryParseExact(
            value,
            SabreDateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);

    private static string CleanName(string value) =>
        Regex.Replace(value.Trim(), @"\s+", " ");

    private static string Normalize(string value)
    {
        value = value.ToUpperInvariant()
            .Replace('\r', '\n')
            .Replace('—', '-')
            .Replace('–', '-');
        value = Regex.Replace(value, @"[ \t]+", " ");
        value = Regex.Replace(value, @"\n+", "\n");
        return value;
    }
}
