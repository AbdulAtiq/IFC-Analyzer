using System.Diagnostics;
using Xbim.Common;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

// ============================================================================
// Lasttest (Nachtrag zu Phase 1) — derselbe Spike-Code (Messen(), dieselben
// xBIM-Aufrufe wie in spike/Program.cs Schritt 1/2/5/6/14), nur gegen eine
// große, echte Projektdatei statt der 6-Elemente-Beispieldatei. KEIN neuer
// Ansatz, KEINE Optimierung — es geht ausschließlich um Messwerte bei
// realistischer Modellgröße. Die Schreiboperationen (10–13) sind laut
// Aufgabenstellung nicht zu wiederholen: ihr Verhalten hängt an der
// Einzeloperation, nicht an der Modellgröße.
//
// Aufruf: dotnet run -c Release -- <pfad-zur-ifc-datei>
// ============================================================================

if (args.Length < 1)
{
    Console.WriteLine("Aufruf: dotnet run -c Release -- <pfad-zur-ifc-datei>");
    return 1;
}

var ifcPfad = Path.GetFullPath(args[0]);
var dateiInfo = new FileInfo(ifcPfad);
if (!dateiInfo.Exists)
{
    Console.WriteLine($"FEHLER: Datei nicht gefunden: {ifcPfad}");
    return 1;
}

Console.WriteLine("=== Lasttest: Schritt 1 — Open(pfad) + Schema ===");
Console.WriteLine($"Datei: {ifcPfad}");
Console.WriteLine($"Größe: {dateiInfo.Length / 1024.0 / 1024.0:F2} MB");
Console.WriteLine();

IfcStore? modell = Messen("1. Open(pfad)", () => IfcStore.Open(ifcPfad));

if (modell is null)
{
    Console.WriteLine("FEHLSCHLAG: Modell konnte nicht geöffnet werden. Das ist ein Ergebnis, kein Bug im Lasttest.");
    return 1;
}

Console.WriteLine($"Erfolg: Datei geöffnet.");
Console.WriteLine($"  Schema (SchemaVersion) : {modell.SchemaVersion}");

Console.WriteLine();
Console.WriteLine("=== Schritt 2 — GetElementsByType(...) inkl. Klassenhierarchie ===");

List<IIfcProduct>? produkte = null;
foreach (var klasse in new[] { "IfcProduct", "IfcObject", "IfcBuildingElement" })
{
    var elemente = Messen($"2. GetElementsByType(\"{klasse}\")",
        () => modell.Instances.OfType(klasse, activate: true).ToList());
    Console.WriteLine($"  Treffer: {elemente?.Count ?? 0}");
    if (klasse == "IfcProduct")
        produkte = elemente?.Cast<IIfcProduct>().ToList();
}

// Echter Fund an dieser Datei (Schema IFC4X3): der String-basierte Weg
// (Instances.OfType(string, bool), s.o.) wirft für "IfcBuildingElement"
// eine ArgumentException ("StringType must be a name of the existing
// persist entity type") -- die EXPRESS-Entity IfcBuildingElement wurde in
// IFC4X3 aus der Klassenhierarchie entfernt (Elemente wie IfcWall erben
// jetzt direkt von IfcElement). Der generische, stark typisierte Weg
// (Instances.OfType<IIfcBuildingElement>()) funktioniert dagegen weiter,
// weil xBIMs Cross-Schema-Interface IIfcBuildingElement (Xbim.Ifc4.Interfaces)
// unabhängig vom tatsächlichen EXPRESS-Namen im jeweiligen Schema besteht.
// Nicht Teil der ursprünglichen 14 Schritte, aber ein Praxisfund: der
// String-basierte Abstraktionsweg ist NICHT über alle IFC-Schemaversionen
// hinweg stabil für Oberklassen, die zwischen Schemaversionen umstrukturiert
// wurden.
Messen("2b. GetElementsByType<IIfcBuildingElement>() (generisch, als Ausweg für obigen Fund)",
    () => modell.Instances.OfType<IIfcBuildingElement>().ToList());

Console.WriteLine();
Console.WriteLine("=== Eckdaten der Datei ===");

var anzahlIfcProduct = produkte?.Count ?? 0;
var alleElemente = Messen("Elemente einlesen (IIfcElement, für Schritt 5)",
    () => modell.Instances.OfType<IIfcElement>().ToList()) ?? new List<IIfcElement>();
var allePropertySetsVorab = modell.Instances.OfType<IIfcPropertySet>().ToList();
var anzahlIfcPropertySet = allePropertySetsVorab.Count;
var gesamtzahlAttributWerte = allePropertySetsVorab.Sum(p => p.HasProperties.Count);

Console.WriteLine($"  IfcProduct (inkl. Unterklassen) : {anzahlIfcProduct}");
Console.WriteLine($"  IfcElement (für Schritt 5)       : {alleElemente.Count}");
Console.WriteLine($"  IfcPropertySet                   : {anzahlIfcPropertySet}");
Console.WriteLine($"  Attribut-Werte über alle P-Sets   : {gesamtzahlAttributWerte}");

Console.WriteLine();
Console.WriteLine("=== Schritt 5 — PropertySets je Element (über ALLE Elemente) ===");

var uhrSchritt5 = Stopwatch.StartNew();
var ergebnisSchritt5 = Messen($"5. PropertySets aller {alleElemente.Count} Elemente lesen", () =>
{
    int anzahlPsets = 0, anzahlQsets = 0, anzahlWerte = 0;
    foreach (var element in alleElemente)
    {
        foreach (var rel in element.IsDefinedBy)
        {
            switch (rel.RelatingPropertyDefinition)
            {
                case IIfcPropertySet pset:
                    anzahlPsets++;
                    foreach (var prop in pset.HasProperties)
                    {
                        if (prop is IIfcPropertySingleValue wert)
                        {
                            anzahlWerte++;
                            _ = wert.NominalValue?.Value; // Zugriff erzwingen, wie in der Anwendung
                        }
                    }
                    break;

                case IIfcElementQuantity qset:
                    anzahlQsets++;
                    foreach (var menge in qset.Quantities)
                        _ = menge.Name;
                    break;
            }
        }
    }
    return (anzahlPsets, anzahlQsets, anzahlWerte);
});
uhrSchritt5.Stop();

Console.WriteLine($"  Insgesamt: {ergebnisSchritt5.anzahlPsets} PropertySets, {ergebnisSchritt5.anzahlQsets} Mengensätze, {ergebnisSchritt5.anzahlWerte} Einzelwerte gelesen.");
if (alleElemente.Count > 0)
{
    double proElement = uhrSchritt5.Elapsed.TotalMilliseconds / alleElemente.Count;
    Console.WriteLine($"  Laufzeit pro 1.000 Elemente: {proElement * 1000:F1} ms");
}

Console.WriteLine();
Console.WriteLine("=== Schritt 6 — GetAllPropertySets(): alle P-Sets ohne Umweg über Elemente ===");

var allePropertySets = Messen("6. GetAllPropertySets()",
    () => modell.Instances.OfType<IIfcPropertySet>().ToList());
Console.WriteLine($"  Treffer: {allePropertySets?.Count ?? 0} IfcPropertySet-Entities im gesamten Modell.");

Console.WriteLine();
Console.WriteLine("=== Schritt 14 — Save(pfad): Modell speichern (ohne vorherige Änderungen) ===");

var ausgabePfad = Path.Combine(Path.GetTempPath(), $"lasttest-ausgabe-{Guid.NewGuid():N}.ifc");

Messen("14. Save(pfad)", () =>
{
    modell.SaveAs(ausgabePfad, Xbim.IO.StorageType.Ifc);
    return true;
});

var groesseAusgabe = new FileInfo(ausgabePfad).Length;
Console.WriteLine($"  Gespeichert nach: {ausgabePfad}");
Console.WriteLine($"  Dateigröße: {groesseAusgabe / 1024.0 / 1024.0:F2} MB (Original: {dateiInfo.Length / 1024.0 / 1024.0:F2} MB)");

modell.Dispose();
try { File.Delete(ausgabePfad); } catch { /* Aufräumen, nicht Teil der Messung */ }

Console.WriteLine();
Console.WriteLine("=== Ende Lasttest ===");
Console.WriteLine($"Finaler Prozess-Spitzenwert (Peak Working Set): {Process.GetCurrentProcess().PeakWorkingSet64 / 1024.0 / 1024.0:F1} MB");

return 0;

// ----------------------------------------------------------------------
// Identisch zu spike/Program.cs: misst Laufzeit und Speicher-Spitzenwert
// (Prozess-Working-Set, nicht nur GC-Delta) davor/danach.
// ----------------------------------------------------------------------
static T? Messen<T>(string bezeichnung, Func<T> aktion)
{
    var prozess = Process.GetCurrentProcess();
    long speicherVorher = prozess.WorkingSet64;

    var uhr = Stopwatch.StartNew();
    T? ergebnis;
    try
    {
        ergebnis = aktion();
    }
    catch (Exception ex)
    {
        uhr.Stop();
        Console.WriteLine($"[{bezeichnung}] AUSNAHME nach {uhr.ElapsedMilliseconds} ms: {ex.GetType().Name}: {ex.Message}");
        return default;
    }
    uhr.Stop();

    prozess.Refresh();
    long speicherNachher = prozess.WorkingSet64;
    long spitze = prozess.PeakWorkingSet64;

    Console.WriteLine($"[{bezeichnung}]");
    Console.WriteLine($"  Laufzeit           : {uhr.ElapsedMilliseconds} ms");
    Console.WriteLine($"  Working-Set vorher : {speicherVorher / 1024.0 / 1024.0:F1} MB");
    Console.WriteLine($"  Working-Set nachher: {speicherNachher / 1024.0 / 1024.0:F1} MB");
    Console.WriteLine($"  Delta              : {(speicherNachher - speicherVorher) / 1024.0 / 1024.0:F1} MB");
    Console.WriteLine($"  Prozess-Spitzenwert: {spitze / 1024.0 / 1024.0:F1} MB");

    return ergebnis;
}
