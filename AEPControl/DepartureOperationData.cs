namespace AEPControl;

public sealed class DepartureOperationData
{
    public string Vuelo { get; set; } = string.Empty;
    public string Matricula { get; set; } = string.Empty;
    public string Configuracion { get; set; } = string.Empty;
    public string Servicios { get; set; } = string.Empty;

    public bool HasOperationalData =>
        !string.IsNullOrWhiteSpace(Matricula) ||
        !string.IsNullOrWhiteSpace(Configuracion) ||
        !string.IsNullOrWhiteSpace(Servicios);
}
