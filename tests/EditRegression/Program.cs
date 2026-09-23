using AEPControl;

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    Console.WriteLine("PASS: " + description);
}

var reader = new ContinuousSpecialReader();
Check(reader.AddOcrText("29D ETO PEREZ/JUAN").ETO == 0,
    "One transient OCR frame cannot create an edit");
reader.AddOcrText("21D ETO PEREZ/JUAN");
Check(reader.AddOcrText("21D ETO PEREZ/JUAN").ETO == 1,
    "Stable reading confirms one edit after transient seat noise");

reader = new ContinuousSpecialReader();
reader.AddOcrText("12A AVIH GONZALEZ/MARIA");
reader.AddOcrText("12A AVIH GONZALEZ/MARIA");
Check(reader.AddOcrText("12A AVIH GONXALEX/MARTA").AVIH == 1,
    "Name OCR noise at the same seat does not add a duplicate");
reader.AddOcrText("22B AVIH TORRES/PEDRO");
Check(reader.AddOcrText("22B AVIH TORRES/PEDRO").AVIH == 2,
    "A separate passenger is still counted");
reader.AddOcrText("");
for (var i = 0; i < 5; i++)
    Check(reader.AddOcrText("12A AVIH GONZALEZ/MARIA\n22B AVIH TORRES/PEDRO").AVIH == 2,
        "Returning to overlapping rows preserves the total");

reader = new ContinuousSpecialReader();
const string samePax = "12A WCHR PEREZ/JUAN\n12A WCHC PEREZ/JUAN";
reader.AddOcrText(samePax);
var counts = reader.AddOcrText(samePax);
Check(counts.WCHC == 1 && counts.WCHR == 0,
    "WCHC takes priority for the same passenger");

reader = new ContinuousSpecialReader();
const string differentPax = "12A WCHR PEREZ/JUAN\n12A WCHC GOMEZ/ANA\n22B WCHS TORRES/PEDRO";
reader.AddOcrText(differentPax);
counts = reader.AddOcrText(differentPax);
Check(counts.WCHR == 1 && counts.WCHC == 1 && counts.WCHS == 1,
    "Wheelchairs for other named passengers survive a reused or misread seat");

reader = new ContinuousSpecialReader();
const string unnamed = "WCHR 123456789012345678901234567890\nWCHC 123456789012345678901234567890";
reader.AddOcrText(unnamed);
counts = reader.AddOcrText(unnamed);
Check(counts.WCHR == 1 && counts.WCHC == 1,
    "Similar anonymous text is not evidence to suppress another wheelchair type");
