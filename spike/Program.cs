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
