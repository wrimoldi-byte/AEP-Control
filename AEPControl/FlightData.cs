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
    public int Total => Premium + Economy;
    public string Booking => $"{Premium:000}/{Economy:000}";

    public int WCHR { get; set; }
    public int WCHS { get; set; }
    public int WCHC { get; set; }
    public int AVIH { get; set; }
    public int INF { get; set; }
    public bool EspecialesLeidos { get; set; }
}
