# Mixr

Hardware-Lautstärkemixer für Windows: ein ESP32-S3 mit AMOLED-Display, vier Fadern, Encoder und Tasten steuert
per USB die Lautstärke einzelner Programme, Discord-Mute/Deafen und die Medienwiedergabe — und zeigt Cover und
Titel des laufenden Songs auf dem Gerät.

```
┌──────────────────┐   USB-HID-Composite (treiberlos)   ┌──────────────────────────────┐
│  ESP32-S3 Mixr   │ ◄──────────────────────────────► │  Mixr (Windows, WinUI 3)      │
│  LVGL-UI, Fader, │   Vendor-HID: Mixr-Protokoll       │  Audio-Sessions, SMTC, Discord│
│  Encoder, Tasten │   Consumer-HID: Medientasten → OS  │  Spielekatalog, Cover, Updates│
└──────────────────┘                                    └──────────────────────────────┘
```

> **Wichtig — was kostenlos ist und was nicht:** siehe [Kosten & Lizenzen](#kosten--lizenzen-wichtig) unten.
> Alles im Repository läuft ohne laufende Kosten; einige Schritte vor einem *Verkauf* nicht.

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
| `ESP/` | ESP-IDF-Firmware (ESP32-S3, LVGL 9, TinyUSB) — Protokoll `main/protocol.h`, Link-Backends `main/mixr_link_*.cpp`, Frame-Handler `main/mixr_proto.cpp` |
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

# Firmware bauen und flashen (im ESP-IDF-Terminal; HID-Produktfirmware)
cd ESP
idf.py set-target esp32s3
idf.py build flash            # kein `monitor`: im HID-Modus liegt die Konsole auf UART0

# Bench-Variante mit USB-Serial/JTAG (Logs + Protokoll auf einem COM-Port, wie früher)
idf.py -B build-serial -D SDKCONFIG=build-serial/sdkconfig -D SDKCONFIG_DEFAULTS="sdkconfig.defaults;sdkconfig.variant.serial" build flash monitor
```

Nach dem Wechsel auf diese Version einmal `idf.py fullclean` bzw. die alte `ESP/sdkconfig` löschen — neue
Defaults (TinyUSB, TJpgDec, Konsole) greifen nur bei frischer Konfiguration.

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

## USB-Architektur

Das Gerät meldet sich als **USB-HID-Composite** (TinyUSB auf dem USB-OTG-Peripheral des ESP32-S3) — so wie
Stream Deck, Loupedeck oder TourBox:

| Interface | Zweck |
|---|---|
| Vendor-HID (Usage Page `0xFF00`, 64-Byte-Reports) | Mixr-Protokoll: Fader, Tasten, Cover, Firmware-Update. Treiberlos auf Windows/macOS/Linux, kein COM-Port, keine Portnummern, kein „Port belegt“. |
| HID Consumer Control | Play/Pause/Next/Prev/Mute als Standard-Medientasten. **Funktioniert ohne die App** — Windows verarbeitet sie nativ. Die App teilt dem Gerät per `SET_BUTTON_MAP` mit, welche Tasten sie selbst übernimmt (z. B. Discord-Mute). |
| CDC-ACM (nur `CONFIG_MIXR_USB_DEBUG_CDC`) | Entwickler-Konsole. Kunden-Firmware hat kein CDC. |

Frames auf HID: `type | payload | CRC-16/CCITT` in Reports `[flags][n][data]` mit Start-/End-Bits — keine
Start-Byte-Suche, keine Verwechslung mit Log-Text. Firmware-Logs kommen auf Wunsch als eigene `LOG`-Frames
(`SET_LOG_STREAM`), Cover als **JPEG** (≈ 15–25 KB statt 115 KB) mit Hash-Abgleich, damit bekannte Bilder nicht erneut
übertragen werden.

**Legacy-Modus:** Mit `CONFIG_MIXR_USB_HID=n` (Variante `sdkconfig.variant.serial`) spricht die Firmware weiter
USB-Serial/JTAG mit `0xAA`-Framing — für Bench-Debugging mit `idf.py monitor`/JTAG. Die App unterstützt beide Wege
(HID zuerst, sonst COM-Port).

**Hinweis USB-OTG vs. Serial/JTAG:** Beide teilen sich die USB-Pins. Im HID-Modus gibt es kein JTAG-Debugging über
USB; die Konsole liegt auf UART0 (GPIO 43/44). Der ROM-Bootloader bleibt erreichbar (`ENTER_BOOTLOADER`-Befehl oder
BOOT-Taste).

## Protokoll PC ↔ Gerät

Definiert in [`ESP/main/protocol.h`](ESP/main/protocol.h) und gespiegelt in `Mixr.Core/Services/MixrProtocol.cs`
(`MixrFrameCodec.cs` für das HID-Framing). Version 3: `HELLO`-Handshake mit Fähigkeiten, `IMAGE_BEGIN/END/ACK`
(JPEG, Hash), `SET_BUTTON_MAP`, `LOG`/`SET_LOG_STREAM`, `ENTER_BOOTLOADER`, `PING/PONG`, Firmware-Update
`FW_BEGIN/CHUNK/END/ACK`. Ältere Firmware (v1/v2, RGB565 über Serial) wird weiter bedient.

**Firmware-Update im Feld:** Geräte mit OTA-Partition (≥ 4 MiB Flash, `ESP/partitions_ota.csv`) werden direkt über die
laufende USB-Verbindung aktualisiert. Die aktuelle Platine mit 2 MiB Flash hat nur eine App-Partition; dort schickt die
App das Gerät per `ENTER_BOOTLOADER` in den ROM-Download-Modus (es erscheint kurz als Espressif-COM-Port) und flasht mit
`esptool` (wird einmalig nach `%LOCALAPPDATA%\Mixr\tools` geladen, SHA-256-geprüft).

## Kosten & Lizenzen (wichtig)

Alles im Repository funktioniert **ohne laufende Kosten**: Velopack, TinyUSB, LVGL, ESP-IDF, HidSharp, GitHub
Releases und GitHub Actions (öffentliches Repo: unbegrenzt; privates Repo: 2.000 Minuten/Monat frei — ein Release
braucht ~15 Minuten). Folgende Punkte kosten Geld oder sind an Bedingungen geknüpft — **erst relevant, wenn Mixr an
andere Leute geht**:

| Thema | Aktueller Stand im Repo | Kostenlose Option | Kostenpflichtige Option |
|---|---|---|---|
| **USB Vendor-/Product-ID** | `0x1209:0x0001` = pid.codes-**Testkennung**, darf **nicht ausgeliefert** werden | pid.codes vergibt eine eigene PID kostenlos, **verlangt aber eine Open-Source-Lizenz** für Firmware *und* Host. Alternative: **Espressif-PID-Programm** (VID `0x303A`, kostenlos für Produkte auf Espressif-Chips, formloser Antrag per GitHub-PR im Repo `espressif/usb-pids`) | Eigene VID bei USB-IF: ~6.000 $ einmalig (+ Jahresbeitrag für Logo-Nutzung) |
| **Code-Signatur Windows** | Setup unsigniert → SmartScreen-Hinweis „Unbekannter Herausgeber“ beim ersten Start | keine (Nutzer klickt „Trotzdem ausführen“; Reputation baut sich mit Downloads auf) | Azure Artifact Signing ~10 $/Monat, oder OV-Zertifikat 200–400 €/Jahr |
| **Flash ≥ 4 MiB** | 2-MiB-Chip → Firmware-Update nur über Download-Modus | — | Neues Modul/Board mit 4–16 MiB Flash (Modulpreis-Differenz wenige Euro) — ermöglicht echtes In-App-OTA mit Rollback |
| **Zertifizierung** (CE/FCC/RoHS) | nicht vorhanden | — (nur für privaten Gebrauch/Einzelstücke unnötig) | Pflicht beim Verkauf in EU/USA: mehrere tausend Euro für EMV-Messungen |
| **IGDB/Twitch-Cover** | Nutzer trägt eigene Twitch-App-Zugangsdaten ein | kostenlos, aber jeder Nutzer braucht eine eigene Twitch-App | eigener Cover-Proxy-Server, falls man das Nutzern ersparen will |
| **Firmware-Signierung (Secure Boot)** | nicht aktiviert | ESP-IDF Secure Boot v2 ist kostenlos, aber **irreversibel** pro Chip (eFuse) — erst mit finaler Hardware aktivieren | — |

Empfehlung ohne Budget: Espressif-PID beantragen (kostenlos, keine Lizenzpflicht), unsigniert mit GitHub-Releases
verteilen, Zertifizierung/Signatur erst bei echtem Verkauf.

## Inspiration

- [deej](https://github.com/omriharel/deej)
- https://www.youtube.com/watch?v=x2yXbFiiAeI
- https://www.youtube.com/watch?v=9WqwH4tebzI
