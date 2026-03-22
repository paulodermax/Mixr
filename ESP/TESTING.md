# Mixr testen (ein USB-Kabel, möglichst ohne Reset)

## Voraussetzungen

- Firmware gebaut und geflasht: `idf.py build flash`
- Python: `pip install pyserial pillow`
- **Nur ein Programm** darf den COM-Port gleichzeitig öffnen — `idf.py monitor` **beenden**, bevor du das Sende-Skript startest (oder umgekehrt).

## Schnelltest Cover (wenig Reset-Risiko)

1. Monitor **schließen** (Strg+] im `idf.py monitor`).
2. Vom Repo-Root `Mixr`:

```text
python tools/mixr_send_demo.py COMx --png tools/mein_cover.png --title "Song" --artist "Band" --chunk-delay-ms 5 --warmup-sec 0.5 --listen-sec 0
```

Auf dem Display: **Debug-Ecke unten rechts** zeigt `#n R:USB` usw. — `R:USB` = letzter Reset durch USB-Peripherie (typisch nach Host/Treiber). Nach stabiler Session sollte `#` **nicht** bei jedem Test hochzählen.

(`COMx` durch deinen Port ersetzen, z. B. `COM6`.)

3. **Erfolg:** Titel, Artist und Cover erscheinen; **kein** kurzes Aufflackern / Boot-Screen mitten drin.
4. **Reset erkennen:** In der Ecke zeigt die Firmware eine **Start-Nummer** (`#1`, `#2`, …). Steigt sie nach dem Senden → Gerät ist neu gestartet. Alternativ: danach kurz `idf.py monitor` — erscheint direkt `rst:` oder Bootloader-Zeilen, war ein Reset.

## Wenn noch `rst:0x15` / Cover fehlt

- **`--chunk-delay-ms` erhöhen:** `4`, dann `5` (langsamer, dafür sanfter für USB).
- **`--listen-sec 0`** lassen beim Test (kein langes Offenhalten des Ports nach dem Senden).
- **Nicht** parallel: Spotify, andere Tools, die denselben COM-Port anfassen.

## Slider & Tasten → PC

- USB verbunden lassen (gleiches Kabel).
- Fader bewegen / Tasten drücken → Host-Programm muss das **Binärprotokoll** lesen (nicht nur Text-Logs). Rohe Bytes in Logs sind erwartbar, solange Monitor und Skript nicht gleichzeitig laufen.

## Logs ansehen

- Entweder **nur** Monitor, **oder** nur Sende-Skript.
- Optional: `mixr_send_demo.py ... --listen-sec 3` **nach** stabilem Cover-Test (kann selten Port-Reset auslösen — bei Problemen `0`).

## Kein Reset garantiert?

Die Hardware kann unter Last **`USB_UART_CHIP_RESET`** melden — vollständig verhindern lässt sich das per Software auf **einem** USB nicht zu 100 %. Mit **großem RX-Puffer** (Firmware), **Chunk-Delay** (Skript) und **ohne** parallelen Monitor ist das praktische Ziel „stabil im Alltag“.
