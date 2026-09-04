# Mixr.FirstFlash

Einmal-Werkzeug: schreibt die **Standard-Mixr-Firmware** (Bootloader + Partitionstabelle + `Mixr.bin`) auf ein neues ESP32-S3-Display (Werksdemo / nur COM-Port).

Danach erkennt die Mixr-App das Gerät als USB-HID. Weitere Updates laufen in der App unter *Einstellungen → Geräte-Firmware*.

## Start

Mixr-App **beenden**, Display per USB anstecken, im Repo-Root:

```powershell
dotnet run --project Mixr.FirstFlash
```

Ohne lokale `ESP/build`-Artefakte lädt das Tool das neueste GitHub-Release (`Mixr.bin`, `bootloader.bin`, `partition-table.bin`).

```powershell
dotnet run --project Mixr.FirstFlash -- --yes
dotnet run --project Mixr.FirstFlash -- --port COM8 --version 0.0.7
```

Wenn esptool nicht verbindet: BOOT halten, RESET tippen, BOOT loslassen, dann `--already-bootloader`.
