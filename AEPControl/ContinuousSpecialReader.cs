using System.Text.RegularExpressions;

namespace AEPControl;

public sealed class ContinuousSpecialReader
{
    private readonly HashSet<string> _uniqueRows = new(StringComparer.OrdinalIgnoreCase);

    public int UniqueRows => _uniqueRows.Count;

    public SpecialCounts AddOcrText(string text)
    {
        foreach (var raw in text.Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = Regex.Replace(raw.ToUpperInvariant(), @"\s+", " ").Trim();
            if (line.Length < 4) continue;
            if (!Regex.IsMatch(line, @"\b(WCHR|WCHS|WCHC|AVIH|INF)\b")) continue;
            _uniqueRows.Add(line);
        }

        return BuildCounts();
    }

    private SpecialCounts BuildCounts()
    {
        var result = new SpecialCounts();
        foreach (var row in _uniqueRows)
        {
            if (Regex.IsMatch(row, @"\bWCHR\b")) result.WCHR++;
            if (Regex.IsMatch(row, @"\bWCHS\b")) result.WCHS++;
            if (Regex.IsMatch(row, @"\bWCHC\b")) result.WCHC++;
            if (Regex.IsMatch(row, @"\bAVIH\b")) result.AVIH++;
            if (Regex.IsMatch(row, @"\bINF\b")) result.INF++;
        }
        return result;
    }
}
