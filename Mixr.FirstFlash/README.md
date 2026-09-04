# Mixr.FirstFlash

Einmal-Werkzeug: schreibt die **Standard-Mixr-Firmware** (Bootloader + Partitionstabelle + `Mixr.bin`) auf ein neues ESP32-S3-Display (Werksdemo / nur COM-Port).

Danach erkennt die Mixr-App das Gerät als USB-HID. Weitere Updates laufen in der App unter *Einstellungen → Geräte-Firmware*.

## Start

Display per USB anstecken, im Repo-Root:

```powershell
dotnet run --project Mixr.FirstFlash
```

Das lädt GitHub **v0.0.7**, beendet eine laufende Mixr-App, findet den COM-Port und flasht sofort.

Optionen: `--version`, `--port`, `--local`, `--confirm`, `--already-bootloader` — siehe `dotnet run --project Mixr.FirstFlash -- --help`.

Wenn esptool nicht verbindet: BOOT halten, RESET tippen, BOOT loslassen, dann `--already-bootloader`.
