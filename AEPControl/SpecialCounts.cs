namespace AEPControl;

public sealed class SpecialCounts
{
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

    public int Total => WCHR + WCHS + WCHC + AVIH + INF + UMNR + PETC + DEAF + BLND + MAAS + STCR + MEDA + WCLBD + WCMP + SVAN + ESAN + INAD + DEPA + DEPU;
}
