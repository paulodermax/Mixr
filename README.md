# Mixr

Hardware-Lautstärkemixer für Windows: ein ESP32-S3 mit AMOLED-Display, vier Fadern, Encoder und Tasten steuert
per USB die Lautstärke einzelner Programme, Discord-Mute/Deafen und die Medienwiedergabe — und zeigt Cover und
Titel des laufenden Songs auf dem Gerät.

```
┌──────────────────┐   USB-Serial/JTAG, 921600 Baud    ┌──────────────────────────────┐
│  ESP32-S3 Mixr   │ ◄──────────────────────────────► │  Mixr (Windows, WinUI 3)      │
│  LVGL-UI, Fader, │   Frames 0xAA|len|type|…|crc      │  Audio-Sessions, SMTC, Discord│
│  Encoder, Tasten │                                    │  Spielekatalog, Cover, Updates│
└──────────────────┘                                    └──────────────────────────────┘
```

## Installation (Endnutzer)

1. Neueste `Mixr-win-Setup.exe` von den [Releases](https://github.com/paulodermax/Mixr/releases) laden und starten.
2. Mixr erscheint im Tray und öffnet kurz das Fenster. Gerät per USB anschließen — es wird automatisch erkannt.
3. Unter **Einstellungen → Geräte-Firmware** zeigt die App, ob die Firmware auf dem Gerät zur App passt, und
   aktualisiert sie auf Knopfdruck.

**Updates:** Die App prüft alle 6 Stunden GitHub Releases, lädt Updates im Hintergrund und installiert sie,
wenn du es im Tray-Menü oder in den Einstellungen bestätigst (sonst beim nächsten Start). Einstellungen,
Spielekatalog und Cover liegen in `%LOCALAPPDATA%\Mixr` und überleben jedes Update.

**Deinstallation:** Windows-Einstellungen → Apps → Mixr. Der Datenordner `%LOCALAPPDATA%\Mixr` bleibt erhalten.

## Projektstruktur

| Ordner | Inhalt |
|---|---|
| `Mixr.App/` | WinUI-3-Desktop-App (Tray, Dashboard, Fader-Zuordnung, Einstellungen, Updater) |
| `Mixr.Core/` | Host-Logik: Serielles Protokoll, Audio (CoreAudio), SMTC, Hotkeys, Config, Firmware-Update |
| `PC/` | Headless-Konsolenvariante desselben Hosts (`Mixr.Console.exe`) für Debugging |
| `PC.Tests/` | xunit-Tests (Protokoll, Config, Firmware-Image) |
| `ESP/` | ESP-IDF-Firmware (ESP32-S3, LVGL 9) — Protokolldefinition in `ESP/main/protocol.h` |
| `searchengine/` | Eigenständige IGDB-Suche (CLI) für Katalog-Debugging |
| `scripts/`, `ESP/tools/` | Asset-Pipeline (SVG → PNG → RGB565) und serielle Testskripte |
| `covers/` | Mitgelieferte App-Cover (werden nach `%LOCALAPPDATA%\Mixr\covers` synchronisiert) |

## Entwicklung

Voraussetzungen: Windows 10/11, [.NET SDK 9](https://dotnet.microsoft.com/download) (Version laut `global.json`),
für die Firmware [ESP-IDF 5.5](https://docs.espressif.com/projects/esp-idf/en/stable/esp32s3/get-started/).

```powershell
# App bauen und starten (Debug, läuft ohne Installation; Updates sind dann deaktiviert)
dotnet build Mixr.sln
dotnet run --project Mixr.App

# Tests
dotnet test PC.Tests

# Firmware bauen und flashen (im ESP-IDF-Terminal)
cd ESP
idf.py set-target esp32s3
idf.py build flash monitor
```

Beim Debug-Start liegt die Firmware nicht in der App. Für lokale Tests des Geräte-Updates
`ESP/build/Mixr.bin` nach `Mixr.App/firmware/Mixr.bin` kopieren (Ordner ist gitignored).

### Konfiguration

- Laufzeit-Konfiguration: `%LOCALAPPDATA%\Mixr\config.yaml` (wird beim ersten Start aus `config.default.yaml` erzeugt;
  eine ältere `config.yaml` neben der EXE wird automatisch migriert).
- Zugangsdaten für IGDB/Twitch (optional, für Spiele-Cover): `%LOCALAPPDATA%\Mixr\config.secrets.yaml` oder in den
  Einstellungen der App. Vorlage: `Mixr.App/config.secrets.example.yaml`. Umgebungsvariablen `IGDB_CLIENT_ID` /
  `IGDB_CLIENT_SECRET` haben Vorrang. **Niemals echte Werte committen.**
- Logs: `%LOCALAPPDATA%\Mixr\logs\mixr_app.log` (rotiert bei 2 MB).
- `MIXR_DATA_DIR` verlegt den gesamten Datenordner (z. B. für portable Tests).

### Release veröffentlichen

Siehe [RELEASING.md](RELEASING.md) — kurz: `git tag v1.2.0 && git push origin v1.2.0`. GitHub Actions baut Firmware
und App, erzeugt den Velopack-Installer samt Delta-Update und veröffentlicht alles als GitHub Release.

## Protokoll PC ↔ Gerät

Definiert in [`ESP/main/protocol.h`](ESP/main/protocol.h) und gespiegelt in `Mixr.Core/Services/MixrProtocol.cs`.
Version 2 ergänzt einen `HELLO`-Handshake (Protokollversion, Firmware-Version, Fähigkeiten) und das
Firmware-Update (`FW_BEGIN` / `FW_CHUNK` / `FW_END` / `FW_ACK`).

**Firmware-Update im Feld:** Geräte mit OTA-Partition (≥ 4 MiB Flash, `ESP/partitions_ota.csv`) werden direkt über die
laufende USB-Verbindung aktualisiert. Die aktuelle Platine mit 2 MiB Flash hat nur eine App-Partition; dort nutzt die App
den USB-Download-Modus des ESP32-S3 mit `esptool` (wird einmalig nach `%LOCALAPPDATA%\Mixr\tools` geladen).

## Inspiration

- [deej](https://github.com/omriharel/deej)
- https://www.youtube.com/watch?v=x2yXbFiiAeI
- https://www.youtube.com/watch?v=9WqwH4tebzI
