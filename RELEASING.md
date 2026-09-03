# Release-Prozess

Mixr wird als **Velopack**-Paket über **GitHub Releases** verteilt. Ein Release = ein Git-Tag.
Installierte Apps prüfen GitHub Releases automatisch, laden Delta-Updates und installieren sie nach Bestätigung.

## Ein Release veröffentlichen

```powershell
git checkout main
git pull
git tag v1.2.0
git push origin v1.2.0
```

Die Pipeline `.github/workflows/release.yml` macht danach:

1. **Firmware** mit ESP-IDF 5.5.3 bauen, `PROJECT_VER` = `1.2.0` (über `ESP/version.txt`).
2. **App** publishen (`dotnet publish … -p:Version=1.2.0`), Firmware-Image als `firmware/Mixr.bin` ins Paket legen.
3. Vorherige Releases laden → **Delta-Pakete** erzeugen (`vpk pack`).
4. `Mixr-win-Setup.exe`, `Mixr-win-Portable.zip`, `*.nupkg`, `releases.win.json` und `RELEASES` als GitHub Release
   veröffentlichen (`vpk upload github`), zusätzlich `Mixr.bin`, Bootloader und Partitionstabelle als Assets.

Versionen sind SemVer **ohne** führendes `v` im Paket (`1.2.0`); der Tag trägt das `v`.
Pre-Releases: `v1.3.0-beta.1` — Velopack veröffentlicht sie als Pre-Release; die App ignoriert sie
(`GithubSource(..., prerelease: false)`).

## Was der Nutzer bekommt

| Datei | Zweck |
|---|---|
| `Mixr-win-Setup.exe` | Installer (pro Benutzer, kein Admin nötig) → `%LOCALAPPDATA%\Mixr\current\Mixr.exe` |
| `Mixr-win-Portable.zip` | Portable Variante, aktualisiert sich ebenfalls selbst |
| `Mixr-1.2.0-full.nupkg`, `*-delta.nupkg` | Update-Pakete, die die installierte App lädt |
| `releases.win.json` | Update-Feed, den `UpdateManager` liest |
| `Mixr.bin` (+ Bootloader, Partitionstabelle) | Firmware für manuelles Flashen mit `esptool` |

Alles Persistente (`config.yaml`, Secrets, Katalog, Cover, Logs) liegt in `%LOCALAPPDATA%\Mixr` — der
`current`-Ordner wird bei jedem Update komplett ersetzt.

## Lokal testen (ohne Tag)

```powershell
dotnet tool install -g vpk --version 1.2.0
# Firmware optional beilegen:
Copy-Item ESP\build\Mixr.bin Mixr.App\firmware\Mixr.bin

dotnet publish Mixr.App\Mixr.App.csproj -c Release -p:PublishProfile=win-x64 -p:Version=0.1.0
vpk pack --packId Mixr --packVersion 0.1.0 --packDir artifacts\publish --mainExe Mixr.exe `
         --packTitle Mixr --icon Mixr.App\Assets\AppIcon.ico --outputDir Releases
```

`Releases\Mixr-win-Setup.exe` installieren, dann mit `--packVersion 0.1.1` erneut packen und
`vpk`'s lokalen Ordner als Update-Quelle testen — oder die Versionsnummer hochzählen und den echten
Tag-Flow verwenden. Im Debug-Start aus Visual Studio meldet die App „nicht installiert“ und prüft keine Updates.

## Code-Signatur (optional, empfohlen vor breiter Verteilung)

Ohne Signatur zeigt Windows SmartScreen beim ersten Start des Setups „Unbekannter Herausgeber“.
Günstigster Weg: **Azure Artifact Signing** (ehem. Trusted Signing, ~10 $/Monat). In `release.yml` vor `vpk pack`
einen Signatur-Schritt ergänzen oder `vpk pack --signParams "…"` / `--azureTrustedSignFile` verwenden
(siehe Velopack-Doku „Code Signing“).

## Kompatibilität App ↔ Firmware

- Die Firmware meldet beim Verbinden `HELLO` mit `MIXR_PROTOCOL_VERSION` und ihrer Version.
- Inkompatible Protokolländerungen erhöhen `MIXR_PROTOCOL_VERSION` in `ESP/main/protocol.h` **und**
  `MixrProtocol.Version` in `Mixr.Core`. Additive Änderungen (neue Pakettypen) brauchen keine Erhöhung.
- Die App bietet ein Firmware-Update an, wenn das mitgelieferte Image neuer ist als das auf dem Gerät.

## Checkliste vor dem Tag

- [ ] `dotnet build Mixr.sln -c Release` und `dotnet test` grün
- [ ] Firmware baut (`idf.py build`), Änderungen an `protocol.h` auch in `MixrProtocol.cs`
- [ ] Keine echten Secrets in `config.secrets.example.yaml` oder anderswo
- [ ] `Docs/ROADMAP.md` / Release-Notes aktualisiert
