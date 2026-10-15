<#
.SYNOPSIS
    Toggles a Home Assistant light on and off a few times, using the token saved by Get-HaToken.ps1.

.EXAMPLE
    pwsh Tools\Test-HaLight.ps1
.EXAMPLE
    pwsh Tools\Test-HaLight.ps1 -Entity switch.metered_wall_plug_switch -Times 2
#>
param(
    [string]$Entity = "light.rgbw_controller_2",
    [int]$Times = 3,
    [double]$DelaySeconds = 3,
    [string]$BaseUrl = $(if ($env:HA_URL) { $env:HA_URL } else { "http://homeassistant.local:8123" }),
    [string]$TokenFile = (Join-Path $PSScriptRoot "..\Assets\Resources\ha_token.txt")
)

$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd("/")
$token = if ($env:HA_TOKEN) { $env:HA_TOKEN } else { (Get-Content $TokenFile -Raw).Trim() }
$headers = @{ Authorization = "Bearer $token" }
$domain = $Entity.Split(".")[0]
if ($domain -notin "light", "switch") { throw "Only light.* and switch.* entities may be controlled." }

function Get-State { (Invoke-RestMethod -Uri "$BaseUrl/api/states/$Entity" -Headers $headers).state }

function Set-State([string]$service) {
    Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/services/$domain/$service" -Headers $headers `
        -ContentType "application/json" -Body (@{ entity_id = $Entity } | ConvertTo-Json) | Out-Null
}

$start = Get-State
Write-Host "$Entity is '$start'"
if ($start -in "unavailable", "unknown") { throw "$Entity is $start; Home Assistant cannot reach the device." }

for ($i = 1; $i -le $Times; $i++) {
    foreach ($service in "turn_on", "turn_off") {
        Set-State $service
        Start-Sleep -Seconds $DelaySeconds
        Write-Host ("[{0}/{1}] {2,-8} -> state '{3}'" -f $i, $Times, $service, (Get-State))
    }
}

# Leave the light the way we found it.
if ($start -eq "on") { Set-State "turn_on"; Write-Host "Restored to 'on'." }
