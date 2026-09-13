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

Console.WriteLine();
Console.WriteLine("=== Schritt 6 — GetAllPropertySets(): alle P-Sets ohne Umweg über Elemente ===");

// Direkter Weg, ohne über IsDefinedBy und die Elemente zu gehen: xBIM
// führt für jeden EXPRESS-Typ intern eine Typtabelle, genau wie in
// Schritt 2 genutzt — nur diesmal auf IIfcPropertySet direkt.
var allePropertySets = Messen("6. GetAllPropertySets()",
    () => modell.Instances.OfType<IIfcPropertySet>().ToList());

Console.WriteLine($"  Treffer: {allePropertySets?.Count ?? 0} IfcPropertySet-Entities im gesamten Modell.");
Console.WriteLine($"  Zum Vergleich Schritt 5 (nur über die 6 Elemente): 24 PropertySets.");
Console.WriteLine($"  Differenz ({(allePropertySets?.Count ?? 0) - 24}) = P-Sets, die NICHT an einem der");
Console.WriteLine($"  6 IfcBuildingElement hängen, sondern z. B. am IfcProject");
Console.WriteLine($"  (ePSet_ProjectedCRS, ePSet_MapConversion) — genau der Fall, für den");
Console.WriteLine($"  GetAllPropertySets() laut Abstraktion existiert.");

Console.WriteLine();
Console.WriteLine("=== Schritt 7 — GetOwnerInfo() aus IfcOwnerHistory ===");

// Die Abstraktion verlangt "Unbekannt" statt einer Ausnahme, wenn Angaben
// fehlen (abhängig vom Exporter). Hier probeweise robust gegen fehlende
// Person/Organisation/Application geschrieben.
Messen("7. GetOwnerInfo()", () =>
{
    var ownerHistory = modell.Instances.OfType<IIfcOwnerHistory>().FirstOrDefault();
    if (ownerHistory is null)
    {
        Console.WriteLine("  Keine IfcOwnerHistory im Modell -> Unbekannt/Unbekannt.");
        return ("Unbekannt", "Unbekannt");
    }

    var person = ownerHistory.OwningUser?.ThePerson;
    var organisation = ownerHistory.OwningUser?.TheOrganization;
    string ersteller = person is not null && (person.GivenName.HasValue || person.FamilyName.HasValue)
        ? $"{person.GivenName} {person.FamilyName}".Trim()
        : organisation?.Name.ToString() ?? "Unbekannt";

    var anwendung = ownerHistory.OwningApplication;
    string software = anwendung is not null
        ? $"{anwendung.ApplicationFullName} {anwendung.Version} ({anwendung.ApplicationDeveloper?.Name})"
        : "Unbekannt";

    Console.WriteLine($"  Ersteller           : {ersteller}");
    Console.WriteLine($"  Verwendete Software : {software}");
    return (ersteller, software);
});

Console.WriteLine();
Console.WriteLine("=== Schritt 8 — GetProjectUnits() aus IfcUnitAssignment ===");

// Überraschung (positiv): xBIM löst Präfixe bereits selbst auf.
// IIfcNamedUnit.Symbol ist eine BERECHNETE Eigenschaft der Bibliothek
// selbst (kein STEP-Attribut!) und liefert z. B. "kg" für
// Prefix=KILO + Name=GRAM, oder "m" für Prefix=null + Name=METRE.
// Das ist exakt die Präfix-Auflösung, die für Schritt 9 gebraucht wird.
Messen("8. GetProjectUnits()", () =>
{
    var zuweisung = modell.Instances.OfType<IIfcUnitAssignment>().FirstOrDefault();
    var ergebnis = new Dictionary<string, string>();
    if (zuweisung is null)
    {
        Console.WriteLine("  Keine IfcUnitAssignment im Modell.");
        return ergebnis;
    }

    foreach (var einheit in zuweisung.Units)
    {
        string schluessel;
        string symbol;
        switch (einheit)
        {
            case IIfcNamedUnit benannt:
                schluessel = benannt.UnitType.ToString();
                symbol = benannt.Symbol;
                break;
            case IIfcMonetaryUnit geld:
                schluessel = "MONETARYUNIT";
                symbol = geld.Currency.ToString();
                break;
            default:
                schluessel = einheit.GetType().Name;
                symbol = einheit.FullName;
                break;
        }
        ergebnis[schluessel] = symbol;
        Console.WriteLine($"  {schluessel,-16} -> \"{symbol}\"  ({einheit.GetType().Name})");
    }
    return ergebnis;
});

Console.WriteLine();
Console.WriteLine("=== Schritt 9 — GetQuantityUnitSymbol(element, pset, attribut) ===");
Console.WriteLine("Die Beispieldatei hat weder IfcElementQuantity noch IfcConversionBasedUnit.");
Console.WriteLine("Wie angekündigt: Lesepfad an echten, aber SELBST ANGELEGTEN Testdaten zeigen.");
Console.WriteLine();

// ------------------------------------------------------------------
// Testfall A: reale Datei (IFC2X3), Mengensatz per Transaktion angelegt.
//
// STOLPERSTELLE (echter Fund, nicht nur theoretisch): Ein erster Versuch
// mit den Xbim.Ifc4.*-Klassen ist zur Laufzeit mit
// "This factory only creates types from its assembly" gescheitert. Der
// Grund: Die gemeinsamen Interfaces (Xbim.Ifc4.Interfaces) sind NUR zum
// LESEN schemaübergreifend nutzbar. Zum SCHREIBEN (Instances.New<T>())
// verlangt jedes Modell zwingend die zu seinem EIGENEN Schema passende
// konkrete Klasse — ein IFC2X3-Modell akzeptiert nur Xbim.Ifc2x3.*-Typen,
// nicht Xbim.Ifc4.*. Für AddPropertySet (Schritt 11) heißt das: Die
// Implementierung braucht zwingend eine Fallunterscheidung nach
// modell.SchemaVersion, wenn sie neue Entities anlegt.
//
// WICHTIG (fachlich): IfcQuantityLength/-Area/-Volume haben in IFC2X3
// GAR KEIN eigenes "Unit"-Attribut (das kam erst mit IFC4) — die Einheit
// einer Menge ergibt sich in IFC2X3 IMMER aus der projektweiten
// IfcUnitAssignment. Das gemeinsame Cross-Schema-Interface
// IIfcQuantityLength (Xbim.Ifc4.Interfaces) spiegelt das exakt: Es hat
// KEIN Unit-Property (siehe Reflexion vorab) — nur die konkrete
// Ifc4-Klasse hat es zusätzlich.
// ------------------------------------------------------------------
var element60 = modell.Instances.OfType<IIfcBuildingElement>().First(e => e.EntityLabel == 60);
using (var transaktion = modell.BeginTransaction("Testdaten für Schritt 9"))
{
    var mengensatz = modell.Instances.New<Xbim.Ifc2x3.ProductExtension.IfcElementQuantity>(q =>
    {
        q.Name = "Testmengen_Schritt9";
        q.Quantities.Add(modell.Instances.New<Xbim.Ifc2x3.QuantityResource.IfcQuantityLength>(
            l => { l.Name = "Laenge"; l.LengthValue = 5.0; }));
        q.Quantities.Add(modell.Instances.New<Xbim.Ifc2x3.QuantityResource.IfcQuantityArea>(
            a => { a.Name = "Flaeche"; a.AreaValue = 12.5; }));
    });
    modell.Instances.New<Xbim.Ifc2x3.Kernel.IfcRelDefinesByProperties>(rel =>
    {
        rel.RelatingPropertyDefinition = mengensatz;
        rel.RelatedObjects.Add((Xbim.Ifc2x3.Kernel.IfcObject)element60);
    });
    transaktion.Commit();
}

var projektEinheiten = modell.Instances.OfType<IIfcUnitAssignment>().First();

Messen("9a. GetQuantityUnitSymbol (IFC2X3, ohne eigene Unit -> Projekt-Einheit)", () =>
{
    var mengensatz = element60.IsDefinedBy
        .Select(r => r.RelatingPropertyDefinition).OfType<IIfcElementQuantity>()
        .First(q => q.Name == "Testmengen_Schritt9");
    foreach (var menge in mengensatz.Quantities)
    {
        string? symbol = ErmittleEinheitenSymbol(menge, projektEinheiten);
        Console.WriteLine($"  {menge.Name} [{menge.GetType().Name}] -> Einheitensymbol: {symbol ?? "(keins ermittelbar)"}");
    }
    return true;
});

// ------------------------------------------------------------------
// Testfall B: separates Wegwerf-IFC4-Modell (nicht aus Datei, nur im
// Speicher), um die IFC4-spezifischen Fälle zu erzwingen, die die
// Beispieldatei nicht hergibt: eigene Unit je Mengen-Angabe, SI-Präfixe
// Milli/Centi/Deci/Kilo, und eine echte IfcConversionBasedUnit.
// ------------------------------------------------------------------
using var testModell = IfcStore.Create(Xbim.Common.Step21.XbimSchemaVersion.Ifc4, Xbim.IO.XbimStoreType.InMemoryModel);
using (var transaktion = testModell.BeginTransaction("IFC4-Testdaten Schritt 9"))
{
    Xbim.Ifc4.MeasureResource.IfcSIUnit SiEinheit(IfcUnitEnum typ, IfcSIPrefix? prefix, IfcSIUnitName name) =>
        testModell.Instances.New<Xbim.Ifc4.MeasureResource.IfcSIUnit>(u =>
        { u.UnitType = typ; u.Prefix = prefix; u.Name = name; });

    var milli = testModell.Instances.New<Xbim.Ifc4.QuantityResource.IfcQuantityLength>(l =>
    { l.Name = "Laenge_mm"; l.LengthValue = 500; l.Unit = SiEinheit(IfcUnitEnum.LENGTHUNIT, IfcSIPrefix.MILLI, IfcSIUnitName.METRE); });
    var centi = testModell.Instances.New<Xbim.Ifc4.QuantityResource.IfcQuantityLength>(l =>
    { l.Name = "Laenge_cm"; l.LengthValue = 50; l.Unit = SiEinheit(IfcUnitEnum.LENGTHUNIT, IfcSIPrefix.CENTI, IfcSIUnitName.METRE); });
    var dezi = testModell.Instances.New<Xbim.Ifc4.QuantityResource.IfcQuantityLength>(l =>
    { l.Name = "Laenge_dm"; l.LengthValue = 5; l.Unit = SiEinheit(IfcUnitEnum.LENGTHUNIT, IfcSIPrefix.DECI, IfcSIUnitName.METRE); });
    var kilo = testModell.Instances.New<Xbim.Ifc4.QuantityResource.IfcQuantityWeight>(g =>
    { g.Name = "Gewicht_kg"; g.WeightValue = 3; g.Unit = SiEinheit(IfcUnitEnum.MASSUNIT, IfcSIPrefix.KILO, IfcSIUnitName.GRAM); });

    // Echte IfcConversionBasedUnit: Zoll, definiert über Umrechnungsfaktor
    // auf die SI-Basiseinheit Meter.
    //
    // STOLPERSTELLE (echter Fund): Ohne "Dimensions" wirft xBIMs eigener
    // Symbol-Getter auf IfcConversionBasedUnit eine rohe
    // NullReferenceException statt einer sprechenden Fehlermeldung oder
    // eines Fallbacks — auch wenn Dimensions in der Datei tatsächlich
    // optional ist (IFC-Schema erlaubt $/fehlend). Erst mit gesetzten
    // Dimensions funktioniert Symbol zuverlässig.
    var dimensionenLaenge = testModell.Instances.New<Xbim.Ifc4.MeasureResource.IfcDimensionalExponents>(d =>
    {
        d.LengthExponent = 1; d.MassExponent = 0; d.TimeExponent = 0;
        d.ElectricCurrentExponent = 0; d.ThermodynamicTemperatureExponent = 0;
        d.AmountOfSubstanceExponent = 0; d.LuminousIntensityExponent = 0;
    });
    var meterAlsBasis = SiEinheit(IfcUnitEnum.LENGTHUNIT, null, IfcSIUnitName.METRE);
    var zollFaktor = testModell.Instances.New<Xbim.Ifc4.MeasureResource.IfcMeasureWithUnit>(f =>
    { f.ValueComponent = new Xbim.Ifc4.MeasureResource.IfcLengthMeasure(0.0254); f.UnitComponent = meterAlsBasis; });
    var zoll = testModell.Instances.New<Xbim.Ifc4.MeasureResource.IfcConversionBasedUnit>(u =>
    { u.UnitType = IfcUnitEnum.LENGTHUNIT; u.Name = "Zoll"; u.ConversionFactor = zollFaktor; u.Dimensions = dimensionenLaenge; });
    var laengeZoll = testModell.Instances.New<Xbim.Ifc4.QuantityResource.IfcQuantityLength>(l =>
    { l.Name = "Laenge_zoll"; l.LengthValue = 12; l.Unit = zoll; });

    transaktion.Commit();

    Messen("9b. GetQuantityUnitSymbol (IFC4, eigene Unit je Menge)", () =>
    {
        foreach (var menge in new IIfcPhysicalQuantity[] { milli, centi, dezi, kilo, laengeZoll })
        {
            string? symbol = ErmittleEinheitenSymbol(menge, projektEinheiten: null);
            Console.WriteLine($"  {menge.Name,-14} -> Einheitensymbol: {symbol ?? "(keins ermittelbar)"}");
        }
        return true;
    });
}

Console.WriteLine();
Console.WriteLine("=== Schritt 10 — SetProperty(pset, name, wert): Wert in bestehende Eigenschaft schreiben ===");

// Zielattribut: "PVI_SCHICHTSTAERKE" im PSet "ProVI" (#63) an Element #60 —
// laut Schritt 5 aktuell 0 [IfcReal]. Wir schreiben einen neuen
// IfcReal-Wert hinein (gleicher Typ bleibt gleich — die eigentliche
// Typ-Frage kommt erst in Schritt 13). NominalValue lässt sich nicht
// "in-place" ändern, sondern nur komplett neu zuweisen — ein neuer
// IfcReal-Werttyp (struct, kein Entity, daher kein Instances.New<T> nötig).
var psetProVI = element60.IsDefinedBy
    .Select(r => r.RelatingPropertyDefinition).OfType<IIfcPropertySet>()
    .First(p => p.Name == "ProVI");
var eigenschaft = (Xbim.Ifc2x3.PropertyResource.IfcPropertySingleValue)
    psetProVI.HasProperties.OfType<IIfcPropertySingleValue>().First(p => p.Name == "PVI_SCHICHTSTAERKE");

Console.WriteLine($"  Vorher: {eigenschaft.Name} = {eigenschaft.NominalValue?.Value} [{eigenschaft.NominalValue?.GetType().Name}]");

Messen("10. SetProperty (PVI_SCHICHTSTAERKE = 3.5)", () =>
{
    using var transaktion = modell.BeginTransaction("Schritt 10: SetProperty");
    eigenschaft.NominalValue = new Xbim.Ifc2x3.MeasureResource.IfcReal(3.5);
    transaktion.Commit();
    return true;
});

Console.WriteLine($"  Nachher: {eigenschaft.Name} = {eigenschaft.NominalValue?.Value} [{eigenschaft.NominalValue?.GetType().Name}]");

modell.Dispose();

// ----------------------------------------------------------------------
// Schritt-9-Kernlogik: löst das Einheitensymbol einer Mengen-Angabe auf.
// 1. Eigene Unit? Nur IFC4-Quantities haben dieses Attribut, das
//    gemeinsame Cross-Schema-Interface kennt es nicht -> "dynamic" als
//    bewusster Kompromiss, um ohne Schema-Fallunterscheidung sowohl
//    IFC2X3 (kein Unit-Attribut, RuntimeBinderException) als auch IFC4
//    (Unit vorhanden, ggf. null) zu bedienen.
// 2. Sonst: passende Einheit aus der Projekt-IfcUnitAssignment suchen,
//    gematcht über IfcUnitEnum (LENGTHUNIT, AREAUNIT, MASSUNIT, ...),
//    das aus dem konkreten Mengen-Typ abgeleitet wird.
// ----------------------------------------------------------------------
static string? ErmittleEinheitenSymbol(IIfcPhysicalQuantity menge, IIfcUnitAssignment? projektEinheiten)
{
    IIfcNamedUnit? eigeneUnit = null;
    try
    {
        dynamic dyn = menge;
        eigeneUnit = dyn.Unit as IIfcNamedUnit;
    }
    catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
    {
        // Schema hat kein Unit-Attribut auf dieser Quantity (IFC2X3) — erwartet.
    }

    if (eigeneUnit is not null)
        return eigeneUnit.Symbol;

    if (projektEinheiten is null)
        return null;

    IfcUnitEnum? gesuchterTyp = menge switch
    {
        IIfcQuantityLength => IfcUnitEnum.LENGTHUNIT,
        IIfcQuantityArea => IfcUnitEnum.AREAUNIT,
        IIfcQuantityVolume => IfcUnitEnum.VOLUMEUNIT,
        IIfcQuantityWeight => IfcUnitEnum.MASSUNIT,
        IIfcQuantityCount => null,
        IIfcQuantityTime => IfcUnitEnum.TIMEUNIT,
        _ => null
    };
    if (gesuchterTyp is null) return null;

    return projektEinheiten.Units.OfType<IIfcNamedUnit>()
        .FirstOrDefault(u => u.UnitType == gesuchterTyp)?.Symbol;
}

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
