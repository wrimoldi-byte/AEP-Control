namespace AEPControl;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var form = new BubbleMainForm();
        Application.Run(form);
    }
}
