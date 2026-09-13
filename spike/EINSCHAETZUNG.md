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

**Nachtrag:** Dieser zweite Lasttest wurde durchgeführt, siehe Abschnitt 7.
Ergebnis in Kürze: Die Größenordnung des hier verfügbaren realen Modells
(730 Elemente statt 6) bestätigt den obigen Befund und liefert erstmals
echte Zahlen jenseits von "die API-Form funktioniert" — siehe dort für
die vollständige Einordnung und die verbleibenden Unsicherheiten bei noch
größeren Modellen.

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
5. **Verhalten bei größeren Modellen — jetzt mit echten Zahlen (siehe
   Abschnitt 7).** Ein Lasttest mit einer realen Projektdatei (IFC4X3,
   3,75 MB, 730 `IfcProduct`, 2386 `IfcPropertySet`, ~120× mehr Elemente
   als die 6-Elemente-Beispieldatei) zeigt: `Open()` bleibt bei ~1,2–1,4 s,
   Prozess-Spitzenwert bei ~136 MB, der komplette P-Set-Scan über alle
   Elemente bei unter einer Sekunde. Speicher und Laufzeit skalieren dabei
   auffällig **unterproportional** zur Elementzahl — der Großteil des
   Overheads ist fixer Startkosten (Assembly-Laden, Schema-Metadaten), nicht
   linear pro Element. Details, Hochrechnung auf ein hypothetisches
   300-MB-Modell und die verbleibende Unsicherheit (die getestete Datei ist
   immer noch ~80× kleiner als ein 300-MB-Modell und weit von
   "zehntausenden Elementen" entfernt) siehe Abschnitt 7. Das Risiko ist
   damit für die getestete Größenordnung entkräftet, für echte
   Großmodelle aber nur durch Hochrechnung, nicht durch Messung, geklärt.
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

## 7. Lasttest mit realer Projektdatei (Nachtrag)

Auftrag: Risiko Nr. 5 nachholen — Speicher- und Laufzeitverhalten bei einer
größeren, echten Projektdatei statt der 6-Elemente-Beispieldatei messen.
Verfügbar war eine reale Infrastruktur-Projektdatei (IFC4X3, personenbezogene
und firmenspezifische Angaben aus dem Header hier bewusst nicht
wiedergegeben); sie ist nicht Teil dieses Repositories, da es sich um echte,
nicht-anonymisierte Projektdaten Dritter handelt.

### Vorgehen

**Nicht** wortwörtlich `spike/Program.cs` unverändert durchlaufen lassen —
das war technisch gar nicht möglich, ohne die eigentliche Fragestellung
(Skalierung) zu verfehlen: Die Schritte 3, 4 und 7–13 dieses Programms
referenzieren feste, an die 6-Elemente-Beispieldatei gebundene Werte (eine
konkrete GlobalId, Entity-Label `60`, PSet-Namen wie `"ProVI"` und
`"Stammdaten Verkehrsanlage"`) und legen in Schritt 9 sogar hartkodiert
`Xbim.Ifc2x3.*`-Klassen an. Die neue Datei hat ein anderes Schema
(IFC4X3 statt IFC2X3) — ein `Instances.New<Xbim.Ifc2x3....>()` auf einem
IFC4X3-Modell bricht mit `"This factory only creates types from its
assembly"` ab (siehe Risiko Nr. 1), und zwar *vor* Schritt 14 (Save), dessen
Zahlen hier aber gerade gebraucht werden. Ein wörtlicher Lauf hätte also nicht
mehr Erkenntnis geliefert, sondern nur einen für die Fragestellung irrelevanten
Absturz vor der eigentlichen Messung.

Stattdessen: ein zweites, separates Konsolenprogramm
(`spike/Lasttest/Program.cs`), das **exakt dieselbe `Messen()`-Hilfsfunktion
und exakt dieselben xBIM-Aufrufe** wie `spike/Program.cs` für die laut
Aufgabenstellung relevanten Schritte 1, 2, 5, 6 und 14 verwendet — nur
parametrisiert auf einen beliebigen Dateipfad statt der hartkodierten
Beispieldatei, und in Schritt 2/5 verallgemeinert von `IfcBuildingElement`
(Hochbau-spezifisch) auf zusätzlich `IIfcElement` (schema- und
domänenunabhängig), damit "über alle Elemente" wörtlich stimmt. Kein neuer
Ansatz, keine Optimierung — dieselbe Technik, nur nicht an die
Beispieldatei gebundene Werte entfernt. Die Schreiboperationen 10–13 wurden
wie in der Aufgabenstellung vorgesehen nicht wiederholt.

Drei Läufe (`dotnet run -c Release`, kein Warmup zwischen den Läufen,
derselbe Prozess-Neustart wie im Ursprungsspike) auf einer Kopie der Datei
(nie auf dem Original gearbeitet).

### Eckdaten der Testdatei

| Kennzahl | Wert |
|---|---|
| Dateigröße | 3,75 MB (3.930.740 Bytes) |
| Schema | IFC4X3_ADD2 (von xBIM erkannt als `Ifc4x3`) |
| `IfcProduct` (inkl. Unterklassen) | 730 |
| `IfcElement` (Basis für Schritt 5) | 724 |
| `IfcPropertySet` | 2386 |
| `IfcElementQuantity` (Mengensätze) | 714 |
| Attribut-Werte über alle P-Sets (`HasProperties`) | 9086 |
| Verhältnis zur Beispieldatei | ~6,5× Dateigröße, ~120× Elementzahl |

### Messwerte (Mittel über 3 Läufe, Bereich in Klammern)

| Schritt | Laufzeit | Prozess-Spitzenwert (Working Set) |
|---|---|---|
| 1. Open | ~1241 ms (1199–1282 ms) | ~109 MB (107,3–112,5 MB) |
| 2. GetElementsByType("IfcProduct") | 2–3 ms | unverändert |
| 2. GetElementsByType("IfcObject") | 0 ms | unverändert |
| 2. GetElementsByType("IfcBuildingElement") | **Ausnahme, siehe Fund unten** | — |
| 2b. GetElementsByType&lt;IIfcBuildingElement&gt;() (generisch) | 0 ms, 724 Treffer | unverändert |
| 5. PropertySets aller 724 Elemente | ~579 ms (495–703 ms) | ~134 MB (133,1–133,9 MB) |
| 6. GetAllPropertySets() (2386 Treffer) | 0 ms | unverändert |
| 14. Save (ohne vorherige Änderung) | ~394 ms (308–452 ms) | ~135 MB (134,2–134,7 MB) |
| **Prozess-Gesamt-Spitzenwert** | — | **~136 MB (135,7–136,7 MB)** |

Schritt 5 pro 1.000 Elemente: 686–975 ms (Mittel ~802 ms) — die geforderte
Hochrechnungsgrundlage.

### Zusätzlicher Fund (nicht Teil der ursprünglichen 14 Schritte)

Der **String-basierte** Weg aus Schritt 2 —
`Instances.OfType("IfcBuildingElement", true)` — wirft auf dieser IFC4X3-Datei
eine `ArgumentException` ("StringType must be a name of the existing persist
entity type"). Grund: In IFC4X3 wurde die EXPRESS-Entity `IfcBuildingElement`
aus der Schema-Hierarchie entfernt (Elemente wie `IfcWall` erben jetzt direkt
von `IfcElement`) — sie taucht in der Metadaten-Typtabelle des Modells schlicht
nicht mehr auf. Der **generische, stark typisierte** Weg
(`Instances.OfType<IIfcBuildingElement>()`) funktioniert dagegen unverändert
(724 Treffer), weil xBIMs schemaübergreifendes Interface `IIfcBuildingElement`
unabhängig vom tatsächlichen EXPRESS-Namen im jeweiligen Schema besteht.
**Konsequenz für eine produktive Implementierung:** Eine `GetElementsByType`,
die Klassennamen als freien String entgegennimmt (statt generischer Typen),
ist nicht über alle IFC-Schemaversionen hinweg stabil für Oberklassen, die
zwischen Versionen umstrukturiert wurden — das betrifft nicht nur
`IfcBuildingElement`, sondern jede vergleichbare Schema-Änderung zwischen
IFC2X3/IFC4/IFC4X3. Muss defensiv behandelt werden (bekannte Aliasse/Fallback
auf die generische Schnittstelle), sonst bricht die Abfrage schemaabhängig.

### Vergleich klein → groß

| | Beispieldatei | Projektdatei (dieser Lasttest) | Faktor |
|---|---|---|---|
| Dateigröße | 0,58 MB | 3,75 MB | ~6,5× |
| Elemente (`IfcProduct`/`IfcBuildingElement`) | 6 | 730 / 724 | ~120× |
| Open(): Laufzeit | 540–1200 ms | 1199–1282 ms | **kaum verändert** |
| Open(): Speicher-Spitzenwert | ~57–63 MB (Delta) | ~75–80 MB (Delta) | **nur ~+30 %** |
| P-Set-Scan (Schritt 5/6) | 0–21 ms (6 Elemente) | 495–703 ms (724 Elemente) | ~30–70× |

Auffällig: Sowohl Laufzeit als auch Speicherverbrauch von `Open()` wachsen bei
120-facher Elementzahl nur um niedrige zweistellige Prozentsätze — der
Großteil des Overheads ist offensichtlich fixer Startkosten (Laden und JIT der
xBIM-Assemblies, Aufbau der EXPRESS-Schema-Metadatentabellen für alle
IFC4X3-Entitätstypen), nicht linear pro Element oder Byte. Nur der eigentliche
P-Set-Scan (Schritt 5), der pro Element tatsächlich Arbeit leistet, wächst
näherungsweise proportional zur Elementzahl.

### Die drei Fragen

**1. Bleibt die Anwendung bedienbar?**
Ja, bei dieser Dateigröße eindeutig: Open() ~1,2–1,4 s, P-Set-Scan über alle
724 Elemente unter einer Sekunde, Save ~0,3–0,45 s — der komplette
Testlauf liegt bei rund 2,5 Sekunden Gesamtlaufzeit. Für den in der
Aufgabenstellung genannten Maßstab ("Open() eine Minute" verkraftbar,
"P-Set-Scan bei jedem Dialogöffnen zwei Minuten" nicht) ist das nicht
ansatzweise ein Problem. Hochgerechnet mit der gemessenen Rate von
~802 ms/1.000 Elemente (Schritt 5) läge ein 10.000-Elemente-Modell bei
grob 8 s, ein 50.000-Elemente-Modell bei grob 40 s für einen vollständigen
Scan aller Elemente — beides mit dem vorhandenen Fortschrittsbalken plus
Abbrechen-Möglichkeit verkraftbar, **aber**: Das gilt nur für einen
*einmaligen* Scan pro Öffnen der Datei. Wird der P-Set-Scan (wie in Risiko
Nr. 4 für `GetElementByGuid` beschrieben) naiv bei *jedem* Dialogöffnen
erneut über alle Elemente laufen gelassen statt das Ergebnis zu cachen,
kippt genau dieses Verhalten bei 10.000+ Elementen in den in der
Aufgabenstellung explizit benannten Problemfall. Empfehlung unverändert aus
Risiko Nr. 4/5: Implementierung muss cachen, nicht bei jedem Zugriff neu
scannen.

**2. Reicht der Speicher?**
Bei dieser Dateigröße (3,75 MB) ja, ohne jede Einschränkung: ~136 MB
Prozess-Spitzenwert läuft auf jedem Rechner, der überhaupt eine
IFC-Anwendung startet, unproblematisch mit. Für die in der Aufgabenstellung
konkret genannte Sorge — ein 300-MB-Modell — liefert dieser Lasttest **keine
Messung, sondern nur eine Hochrechnung**, und die muss klar als solche
gekennzeichnet werden: Die getestete Datei ist mit 3,75 MB immer noch rund
80× kleiner als ein 300-MB-Modell, und mit 730 Elementen weit von den in
der ursprünglichen Einschätzung befürchteten "zehntausenden Elementen"
entfernt. Rechne ich den beobachteten Speicher-Overhead oberhalb der
Baseline (~104 MB oberhalb der ~32 MB Prozessstart-Grundlast) linear mit
der Dateigröße hoch, ergäbe das für ein 300-MB-Modell rund **8 GB**
zusätzlich zur Grundlast — auf einem normalen 8–16-GB-Arbeitsplatzrechner,
auf dem parallel weitere Anwendungen laufen, wäre das tatsächlich ein
Problem. Diese Hochrechnung ist aber auf Basis von genau einem Messpunkt
weit außerhalb des getesteten Bereichs gemacht (Faktor 80×) und mit
erheblicher Unsicherheit behaftet: Der beobachtete Sprung von 6 auf 730
Elemente zeigte gerade *unterproportionales* Wachstum, weil Fixkosten
dominierten — ob sich das bei 300 MB fortsetzt (dann deutlich weniger als
8 GB) oder ob geometrielastige Großmodelle stattdessen überproportional
wachsen (dann mehr), ist mit den hier vorliegenden Daten **nicht**
entscheidbar. Ehrliches Fazit: Die Speicherfrage ist für Modelle bis
mindestens ~750 Elemente/~4 MB geklärt und unproblematisch — für echte
Großmodelle (zehntausende Elemente, hunderte MB) bleibt sie ohne eine
tatsächlich so große Testdatei offen.

**3. Ändert sich die Empfehlung aus `EINSCHAETZUNG.md`?**
Nein, die Empfehlung (xBIM Essentials weiterverwenden) bleibt unverändert
— dieser Lasttest liefert ausschließlich zusätzliche Bestätigung, keinen
Gegenbeweis. Risiko Nr. 5 ist oben mit den tatsächlichen Zahlen aktualisiert
statt offen gelassen. Ergänzend zwei neue, konkrete Punkte für die
Implementierung: (a) der oben beschriebene Fund zu
`Instances.OfType(string, bool)` bei schemaübergreifend umstrukturierten
Oberklassen (neues Detail zu Risiko Nr. 1) und (b) die unter Frage 2
offen gebliebene Speicher-Hochrechnung für echte Großmodelle — sollte die
Anwendung real mit Modellen im dreistelligen MB-Bereich rechnen müssen,
empfiehlt sich vor dem endgültigen produktiven Commit ein dritter Lasttest
mit einer tatsächlich so großen Datei, oder ersatzweise eine Prüfung von
`Xbim.IO.Esent` (Datenbank-gestützter statt In-Memory-Modell-Store) als
Fallback für sehr große Modelle, falls verfügbar.

### Reproduzierbarkeit

Code: `spike/Lasttest/` (eigenes Projekt, referenziert dieselbe
`Xbim.Essentials`-Version 6.1.605 wie der Ursprungsspike). Aufruf:
`dotnet run -c Release --project spike/Lasttest -- <pfad-zur-ifc-datei>`.
Die verwendete Projektdatei ist nicht Teil dieses Repositories (siehe oben);
jede andere ausreichend große, reale IFC-Datei liefert vergleichbare
Diagnosewerte.
