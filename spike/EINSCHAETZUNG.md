# xBIM-Spike (Phase 1) — Schriftliche Einschätzung

Stand: Alle 14 Teilschritte der Aufgabenstellung durchgespielt, siehe
`Program.cs` und die Commit-Historie (ein Commit pro Schritt, mit den
jeweiligen Funden in der Commit-Message).

## 1. Welche Operationen liefen wie?

| # | Operation | Ergebnis |
|---|---|---|
| 1 | `Open` + Schema | Problemlos. `IfcStore.Open()` erkennt IFC2X3/IFC4 selbst. |
| 2 | `GetElementsByType` inkl. Hierarchie | Problemlos. `Instances.OfType(string, bool)` löst die Klassenhierarchie über die EXPRESS-Metadaten selbst auf. |
| 3 | `GetElementByGuid` | Funktioniert, aber **kein indexierter Zugriff** wie ifcopenshells `by_guid()` — nur linearer Scan. Für häufige Einzel-Lookups sollte die Implementierung selbst einen `Dictionary<string, IIfcElement>` cachen. |
| 4 | `Id`/`IfcClass`/`GlobalId`/`Name`/`ObjectType` | Problemlos. Falle: `Name`/`ObjectType` sind `Nullable<T>`-Werttypen — bei String-Interpolation eines nicht gesetzten Werts kommt `""` statt erkennbar "nicht gesetzt" heraus; `.HasValue` explizit prüfen. |
| 5 | `PropertySets` je Element (inkl. Mengensätzen) | Funktioniert, aber kein Einzeiler: Weg über `IsDefinedBy` → `RelatingPropertyDefinition` (Pattern-Match auf `IIfcPropertySet`/`IIfcElementQuantity`). 24 P-Sets über 6 Elemente in 21 ms. |
| 6 | `GetAllPropertySets()` | Problemlos, 0 ms. `Instances.OfType<IIfcPropertySet>()` direkt. |
| 7 | `GetOwnerInfo()` | Problemlos, alle Felder direkt typisiert erreichbar. |
| 8 | `GetProjectUnits()` | Problemlos — **positiver Fund**: `IIfcNamedUnit.Symbol` ist eine von xBIM selbst berechnete Eigenschaft und löst SI-Präfixe (z. B. KILO+GRAM → "kg") bereits auf. |
| 9 | `GetQuantityUnitSymbol` | Funktioniert, aber mit zwei echten Stolperstellen: (a) Schreibzugriff verlangt schema-spezifische Klassen, Lesen dagegen ist schemaunabhängig über die gemeinsamen Interfaces — Bruch in der Abstraktion. (b) `IfcConversionBasedUnit.Symbol` wirft ohne gesetztes `Dimensions`-Attribut eine rohe `NullReferenceException`, obwohl das Attribut laut Schema optional ist. |
| 10 | `SetProperty` | Problemlos. `NominalValue` per Neuzuweisung ändern, in einer Transaktion. |
| 11 | `AddPropertySet` | Mehrschrittig (PSet + `IfcRelDefinesByProperties` separat anlegen und verknüpfen), aber deutlich weniger schmerzhaft als erwartet: `GlobalId` und `OwnerHistory` werden von xBIM automatisch gesetzt. |
| 12 | `RemoveProperty`/`RemovePropertySet` | `RemoveProperty` einfach (`model.Delete()` räumt Rückverweise automatisch auf). `RemovePropertySet` ist die **zweite echte Stolperstelle**: Wird nur das PSet gelöscht, bleibt eine jetzt schema-ungültige `IfcRelDefinesByProperties`-Relationship zurück (Pflichtattribut `$`) — die Implementierung muss die Relationship explizit mitlöschen. |
| 13 | `CreateLabelValue` + Typ-Validierung | **Die wichtigste Frage des Spikes, klar beantwortet.** `NominalValue` ist ein EXPRESS-SELECT-Typ — der Wert lässt sich ohne Ausnahme und ohne Neuanlage der Entity auf einen neuen `IfcLabel` umstellen. xBIM verhindert weder das "Kaputtschreiben" (Text in denselben numerischen Typ zwingen → rohe `FormatException`) noch erzwingt es die saubere Lösung — die Sorgfalt bleibt Aufgabe der Fachlogik, genau wie in der Python-Fassung. |
| 14 | `Save` | Problemlos. `SaveAs(pfad, StorageType.Ifc)`, 130 ms für die Beispieldatei. |

**Fazit Operationen:** Kein einziger Schritt ist grundsätzlich gescheitert.
Zwei echte Stolperstellen (Schritt 9's Schema-Bruch beim Schreiben,
Schritt 12's Relationship-Leiche bei `RemovePropertySet`) und eine
Bibliotheks-Unsauberkeit (Schritt 9's `NullReferenceException` in
`IfcConversionBasedUnit.Symbol`) — alle sind bekannt, verstanden und in
einer produktiven Implementierung mit überschaubarem Aufwand zu
berücksichtigen.

## 2. Messwerte (Beispieldatei, ~580 KB, IFC2X3, 6 Elemente)

| Schritt | Laufzeit | Speicher-Delta |
|---|---|---|
| 1. Open | 540–1200 ms (variiert, JIT-Warmup) | ~57–63 MB |
| 2. GetElementsByType | 0–2 ms | ~0 MB |
| 3. GetElementByGuid | 0–1 ms | ~0 MB |
| 5. PropertySets je Element (6 Elemente, 24 PSets) | 21 ms | ~0,4 MB |
| 6. GetAllPropertySets (26 PSets, modellweit) | 0 ms | ~0 MB |
| 9. GetQuantityUnitSymbol | 4–70 ms (inkl. Transaktions-Overhead für Testdaten) | bis 4,6 MB |
| 10–13. Schreiboperationen einzeln | 0–15 ms je Operation | ~0–0,8 MB |
| 14. Save | 130 ms | — |

Die für die Anwendung wichtigste Operation (Schritt 5/6, alle P-Sets
auslesen) ist mit 0–21 ms bei dieser Dateigröße nicht der Flaschenhals.
Der dominante Kostenfaktor ist das einmalige `Open()` (Parsen der
gesamten STEP-Datei inkl. Geometrie-Rohdaten wie 3456 `IfcFace`), nicht
der eigentliche Attributzugriff. Bei deutlich größeren Modellen (mehrere
zehntausend Elemente) sollte vor einer endgültigen Entscheidung ein
zweiter Lasttest mit einer realistisch großen Projektdatei erfolgen —
die aktuelle Beispieldatei ist mit 6 Elementen dafür zu klein.

## 3. Version und Lizenz

- **Xbim.Essentials, aktuelle Version: 6.1.605** (NuGet, veröffentlicht
  15.07.2026) — das ist auch die Version, die dieser Spike verwendet.
  Quelle: [nuget.org/packages/Xbim.Essentials](https://www.nuget.org/packages/Xbim.Essentials)
- **Lizenz: CDDL-1.0** (Common Development and Distribution License),
  für das gesamte `XbimEssentials`-Repository einheitlich (`Xbim.Common`,
  `Xbim.Ifc`, `Xbim.Ifc2x3`, `Xbim.Ifc4`, `Xbim.Ifc4x3`, `Xbim.IO.Esent`,
  `Xbim.IO.MemoryModel` — alle Pakete, die dieser Spike installiert hat).
  CDDL erlaubt ausdrücklich die Einbindung in ein "Larger Work" und damit
  kommerzielle, closed-source Nutzung, solange die CDDL-Bedingungen für
  den *unveränderten* xBIM-Code selbst eingehalten werden (Copyleft nur
  auf Dateiebene, nicht auf das gesamte Produkt). Quelle:
  [XbimEssentials/LICENCE.md](https://github.com/xBimTeam/XbimEssentials/blob/master/LICENCE.md)
- **Teilpakete mit abweichender Lizenz:** Innerhalb der für diesen Spike
  genutzten Pakete keine — alle stammen aus demselben Repository unter
  derselben CDDL-1.0. Die transitive Abhängigkeit
  `Microsoft.Database.ManagedEsent` (für `Xbim.IO.Esent`) ist **MIT**-lizenziert,
  ebenso die `Microsoft.Extensions.*`-Pakete — unproblematisch.
  **Nicht genutzt, aber relevant, falls später Geometrie dazukommt:**
  `Xbim.Geometry` ist ebenfalls CDDL-1.0, bindet aber die C++-Bibliothek
  Open Cascade Technology (OCCT) ein, die unter einer eigenen,
  LGPL-artigen Lizenz mit statischer-Link-Ausnahme steht — für diesen
  rein attributbasierten Anwendungsfall irrelevant, da explizit keine
  Geometrie-Pakete installiert wurden.

## 4. Empfehlung: tragfähig oder nicht?

**Tragfähig — Empfehlung: xBIM Essentials weiterverwenden.**

Begründung: Alle 13 (bzw. 14 inkl. Save) benötigten Operationen sind mit
xBIM lösbar, in überschaubarem Umfang und ohne Geometrie-Overhead
(genau die für diesen Anwendungsfall relevanten Pakete werden geladen,
`Xbim.Geometry` bleibt außen vor). Die Lizenz (CDDL-1.0) ist für
kommerzielle/closed-source Nutzung unproblematisch. Die gefundenen
Stolperstellen sind lokal begrenzt, gut verstanden und einmalig in der
`IIfcModel`-Implementierung zu lösen — sie multiplizieren sich nicht mit
jeder neuen Fachlogik-Anforderung.

**Wäre GeometryGymIFC die bessere Wahl? Nein, nicht für diesen
Anwendungsfall.** GeometryGymIFC (MIT-lizenziert, aktiv gepflegt, letztes
NuGet-Release Juli 2025) ist eine reelle Alternative, aber:
- Der Schwerpunkt liegt klar auf Geometrie-Interop (Rhino/Grasshopper,
  Revit, ETABS/SAP2000-Anbindungen) — für einen reinen
  Attribut-Prüf-Anwendungsfall ist das nicht der Kernnutzen der
  Bibliothek.
- Keine vergleichbar ausgereifte, stark typisierte Schema-Abdeckung wie
  xBIMs generierte `IIfcXxx`-Interfaces pro EXPRESS-Entity — mehr
  Handarbeit bei der Navigation durch P-Sets und Mengensätze zu
  erwarten (nicht im Spike verifiziert, da nicht Gegenstand der
  Aufgabenstellung).
- Deutlich kleinere Community und Dokumentationsbasis als xBIM
  (kein `docs.xbim.net`-Äquivalent, keine buildingSMART-Anbindung).
- Der einzige klare Vorteil (MIT statt CDDL) löst kein tatsächliches
  Problem hier — CDDL blockiert die kommerzielle Nutzung nicht.

Ein Wechsel wäre nicht durch die Spike-Ergebnisse gerechtfertigt.

## 5. Risiken der empfohlenen Variante (xBIM), ausdrücklich benannt

1. **Schema-Bruch beim Schreiben.** Lesecode ist über die gemeinsamen
   `Xbim.Ifc4.Interfaces` schemaunabhängig, Schreibcode (`Instances.New<T>()`)
   verlangt zwingend die zum jeweiligen Modell-Schema passende konkrete
   Klasse (`Xbim.Ifc2x3.*` vs. `Xbim.Ifc4.*`). Jede Stelle der
   `IIfcModel`-Implementierung, die neue Entities anlegt (`AddPropertySet`
   vor allem), braucht eine sauber gekapselte Fallunterscheidung nach
   `modell.SchemaVersion` — sonst Laufzeitfehler ("This factory only
   creates types from its assembly").
2. **Kein referenzintegritäts-sicheres Löschen.** `model.Delete()` räumt
   Rückverweise *innerhalb bestehender* Entities auf, löscht aber keine
   Entities, die durch die Löschung bedeutungslos werden (siehe
   `RemovePropertySet`-Fund). Jede Lösch-Operation in der Implementierung
   muss die Relationship-Struktur des jeweiligen EXPRESS-Typs explizit
   kennen — ein generisches "lösch einfach" reicht nicht.
3. **Einzelne Bibliotheksfunktionen sind nicht robust gegen fehlende
   optionale Attribute.** `IfcConversionBasedUnit.Symbol` wirft eine
   rohe `NullReferenceException` statt eines Fallbacks, wenn `Dimensions`
   fehlt — obwohl das Attribut laut IFC-Schema optional ist. Es ist nicht
   auszuschließen, dass andere, im Spike nicht getestete
   Eigenschaften ähnliche stille Annahmen treffen. Produktionscode sollte
   Bibliotheksaufrufe an Attributen mit vielen optionalen Vorgänger-Werten
   defensiv behandeln (try/catch oder Vorab-Prüfung), nicht blind
   vertrauen.
4. **Kein indexierter GUID-Zugriff.** `GetElementByGuid` ist ohne
   Zusatzaufwand ein linearer Scan. Bei großen Modellen mit vielen
   Einzel-Lookups (z. B. Revit-Round-Trip-Matching über tausende
   Elemente) muss die Implementierung selbst cachen.
5. **Ungetestet in diesem Spike: Verhalten bei wirklich großen Modellen.**
   Die Beispieldatei hat nur 6 Elemente. Speicher- und Laufzeitverhalten
   bei Modellen mit zehntausenden Elementen und P-Sets (der eigentliche
   Praxisfall der Anwendung) ist auf Basis dieses Spikes **nicht**
   verlässlich abschätzbar — nur, dass die API-Form funktioniert.
6. **Lizenz-Copyleft auf Dateiebene.** CDDL verlangt, dass Änderungen an
   den xBIM-Quelldateien selbst offengelegt werden, falls die geänderte
   Datei weitergegeben wird. Solange xBIM nur als unveränderte
   NuGet-Abhängigkeit eingebunden wird (der übliche Fall), ist das
   irrelevant — nur bei einem eigenen Fork mit Patches wäre das zu
   beachten.

## 6. Validierung der Ausgabedatei

`spike/data/ausgabe.ifc` wurde nicht nur mit xBIM selbst zurückgelesen
(Schritt 14b, beweist nichts über Fremdkompatibilität), sondern zusätzlich
mit **ifcopenshell** (komplett unabhängige C++/Python-Implementierung)
geöffnet und mit dessen EXPRESS-Regel-Validator geprüft:

- Datei öffnet und liest sich korrekt, alle vorgenommenen Änderungen
  (`Bf_Nr` → `IfcLabel('aktiv')`, `PVI_SCHICHTSTAERKE` → `IfcReal(3.5)`,
  `QS_Pruefung` korrekt entfernt) sind identisch nachweisbar.
- **Ein Schema-Validierungsfehler**, aber nicht in den eigenen
  Änderungen: `IfcApplication` (von xBIM automatisch für die
  Bearbeitungssitzung angelegt, siehe Schritt 11) hat
  `ApplicationIdentifier = $`, obwohl das Attribut laut Schema
  verpflichtend ist. Für eine produktive Implementierung heißt das:
  Beim Schreibzugriff selbst eine vollständige `IfcApplication` mit allen
  Pflichtfeldern setzen, statt sich auf xBIMs automatisch erzeugte
  Platzhalter zu verlassen.

**Zusätzlicher Hinweis für eigene Prüfung:** Der offizielle
buildingSMART-Online-Validator
([validate.buildingsmart.org](https://validate.buildingsmart.org/)) prüft
zusätzlich gegen die volle bSI-Spezifikation (inkl. Model View Definition,
falls angegeben) — das geht über die reine EXPRESS-Schema-Konformität von
ifcopenshell hinaus. Da das Hochladen der Datei an einen Drittanbieter-Dienst
geht, wurde das hier bewusst nicht automatisch gemacht — bei Bedarf einfach
`spike/data/ausgabe.ifc` dort hochladen.
