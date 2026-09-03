# Mixr — Roadmap & Ideen

Kurzer Katalog für die weitere Produktentwicklung (Stand: Entwicklungsstand im Repo).

## Erledigt (Referenz)

- Hintergrund-Host mit SMTC, Seriell, Audio-Sessions, Discord-Hotkeys.
- USB-Wiederverbindung und COM-Erkennung über ESP32-S3 USB Serial/JTAG (VID/PID).
- Fader-Zuordnung: Bibliothek → Fader-Karten, Speicherung in `config.yaml`, Anzeige zugeordneter Programme, Entfernen pro Eintrag, eigene Ablagefläche für Drag & Drop.
- Release-Fundament: Velopack-Installer + Auto-Update aus GitHub Releases, zentrale Versionierung (`Directory.Build.props`, Git-Tag), CI/Release-Workflows, Daten in `%LOCALAPPDATA%\Mixr` (Config, Secrets, Logs mit Rotation), geordnetes Herunterfahren (Hook/Serial/Audio), Autostart nur als Opt-in.
- Protokoll v2: `HELLO`-Handshake (Firmware-Version, Fähigkeiten) und Firmware-Update über `FW_*` (esp_ota) bzw. USB-Download-Modus (esptool) für die 2-MiB-Platine.

## Offen aus dem Release-Umbau

- **Hardware:** GPIO17 ist doppelt belegt (Display-Reset `pins_config.h` vs. Encoder-CLK `board_pins.h`) — auf der Platine oder per Pin-Map lösen.
- **Flash ≥ 4 MiB:** mit `partitions_ota.csv` echte In-App-Updates ohne Download-Modus.
- **Code-Signatur** (Azure Artifact Signing) vor breiter Verteilung, sonst SmartScreen-Hinweis.
- IGDB-Client-Secret gehört nicht in Installationen — Cover-Proxy oder Nutzer-eigene Twitch-App (aktuell: Eingabe in den Einstellungen).

## Kurzfristig (nächste Iterationen)

1. **Audio-Session-Vorschau** (optional): Auf der Zuordnungsseite aktuell laufende Windows-Audio-Apps anzeigen (nur Lesen), um Suchstrings zu validieren — ohne automatisches Überschreiben der Config.
2. **Mehrsprachigkeit**: String-Ressourcen (`resw`) für DE/EN, statt Texte nur in XAML/Code.
3. **Katalog-Qualität**: Duplikate/Normalisierung von Spielnamen im `GameCatalogStore`, bessere Fehlerprotokolle beim Steam-/API-Abruf.
4. **Barrierefreiheit**: AutomationProperties auf Bibliotheks-Kacheln und Fader-Karten, Tastaturpfad für „Zuordnen“ (ohne Drag).

## Mittelfristig

1. **Einstellungen-UI** für `com_port`-Override, Baudrate, `invert_sliders` ohne manuelles YAML.
2. **Installer / MSIX**: saubere Installation, Startmenü, optionale Autostart-Steuerung sichtbar in der UI.
3. **Telemetrie (opt-in)**: anonyme Fehler- oder Nutzungsstatistik — nur mit Zustimmung und Datenschutz-Hinweis.
4. **Tests**: UI-Tests für Zuordnungslogik (reine C#-Logik aus `SliderMappingPage` in Services extrahieren und unit-testen).

## Langfristig / Forschung

1. **Eigenes USB-VID/PID** oder PID-Sublicense für klarere Geräteerkennung bei mehreren Produktlinien.
2. **ESP-Firmware**: optionale Anzeige der zuletzt zugeordneten Gruppen auf dem Display (nur wenn Protokoll erweitert wird).
3. **Profile**: mehrere Config-Sets (z. B. „Streaming“ / „Gaming“) umschaltbar per UI.

## Technische Schulden (optional aufräumen)

- `NETSDK1198` Publish-Profil-Warnung im App-Projekt prüfen.
- Gemeinsame `BoolToVisibilityConverter` zentral in `App.xaml` registrieren, falls weitere Seiten sie nutzen.
