param (
    [switch]$Uninstall
)

$pluginName = "MsfsPhysicsCamera"

# Detect executable location:
# 1. Packaged release: MsfsPhysicsCamera.exe (same folder)
# 2. Debug build: bin\Debug\net10.0-windows\MsfsPhysicsCamera.exe
$releasePath = Join-Path $PSScriptRoot "MsfsPhysicsCamera.exe"
$debugPath = Join-Path $PSScriptRoot "bin\Debug\net10.0-windows\MsfsPhysicsCamera.exe"

$exePath = $null
if (Test-Path $releasePath) {
    $exePath = $releasePath
} elseif (Test-Path $debugPath) {
    $exePath = $debugPath
}

if (-not $Uninstall -and -not $exePath) {
    Write-Host "Could not find MsfsPhysicsCamera.exe." -ForegroundColor Red
    Write-Host "Looked in:"
    Write-Host "  $releasePath"
    Write-Host "  $debugPath"
    Write-Host "Build the project first, or ensure the script is in the same folder as the executable."
    Pause
    exit
}

if ($exePath) {
    Write-Host "Using executable: $exePath"
}

# Detect MSFS 2020 and 2024 exe.xml locations
$possiblePaths = @(
    "$env:LOCALAPPDATA\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\exe.xml",       # MSFS 2024 MS Store
    "$env:APPDATA\Microsoft Flight Simulator 2024\exe.xml",                                  # MSFS 2024 Steam
    "$env:LOCALAPPDATA\Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalCache\exe.xml", # MSFS 2020 MS Store
    "$env:APPDATA\Microsoft Flight Simulator\exe.xml"                                        # MSFS 2020 Steam
)

$foundPaths = @()
foreach ($path in $possiblePaths) {
    if (Test-Path $path) {
        $foundPaths += $path
    } else {
        $dir = Split-Path $path
        if (Test-Path $dir) {
            Write-Host "Found MSFS installation at $dir, but exe.xml is missing. Creating it..."
            $baseXml = @"
<?xml version="1.0" encoding="utf-8"?>
<SimBase.Document Type="Launch" version="1,0">
  <Filename>exe.xml</Filename>
</SimBase.Document>
"@
            try {
                $baseXml | Out-File -FilePath $path -Encoding utf8 -ErrorAction Stop
                $foundPaths += $path
            } catch {
                Write-Host "Failed to create exe.xml at $path : $_" -ForegroundColor Red
            }
        }
    }
}

if ($foundPaths.Count -eq 0) {
    Write-Host "Could not find MSFS 2020 or 2024 installation (checked MS Store and Steam paths)." -ForegroundColor Red
    Write-Host "You may need to run MSFS at least once, or create exe.xml manually."
    Pause
    exit
}

foreach ($exeXmlPath in $foundPaths) {
    Write-Host "Processing exe.xml at: $exeXmlPath"

    # Load XML
    [xml]$xml = Get-Content $exeXmlPath

    if ($Uninstall) {
        # Remove existing node if uninstalling
        $nodes = $xml.SelectNodes("//Launch.Addon[Name='$pluginName']")
        if ($nodes.Count -gt 0) {
            foreach ($node in $nodes) {
                $xml.DocumentElement.RemoveChild($node) | Out-Null
            }
            $xml.Save($exeXmlPath)
            Write-Host "Successfully removed $pluginName from auto-start." -ForegroundColor Green
        } else {
            Write-Host "$pluginName was not found in auto-start."
        }
    } else {
        # Install / Update
        $nodes = $xml.SelectNodes("//Launch.Addon[Name='$pluginName']")
        if ($nodes.Count -gt 0) {
            Write-Host "Updating existing entry for $pluginName..."
            foreach ($node in $nodes) {
                $xml.DocumentElement.RemoveChild($node) | Out-Null
            }
        } else {
            Write-Host "Adding new entry for $pluginName..."
        }

        $addonNode = $xml.CreateElement("Launch.Addon")

        $nameNode = $xml.CreateElement("Name")
        $nameNode.InnerText = $pluginName
        $addonNode.AppendChild($nameNode) | Out-Null

        $disabledNode = $xml.CreateElement("Disabled")
        $disabledNode.InnerText = "False"
        $addonNode.AppendChild($disabledNode) | Out-Null

        $pathNode = $xml.CreateElement("Path")
        $pathNode.InnerText = $exePath
        $addonNode.AppendChild($pathNode) | Out-Null

        $xml.DocumentElement.AppendChild($addonNode) | Out-Null

        $xml.Save($exeXmlPath)
        Write-Host "Successfully added $pluginName to auto-start!" -ForegroundColor Green
    }
}

Write-Host "Press any key to exit..."
$Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown") | Out-Null
