namespace AEPControl;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var form = new BubbleMainForm();
        FlightSelectionEnhancer.Attach(form);
        Application.Run(form);
    }
}
