# Als Administrator ausführen!
$ServiceName = "MixrService"
$ExePath = Join-Path $PSScriptRoot "Mixr.exe"

# Dienst erstellen
New-Service -Name $ServiceName -BinaryPathName $ExePath -DisplayName "Mixr Audio Controller" -StartupType Automatic

# Dienst starten
Start-Service -Name $ServiceName

Write-Host "Mixr wurde erfolgreich als Dienst installiert und gestartet!" -ForegroundColor Green