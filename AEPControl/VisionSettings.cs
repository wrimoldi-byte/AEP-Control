using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AEPControl;

public sealed class VisionSettings
{
    public string Model { get; set; } = "gemini-3.5-flash-lite";
    public string ProtectedKey { get; set; } = "";
    public bool FreeProjectConfirmed { get; set; }
    public int DailyLimit { get; set; } = 100;
    public string UsageDate { get; set; } = "";
    public int RequestsToday { get; set; }
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AEPControl", "vision-settings.json");
    public static VisionSettings Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<VisionSettings>(File.ReadAllText(FilePath)) ?? new() : new(); }
        catch { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }
    public void SetApiKey(string key) => ProtectedKey = key.Length == 0 ? "" : Convert.ToBase64String(WindowsKeyProtection.Protect(Encoding.UTF8.GetBytes(key)));
    public string GetApiKey()
    {
        try { return Encoding.UTF8.GetString(WindowsKeyProtection.Unprotect(Convert.FromBase64String(ProtectedKey))); }
        catch { throw new InvalidOperationException("No se pudo abrir la clave protegida. Volvé a guardarla en Configurar IA desde este usuario de Windows."); }
    }
    public void ReserveRequest()
    {
        if (!FreeProjectConfirmed || ProtectedKey.Length == 0) throw new InvalidOperationException("Configurá la clave y confirmá un proyecto sin facturación en Configurar IA.");
        if (!Regex.IsMatch(Model, @"^gemini-[a-z0-9.-]+$")) throw new InvalidOperationException("Nombre de modelo inválido.");
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        if (UsageDate != date) { UsageDate = date; RequestsToday = 0; }
        if (RequestsToday >= Math.Clamp(DailyLimit, 1, 500)) throw new InvalidOperationException("Se alcanzó el límite diario configurado. Usá OCR local o edición manual.");
        RequestsToday++;
        Save();
    }
}

internal static class WindowsKeyProtection
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    public static byte[] Protect(byte[] value) => Transform(value, false);
    public static byte[] Unprotect(byte[] value) => Transform(value, true);
    private static byte[] Transform(byte[] value, bool decrypt)
    {
        var input = new Blob { Size = value.Length, Data = Marshal.AllocHGlobal(value.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(value, 0, input.Data, value.Length);
            var success = decrypt
                ? CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptProtectData(ref input, "AEP Control Gemini", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new InvalidOperationException("Windows no pudo proteger o abrir la clave.");
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }
}
