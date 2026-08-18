using System.Text.Json;
using System.Text.RegularExpressions;

namespace AEPControl;

public sealed class SpecialCodeSettings
{
    public List<string> Codes { get; set; } = DefaultCodes.ToList();

    public static readonly string[] DefaultCodes =
    {
        "WCHR", "WCHS", "WCHC", "AVIH", "INF",
        "UMNR", "PETC", "DEAF", "BLND", "MAAS", "STCR", "MEDA",
        "WCLB", "WCMP", "SVAN", "ESAN", "INAD", "DEPA", "DEPU"
    };

    private static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AEPControl");

    private static string FilePath => Path.Combine(Folder, "special-codes.json");

    public static SpecialCodeSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new SpecialCodeSettings();

            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<SpecialCodeSettings>(json) ?? new SpecialCodeSettings();
            loaded.Codes = Normalize(loaded.Codes);
            return loaded;
        }
        catch
        {
            return new SpecialCodeSettings();
        }
    }

    public void Save()
    {
        Codes = Normalize(Codes);
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static List<string> Normalize(IEnumerable<string> codes)
    {
        return codes
            .Select(c => (c ?? string.Empty).Trim().ToUpperInvariant())
            .Where(c => Regex.IsMatch(c, @"^[A-Z0-9]{3,6}$"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
