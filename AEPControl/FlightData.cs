namespace AEPControl;

public sealed class FlightData
{
    public string Movimiento { get; set; } = "";
    public string Vuelo { get; set; } = "";
    public string Destino { get; set; } = "";
    public string Hora { get; set; } = "";
    public string Equipo { get; set; } = "";
    public int Premium { get; set; }
    public int Economy { get; set; }
    public bool BookingKnown { get; set; }
    public int Total => Premium + Economy;
    public string Booking => BookingKnown ? $"{Premium:000}/{Economy:000}" : string.Empty;

    public int WCHR { get; set; }
    public int WCHS { get; set; }
    public int WCHC { get; set; }
    public int AVIH { get; set; }
    public int INF { get; set; }

    public int UMNR { get; set; }
    public int PETC { get; set; }
    public int DEAF { get; set; }
    public int BLND { get; set; }
    public int MAAS { get; set; }
    public int STCR { get; set; }
    public int MEDA { get; set; }
    public int WCLBD { get; set; }
    public int WCMP { get; set; }
    public int SVAN { get; set; }
    public int ESAN { get; set; }
    public int INAD { get; set; }
    public int DEPA { get; set; }
    public int DEPU { get; set; }

    public string OtrosEspeciales
    {
        get
        {
            var parts = new List<string>();
            Add(parts, "UMNR", UMNR); Add(parts, "PETC", PETC); Add(parts, "DEAF", DEAF);
            Add(parts, "BLND", BLND); Add(parts, "MAAS", MAAS); Add(parts, "STCR", STCR);
            Add(parts, "MEDA", MEDA); Add(parts, "WCLBD", WCLBD); Add(parts, "WCMP", WCMP);
            Add(parts, "SVAN", SVAN); Add(parts, "ESAN", ESAN); Add(parts, "INAD", INAD);
            Add(parts, "DEPA", DEPA); Add(parts, "DEPU", DEPU);
            return string.Join(" · ", parts);
        }
    }

    public bool EspecialesLeidos { get; set; }

    private static void Add(List<string> parts, string code, int value)
    {
        if (value > 0) parts.Add($"{code} {value}");
    }
}
