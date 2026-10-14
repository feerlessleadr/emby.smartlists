<#
.SYNOPSIS
  Live check of the SmartLists phone-alert subscription, connecting the way the Embidextrous app does: the token ONLY in the X-Emby-Token
  header, deviceId in the address, no api_key.
.DESCRIPTION
  1. Connects to /embywebsocket and sends SmartListsEventsStart ("0,30000,<afterSeq>,<token>": the token is also in the body because Emby does not link a header-authenticated socket to its user).
  2. Expects the baseline SmartListsEvents message (ApiLevel, LatestSeq, Truncated, Events) right away.
  3. Waits for a heartbeat (empty Events) within 35 seconds.
  4. With -ExpectEvent, also waits for a message that carries events: cause a list refresh to fail while it waits.
  5. With -NonAdminToken, checks that a non-administrator gets only {"Error":"Administrator login required."}.
  6. Compares GET Plugins/SmartLists/Events with what was pushed.
  Tokens come from the environment (SMARTLISTS_TOKEN, SMARTLISTS_NONADMIN_TOKEN) or parameters; they are never printed.
  Run it only against a server you may test on.
.EXAMPLE
  $env:SMARTLISTS_TOKEN = '<administrator session token>'; ./dev/Test-PhoneAlerts.ps1 -Server http://localhost:8096 -ExpectEvent
#>
param(
    [Parameter(Mandatory)] [string] $Server,
    [string] $Token = $env:SMARTLISTS_TOKEN,
    [string] $NonAdminToken = $env:SMARTLISTS_NONADMIN_TOKEN,
    [long] $AfterSeq = -1,
    [switch] $ExpectEvent,
    [int] $ExpectEventSeconds = 120
)

$ErrorActionPreference = 'Stop'
if (-not $Token) { throw 'Set -Token or $env:SMARTLISTS_TOKEN (an administrator session token).' }
$Server = $Server.TrimEnd('/')
$wsBase = ($Server -replace '^http', 'ws') + '/embywebsocket?deviceId=smartlists-live-test'
$failures = 0
function Check($ok, $text) { if ($ok) { Write-Host "PASS  $text" } else { Write-Host "FAIL  $text"; $script:failures++ } }

function Open-Socket($token) {
    $ws = New-Object System.Net.WebSockets.ClientWebSocket
    $ws.Options.SetRequestHeader('X-Emby-Token', $token)
    $ws.ConnectAsync([Uri]$wsBase, [Threading.CancellationToken]::None).Wait()
    return $ws
}

function Send-Text($ws, $text) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($text)
    $ws.SendAsync((New-Object ArraySegment[byte] -ArgumentList (, $bytes)), 'Text', $true, [Threading.CancellationToken]::None).Wait()
}

# Reads one text message or returns $null after the timeout. The receive is never cancelled (cancelling a ClientWebSocket receive aborts the
# socket); an unfinished receive is kept and reused by the next call.
$script:pending = @{}
function Receive-Message($ws, [int] $seconds) {
    $key = $ws.GetHashCode()
    $deadline = (Get-Date).AddSeconds($seconds)
    while ($true) {
        if (-not $script:pending.ContainsKey($key)) {
            $buffer = New-Object byte[] 65536
            $segment = New-Object ArraySegment[byte] -ArgumentList (, $buffer)
            $script:pending[$key] = @{ Task = $ws.ReceiveAsync($segment, [Threading.CancellationToken]::None); Buffer = $buffer; Text = (New-Object System.Text.StringBuilder) }
        }
        $p = $script:pending[$key]
        $left = [int][Math]::Max(0, ($deadline - (Get-Date)).TotalMilliseconds)
        if (-not $p.Task.Wait($left)) { return $null }
        $result = $p.Task.Result
        [void]$p.Text.Append([Text.Encoding]::UTF8.GetString($p.Buffer, 0, $result.Count))
        if ($result.EndOfMessage) {
            $script:pending.Remove($key)
            return $p.Text.ToString() | ConvertFrom-Json
        }
        $segment = New-Object ArraySegment[byte] -ArgumentList (, $p.Buffer)
        $p.Task = $ws.ReceiveAsync($segment, [Threading.CancellationToken]::None)
    }
}

# Reads messages until one named SmartListsEvents arrives (Emby also sends KeepAlive and other messages).
function Receive-Events($ws, [int] $seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $left = [int][Math]::Ceiling(($deadline - (Get-Date)).TotalSeconds)
        if ($left -lt 1) { break }
        $m = Receive-Message $ws $left
        if ($null -eq $m) { return $null }
        if ($m.MessageType -eq 'SmartListsEvents') { return $m }
    }
    return $null
}

# ---- administrator subscription ----
$ws = Open-Socket $Token
# Like the app: afterSeq (empty = none) and the administrator token inside the start message; the token is never in the address.
$afterText = if ($AfterSeq -ge 0) { "$AfterSeq" } else { '' }
Send-Text $ws ('{"MessageType":"SmartListsEventsStart","Data":"0,30000,' + $afterText + ',' + $Token + '"}')

$first = Receive-Events $ws 10
Check ($null -ne $first) 'baseline SmartListsEvents arrives right after the start message'
$seen = @()
if ($first) {
    Check ($first.Data.ApiLevel -eq 1) 'Data.ApiLevel is 1'
    Check ($null -ne $first.Data.LatestSeq) ("Data.LatestSeq present ($($first.Data.LatestSeq))")
    Check ($first.Data.Truncated -eq $false -or $AfterSeq -ge 0) 'Data.Truncated present'
    if ($AfterSeq -lt 0) { Check (@($first.Data.Events).Count -eq 0) 'no events in the baseline when no afterSeq was sent' }
    $seen += @($first.Data.Events)
}

$beat = Receive-Events $ws 36
Check ($null -ne $beat) 'a second message (heartbeat) arrives within 36 seconds'
if ($beat) { $seen += @($beat.Data.Events) }

if ($ExpectEvent) {
    Write-Host "Waiting up to $ExpectEventSeconds s for an event: make a list refresh fail now."
    $deadline = (Get-Date).AddSeconds($ExpectEventSeconds)
    $got = $null
    while ((Get-Date) -lt $deadline -and -not $got) {
        $m = Receive-Events $ws 5
        if ($m -and @($m.Data.Events).Count -gt 0) { $got = $m }
    }
    Check ($null -ne $got) 'an event is pushed to the header-token socket'
    if ($got) {
        $seen += @($got.Data.Events)
        $e = $got.Data.Events[0]
        Check ($e.Name -like 'SmartLists.*') "event name $($e.Name)"
        Check ($e.Data.Seq -eq $e.Seq) 'Data.Seq equals Seq'
        Check ($e.Data.Event -and $e.Data.EventId -and $e.Data.ServerTimeUtc) 'common fields present'
        Check (($e | ConvertTo-Json -Depth 8) -notmatch '[A-Za-z]:\\|\\\\') 'payload has no drive or share path'
    }
}

Send-Text $ws '{"MessageType":"SmartListsEventsStop"}'
try { $ws.CloseAsync('NormalClosure', 'done', [Threading.CancellationToken]::None).Wait() } catch { }

# ---- catch-up route agrees with the push ----
$headers = @{ 'X-Emby-Token' = $Token }
$rest = Invoke-RestMethod -Headers $headers -Uri "$Server/emby/Plugins/SmartLists/Events?After=0&Limit=500"
Check ($null -ne $rest.LatestSeq) "GET Events works (LatestSeq $($rest.LatestSeq))"
foreach ($e in $seen) { Check (@($rest.Events | Where-Object { $_.Seq -eq $e.Seq }).Count -eq 1) "pushed Seq $($e.Seq) is in the catch-up log" }
$cat = Invoke-RestMethod -Headers $headers -Uri "$Server/emby/Plugins/SmartLists/Notifications/Catalog"
Check (@($cat.Events).Count -eq 3) 'GET Notifications/Catalog lists 3 events'
$info = Invoke-RestMethod -Headers $headers -Uri "$Server/emby/Plugins/SmartLists/info"
Check ($info.ApiVersion -eq 1 -and $info.Features -contains 'events' -and $info.Features -contains 'catalog') 'info: ApiVersion 1, Features has events and catalog'

# ---- non-administrator ----
if ($NonAdminToken) {
    $ws2 = Open-Socket $NonAdminToken
    Send-Text $ws2 ('{"MessageType":"SmartListsEventsStart","Data":"0,30000,,' + $NonAdminToken + '"}')
    $err = Receive-Events $ws2 10
    Check ($err -and $err.Data.Error -eq 'Administrator login required.') 'non-administrator gets the Error reply'
    Check ($err -and -not $err.Data.Events) 'non-administrator gets no events'
    try { $ws2.CloseAsync('NormalClosure', 'done', [Threading.CancellationToken]::None).Wait() } catch { }
}
else { Write-Host 'SKIP  non-administrator check (no -NonAdminToken)' }

if ($failures -gt 0) { Write-Host "$failures check(s) failed"; exit 1 }
Write-Host 'All checks passed'
