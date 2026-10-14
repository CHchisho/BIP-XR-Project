<#
.SYNOPSIS
    Logs in to Home Assistant, creates a long-lived access token and saves it for Unity.

.DESCRIPTION
    1. Runs the Home Assistant login flow (username + password) to get a short-lived access token.
    2. Uses that token on the WebSocket API to create a long-lived access token
       (the same thing as Profile > Security > Long-lived access tokens > Create token).
    3. Saves it to Assets/Resources/ha_token.txt, where HomeAssistantClient reads it.
    4. Verifies the token by reading the state of -TestEntity.

    The password is never written to disk. It is asked for interactively, or taken
    from the HA_PASSWORD environment variable if that is set.
    ha_token.txt is gitignored; do not commit it.

.EXAMPLE
    pwsh Tools\Get-HaToken.ps1
.EXAMPLE
    pwsh Tools\Get-HaToken.ps1 -LifespanDays 60 -TestEntity light.rgbw_controller_2
#>
param(
    [string]$BaseUrl = "http://homeassistant.local:8123",
    [string]$Username = "bip_erasmus",
    [int]$LifespanDays = 30,
    [string]$TestEntity = "light.rgbw_controller_2",
    [string]$OutFile = (Join-Path $PSScriptRoot "..\Assets\Resources\ha_token.txt")
)

$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd("/")
$clientId = "$BaseUrl/"

# --- Password -------------------------------------------------------------------------
if ($env:HA_PASSWORD) {
    $password = $env:HA_PASSWORD
} else {
    $secure = Read-Host "Home Assistant password for $Username" -AsSecureString
    $password = [System.Net.NetworkCredential]::new("", $secure).Password
}

# --- 1. Login flow -> short-lived access token ----------------------------------------
Write-Host "Logging in to $BaseUrl as $Username..."
$flow = Invoke-RestMethod -Method Post -Uri "$BaseUrl/auth/login_flow" -ContentType "application/json" -Body (@{
    client_id    = $clientId
    handler      = @("homeassistant", $null)
    redirect_uri = $clientId
} | ConvertTo-Json)

$step = Invoke-RestMethod -Method Post -Uri "$BaseUrl/auth/login_flow/$($flow.flow_id)" -ContentType "application/json" -Body (@{
    client_id = $clientId
    username  = $Username
    password  = $password
} | ConvertTo-Json)
$password = $null

if ($step.type -ne "create_entry") {
    $reason = if ($step.errors) { ($step.errors | ConvertTo-Json -Compress) } else { "step '$($step.step_id)'" }
    throw "Login failed: $reason. (Wrong password, or the account needs MFA, which this script does not handle.)"
}

$auth = Invoke-RestMethod -Method Post -Uri "$BaseUrl/auth/token" -Body @{
    grant_type = "authorization_code"
    code       = $step.result
    client_id  = $clientId
}
Write-Host "Logged in (session token valid for $([int]($auth.expires_in / 60)) min)."

# --- 2. WebSocket API -> long-lived access token --------------------------------------
function Send-Ws($ws, $obj) {
    $bytes = [Text.Encoding]::UTF8.GetBytes(($obj | ConvertTo-Json -Compress))
    $ws.SendAsync([ArraySegment[byte]]::new($bytes), "Text", $true, [Threading.CancellationToken]::None).GetAwaiter().GetResult() | Out-Null
}

function Receive-Ws($ws) {
    $buffer = [byte[]]::new(16384)
    $stream = [IO.MemoryStream]::new()
    do {
        $r = $ws.ReceiveAsync([ArraySegment[byte]]::new($buffer), [Threading.CancellationToken]::None).GetAwaiter().GetResult()
        $stream.Write($buffer, 0, $r.Count)
    } until ($r.EndOfMessage)
    [Text.Encoding]::UTF8.GetString($stream.ToArray()) | ConvertFrom-Json
}

$wsUrl = ($BaseUrl -replace "^http", "ws") + "/api/websocket"
$ws = [Net.WebSockets.ClientWebSocket]::new()
try {
    $ws.ConnectAsync([Uri]$wsUrl, [Threading.CancellationToken]::None).GetAwaiter().GetResult() | Out-Null
    $null = Receive-Ws $ws                                   # auth_required
    Send-Ws $ws @{ type = "auth"; access_token = $auth.access_token }
    $hello = Receive-Ws $ws
    if ($hello.type -ne "auth_ok") { throw "WebSocket auth failed: $($hello.message)" }

    # Token names must be unique per user, so include a timestamp.
    $name = "XR SmartHouse $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
    Send-Ws $ws @{ id = 1; type = "auth/long_lived_access_token"; client_name = $name; lifespan = $LifespanDays }
    $reply = Receive-Ws $ws
    if (-not $reply.success) { throw "Could not create long-lived token: $($reply.error.message)" }
    $token = $reply.result
} finally {
    if ($ws.State -eq "Open") {
        $ws.CloseAsync("NormalClosure", "", [Threading.CancellationToken]::None).GetAwaiter().GetResult() | Out-Null
    }
    $ws.Dispose()
}
Write-Host "Created long-lived token '$name', valid for $LifespanDays days (until $((Get-Date).AddDays($LifespanDays).ToString('yyyy-MM-dd')))."

# --- 3. Save -------------------------------------------------------------------------
$OutFile = [IO.Path]::GetFullPath($OutFile)
New-Item -ItemType Directory -Force (Split-Path $OutFile) | Out-Null
[IO.File]::WriteAllText($OutFile, $token)
Write-Host "Saved to $OutFile"

# --- 4. Verify -----------------------------------------------------------------------
$state = Invoke-RestMethod -Uri "$BaseUrl/api/states/$TestEntity" -Headers @{ Authorization = "Bearer $token" }
Write-Host "API check OK: $TestEntity is '$($state.state)'."
