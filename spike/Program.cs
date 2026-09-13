using System.Diagnostics;
using Xbim.Ifc;

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
