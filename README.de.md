# FocusFence 1.2

Eine kompakte Windows-App für .NET Framework 4.8. Sie hält die Maus im Spielbereich, erkennt typische Bild-in-Bild-Fenster automatisch und kann Klicks durch das Video an das Spiel weiterreichen.

[English](README.md) · [Portable ZIP herunterladen](https://github.com/ZazaJr24/FocusFence/releases/latest/download/FocusFence-Portable.zip)

## Starten

`FocusFence.exe` aus dem entpackten ZIP starten (im Quellprojekt: `dist\FocusFence.exe`). Eine Installation ist nicht nötig. Windows 10/11 und .NET Framework 4.8 werden benötigt. Einstellungen bleiben lokal in `%LOCALAPPDATA%\FocusFence`.

1. Das Browser-Video als **Bild-in-Bild** öffnen. FocusFence sucht das Videofenster automatisch.
2. **Einschalten** und ins Spiel wechseln. Bekannte Spiele (z. B. Fortnite, VALORANT und Counter-Strike 2) werden automatisch ausgewählt.
3. Andere Spiele direkt auf der Schutz-Seite im Feld **Spiel** wählen. Ein Profil wird dabei automatisch gespeichert; zusätzliche Einrichtung ist nicht erforderlich.
4. **Alt+Tab** gibt Maus und Videobedienung frei. Beim Zurückwechseln greift der Schutz wieder.

| Tastenkürzel | Aktion |
| --- | --- |
| Strg+Alt+F8 | Schutz an/aus |
| Strg+Alt+F9 | 30 Sekunden freigeben / sofort fortsetzen |
| Strg+Alt+F12 | Notfall: Schutz aus, Maus und Video frei |

Die ersten beiden Kürzel sind in den Einstellungen umstellbar. Ist das Notfall-Kürzel belegt, lässt sich der Schutz nicht aktivieren.

## Funktionen

- Spielprofile mit eigenem Namen, Prozess, Randabstand und PiP-Klickschutz.
- Maus auf den sichtbaren Spielbereich oder einen gewählten Monitor begrenzen. Beim Fenstermodus wird bei über mehrere Monitore reichenden Spielen der Teil auf dem nächstliegenden Monitor verwendet.
- Fensterposition und -größe werden laufend berücksichtigt; physische Koordinaten mit DPI-Unterstützung.
- Automatische PiP-Erkennung im Hintergrund, einschließlich erneuter Auswahl nach einem Browser-Neustart. Klickschutz nur während des Spielens.
- Bekannte Spiele und gespeicherte Spielprofile werden automatisch berücksichtigt. Beide Automatiken sind in den Einstellungen abschaltbar.
- Kompakte Oberfläche mit Tabs statt großer Seitenleiste. Manuelle Fensterauswahl bei Bedarf unter **Bild-in-Bild → Ändern**.
- Tray-Menü mit Öffnen, Umschalten, Pause, Notfall-Freigabe und Beenden. Standardmäßig läuft die App beim Schließen im Tray weiter.
- Optionaler Windows-Autostart, Start im Tray und Umschaltton. Autostart ist standardmäßig aus; der Schutz startet immer ausgeschaltet.
- Profile als XML importieren/exportieren. Vorhandene Prozesse werden beim Import nicht überschrieben.
- Geschützte Spielzeit, lokale Ereignisdatei und separater Wiederherstellungsprozess für einen unerwarteten Programmabbruch.
- Bei Bildschirmsperre, Abmeldung und Monitoränderungen wird der Schutz deaktiviert.

## Hinweise

Die App sucht etwa alle 1,2 Sekunden nach Fenstern und prüft Browserprozess, native Fensterklasse, Titel und Fenstermerkmale. Die Heuristik berücksichtigt typische Fenster von Firefox, Chrome, Edge und weiteren Chromium-Browsern mit deutschen und mehreren anderen PiP-Titeln. Passende kompakte Browser-Toolfenster können auch mit einem eigenen Document-PiP-Titel erkannt werden. Sonderfenster bleiben manuell auswählbar. Normale Browserseiten werden nicht allein aufgrund eines PiP-Begriffs im Titel ausgewählt.

Bei mehreren Videos bleibt die bisherige Auswahl erhalten. Ohne bestehende Auswahl bittet die App um eine Auswahl unter **Ändern**. Eine manuelle Auswahl wird nicht von der Suche überschrieben; **Automatisch erkennen** schaltet die Automatik wieder ein. Ausgeblendete oder minimierte PiP-Fenster werden nicht automatisch gewählt. Die Erkennung benötigt keine Netzwerkverbindung.

Der Schutz wird nie durch die Erkennung eingeschaltet. Du aktivierst ihn mit **Einschalten**. Einstellungen und Profile der Vorgängerversion bleiben erhalten.

Fenster- oder randloser Vollbildmodus eignet sich für Bild-in-Bild. Einige Browser-Videoimplementierungen oder geschützte Spiele lassen Änderungen an ihren Fenstern nicht zu. Zugriff auf höher privilegierte Fenster kann von Windows abgelehnt werden. Fehler werden im Status angezeigt; bei Fehlern stoppt der Schutz. Funktion mit dem konkreten Spiel und Browser zunächst außerhalb einer wichtigen Runde prüfen.

FocusFence verwendet `ClipCursor`, Vordergrund-Fensterereignisse und erweiterte Fensterstile. Es injiziert keinen Code und greift nicht auf Spielspeicher zu. Eine allgemeine Zusage zur Verträglichkeit mit jedem Spiel oder dessen Anti-Cheat ist nicht möglich.

Der PiP-Klickschutz setzt bei einem nicht geschichteten Fenster `WS_EX_LAYERED` und `WS_EX_TRANSPARENT` und verwendet volle Deckkraft. Bereits vorhandene Layer-Einstellungen bleiben unangetastet. Nur von FocusFence hinzugefügte Stilbits werden entfernt. Ein separater Prozess stellt diese Änderungen bei einem Absturz wieder her, soweit Windows und das Zielfenster das zulassen. Bei einem hart beendeten oder blockierten Windows-System kann kein Hintergrundprozess arbeiten.

Der Autostart verweist auf den aktuellen Pfad der EXE. Nach dem Verschieben der App die Option aus- und wieder einschalten. Zum Entfernen Autostart ausschalten, FocusFence über das Tray beenden und den App-Ordner löschen. Persönliche Daten können zusätzlich im lokalen Datenordner gelöscht werden.

## Quellcode und Build

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

Der Build verwendet den mit .NET Framework gelieferten C#-Compiler; keine NuGet-Pakete erforderlich. `FocusFence\FocusFence.csproj` kann alternativ mit Visual Studio und dem .NET-Framework-4.8-Developer-Pack geöffnet werden.

Die Tests prüfen PiP-Erkennung anhand von Fensterdaten, Fehlzuordnungen, Browser-Neustart, mehrere Videos, manuelle Priorität, Zustandslogik, Monitorgrenzen, XML-Speicherung, WPF-Bedienung, echte PiP-Fensterstile und deren Wiederherstellung nach erzwungenem Prozessende. Für die Zustandswechsel des Schutzes werden ein steuerbarer Vordergrund und ein Test-Mausbackend verwendet. Der echte Windows-Aufruf wird mit dem unveränderten bestehenden Sperrbereich geprüft. Die Tests nehmen deinem Spiel weder Maus noch Fokus weg. Die Startprüfung kontrolliert auch die echte Hintergrundsuche. Sie ersetzen keinen interaktiven Test jeder Spiel-/Browser-Kombination.

Die Oberfläche lässt sich mit `dist\FocusFence.exe --render preview.png` ohne Schutz, Tray und gespeicherte Benutzerprofile rendern.

Für die vollständige Startprüfung muss FocusFence vorher beendet sein, damit die globalen Tastenkürzel frei sind. `build.ps1 -Test` führt auch diese Startprüfung aus. Ein kurzer Spieltest mit dem gewählten Browser bleibt erforderlich, um dessen konkrete PiP-Implementierung zu prüfen.

Win32-Dokumentation: [ClipCursor](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-clipcursor), [Layered Windows und Klickweiterleitung](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features#layered-windows).
