namespace AEPControl;

public sealed class SpecialCounts
{
    public int WCHR { get; set; }
    public int WCHS { get; set; }
    public int WCHC { get; set; }
    public int AVIH { get; set; }
    public int INF { get; set; }
    public int Total => WCHR + WCHS + WCHC + AVIH + INF;
}
