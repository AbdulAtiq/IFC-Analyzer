using System.Diagnostics;
using Xbim.Common;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

// ============================================================================
// xBIM-Spike (Phase 1) — Wegwerfcode zur Bibliotheksauswahl.
// KEINE Abstraktion, KEIN Fehlerbehandlungs-Feinschliff. Jeder Schritt wird
// einzeln durchgespielt und misst Laufzeit + Speicher-Spitzenwert.
// ============================================================================

var ifcPfad = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "data", "beispiel.ifc");
ifcPfad = Path.GetFullPath(ifcPfad);

Console.WriteLine("=== xBIM-Spike: Schritt 1 — Open(pfad) + Schema ===");
Console.WriteLine($"Datei: {ifcPfad}");
Console.WriteLine();

IfcStore? modell = Messen("1. Open(pfad)", () => IfcStore.Open(ifcPfad));

if (modell is null)
{
    Console.WriteLine("FEHLSCHLAG: Modell konnte nicht geöffnet werden.");
    return;
}

Console.WriteLine($"Erfolg: Datei geöffnet.");
Console.WriteLine($"  Schema (SchemaVersion) : {modell.SchemaVersion}");

Console.WriteLine();
Console.WriteLine("=== Schritt 2 — GetElementsByType(\"IfcProduct\") inkl. Klassenhierarchie ===");

// xBIM bietet für "nach Klasse abfragen, inkl. Unterklassen" die
// IEntityCollection.OfType(string, bool)-Methode: Der String ist der
// EXPRESS-Typname (Groß-/Kleinschreibung egal), der bool-Parameter
// steuert, ob referenzierte, aber noch nicht geladene Entities beim
// Zugriff aktiviert werden (bei einem komplett im Speicher gehaltenen
// Modell ohne Bedeutung, wird trotzdem als "true" mitgegeben).
// Die Unterklassen-Auflösung übernimmt xBIM selbst über die
// EXPRESS-Metadaten des jeweiligen Schemas (IFC2X3 oder IFC4).
foreach (var klasse in new[] { "IfcProduct", "IfcObject", "IfcBuildingElement", "IfcWall" })
{
    var elemente = Messen($"2. GetElementsByType(\"{klasse}\")",
        () => modell.Instances.OfType(klasse, activate: true).ToList());
    Console.WriteLine($"  Treffer: {elemente?.Count ?? 0}");
}

Console.WriteLine();
Console.WriteLine("=== Schritt 3 — GetElementByGuid(globalId) ===");

// WICHTIG (Erkenntnis aus der Reflexion vorab): xBIMs Ifc2x3-Klassen
// implementieren dieselben Interfaces wie Ifc4 (Namespace
// "Xbim.Ifc4.Interfaces"). Das ist der Mechanismus, der Schema-Unabhängigkeit
// im Code ermöglicht: IIfcRoot, IIfcProduct, IIfcElement usw. gelten für
// BEIDE Schemas, ohne Fallunterscheidung. GlobalId ist vom Typ
// IfcGloballyUniqueId (kein reiner string) — Vergleich über ToString().
const string vorhandeneGuid = "2bkq4to1n7zOPW8LrQtFDY";
const string unbekannteGuid = "DIESE_GUID_GIBT_ES_NICHT";

var gefunden = Messen("3. GetElementByGuid (vorhanden)",
    () => modell.Instances.OfType<IIfcRoot>().FirstOrDefault(r => r.GlobalId.ToString() == vorhandeneGuid));
Console.WriteLine(gefunden is not null
    ? $"  Gefunden: #{gefunden.EntityLabel} {gefunden.GetType().Name}"
    : "  NICHT gefunden (unerwartet!)");

var nichtGefunden = Messen("3. GetElementByGuid (unbekannt)",
    () => modell.Instances.OfType<IIfcRoot>().FirstOrDefault(r => r.GlobalId.ToString() == unbekannteGuid));
Console.WriteLine(nichtGefunden is null
    ? "  Erwartungsgemäß null (keine Ausnahme) — entspricht der Anforderung an GetElementByGuid."
    : "  UNERWARTET: Element gefunden.");

Console.WriteLine();
Console.WriteLine("=== Schritt 4 — Je Element: Id, IfcClass, GlobalId, Name, ObjectType ===");

// IIfcElement (aus Xbim.Ifc4.Interfaces) bringt alle fünf Werte mit.
// Id kommt nicht aus IIfcElement selbst, sondern aus IPersistEntity.EntityLabel
// (jede Entity in jedem xBIM-Modell hat das). ObjectType sitzt auf IIfcObject,
// von dem IIfcElement erbt.
var elementeSchritt4 = Messen("4. Elemente einlesen (IfcBuildingElement)",
    () => modell.Instances.OfType<IIfcBuildingElement>().ToList());

foreach (var element in elementeSchritt4 ?? Enumerable.Empty<IIfcBuildingElement>())
{
    // Name/ObjectType sind IfcLabel? (Nullable<T> um einen IFC-Werttyp).
    // Bei fehlendem Wert liefert die String-Interpolation stillschweigend
    // "" statt "null" — deshalb hier explizit HasValue geprüft, damit
    // "leerer Text" nicht mit "gar nicht gesetzt" verwechselt wird.
    string objektTyp = element.ObjectType.HasValue ? $"\"{element.ObjectType}\"" : "(nicht gesetzt)";
    Console.WriteLine($"  Id={element.EntityLabel,-6} IfcClass={element.GetType().Name,-28} " +
                       $"GlobalId={element.GlobalId,-24} Name=\"{element.Name}\" ObjectType={objektTyp}");
}

Console.WriteLine();
Console.WriteLine("=== Schritt 5 — PropertySets je Element (inkl. Mengensätzen) ===");

// Kein fertiges "element.PropertySets" in xBIM — der Weg führt über die
// Beziehung IsDefinedBy (IfcRelDefinesByProperties). Deren
// RelatingPropertyDefinition ist entweder ein IIfcPropertySet (Attribute)
// oder ein IIfcElementQuantity (Mengensatz) — beide erben von
// IIfcPropertySetDefinition. Die Beispieldatei enthält KEINE
// IfcElementQuantity (0 Mengensätze im Modell, siehe Voranalyse) — der
// Mengensatz-Zweig ist deshalb nur "kalt" mitgetestet, siehe Hinweis unten.
Messen("5. PropertySets aller 6 Elemente lesen", () =>
{
    int anzahlPsets = 0, anzahlQsets = 0;
    foreach (var element in elementeSchritt4 ?? Enumerable.Empty<IIfcBuildingElement>())
    {
        Console.WriteLine($"  Element #{element.EntityLabel} ({element.GetType().Name}):");
        foreach (var rel in element.IsDefinedBy)
        {
            switch (rel.RelatingPropertyDefinition)
            {
                case IIfcPropertySet pset:
                    anzahlPsets++;
                    Console.WriteLine($"    PSet \"{pset.Name}\" (#{pset.EntityLabel}):");
                    foreach (var prop in pset.HasProperties)
                    {
                        if (prop is IIfcPropertySingleValue wert)
                        {
                            var nominal = wert.NominalValue;
                            string typName = nominal?.GetType().Name ?? "(kein Wert)";
                            Console.WriteLine($"      {prop.Name} = {nominal?.Value} [{typName}]");
                        }
                        else
                        {
                            Console.WriteLine($"      {prop.Name} = (Property-Typ {prop.GetType().Name} nicht ausgewertet — kein IfcPropertySingleValue)");
                        }
                    }
                    break;

                case IIfcElementQuantity qset:
                    anzahlQsets++;
                    Console.WriteLine($"    QSet (Mengensatz) \"{qset.Name}\" (#{qset.EntityLabel}):");
                    foreach (var menge in qset.Quantities)
                        Console.WriteLine($"      {menge.Name} [{menge.GetType().Name}]");
                    break;

                default:
                    Console.WriteLine($"    Unbekannter RelatingPropertyDefinition-Typ: {rel.RelatingPropertyDefinition?.GetType().Name}");
                    break;
            }
        }
    }
    Console.WriteLine($"  Insgesamt: {anzahlPsets} PropertySets, {anzahlQsets} Mengensätze (Quantity-Sets).");
    return anzahlPsets;
});

if (elementeSchritt4 is not null && elementeSchritt4.Count == 0)
{
    Console.WriteLine("  Hinweis: keine Elemente vorhanden — Schritt übersprungen.");
}
Console.WriteLine();
Console.WriteLine("  HINWEIS zu Mengensätzen: Die Beispieldatei enthält keine einzige");
Console.WriteLine("  IfcElementQuantity. Der QSet-Zweig oben ist damit real durchlaufen,");
Console.WriteLine("  aber nie mit echten Daten ausgeführt worden. Wird bei Schritt 9");
Console.WriteLine("  (Einheitensymbol von Mengen) erneut relevant — dort baue ich, wie");
Console.WriteLine("  angekündigt, eine IfcElementQuantity per xBIM-API synthetisch nach,");
Console.WriteLine("  um den Lesepfad trotzdem an echtem xBIM-Verhalten zu zeigen.");

modell.Dispose();

// ----------------------------------------------------------------------
// Hilfsfunktion: führt einen Schritt aus, misst Laufzeit und
// Speicher-Spitzenwert (Prozess-Working-Set) davor/danach.
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
