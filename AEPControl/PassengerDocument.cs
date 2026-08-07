namespace AEPControl;

public sealed class PassengerDocument
{
    public string DocumentType { get; init; } = string.Empty;
    public string IssuingCountry { get; init; } = string.Empty;
    public string DocumentNumber { get; init; } = string.Empty;
    public string Nationality { get; init; } = string.Empty;
    public DateTime BirthDate { get; init; }
    public string Sex { get; init; } = string.Empty;
    public DateTime ExpirationDate { get; init; }
    public string Surname { get; init; } = string.Empty;
    public string GivenNames { get; init; } = string.Empty;

    public string DocumentTypeName => DocumentType switch
    {
        "I" => "Identidad",
        "P" => "Pasaporte",
        _ => DocumentType
    };

    public string MaskedDocumentNumber
    {
        get
        {
            if (DocumentNumber.Length <= 4) return new string('*', DocumentNumber.Length);
            return new string('*', DocumentNumber.Length - 4) + DocumentNumber[^4..];
        }
    }

    public string BirthDateText => BirthDate.ToString("dd/MM/yyyy");
    public string ExpirationDateText => ExpirationDate.ToString("dd/MM/yyyy");
}
