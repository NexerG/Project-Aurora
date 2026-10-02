# Summarizes a frame capture folder: per thread, frame time and per-zone cost per frame.
# Per-frame zone totals and nearest-rank percentiles match TestRunner.CheckBudgets.
#   -Dir     a session folder (*.frames.xml), e.g. %APPDATA%\Arktis\Thorium\Profiling\<session> or <run>\<Action>
#   -Thread  only this thread file (Main, Render, Worker0, Bootstrap ...)
#   -From/-To  frame index range (the F I attribute), inclusive
#   -Zone    only zones whose name contains this text
#   -Top     at most this many zones per thread, by mean ms per frame
param(
    [Parameter(Mandatory = $true)][string]$Dir,
    [string]$Thread,
    [long]$From = 0,
    [long]$To = [long]::MaxValue,
    [string]$Zone,
    [int]$Top = 40
)

function Pct([double[]]$sorted, [double]$p) {
    $i = [Math]::Min($sorted.Count - 1, [int][Math]::Ceiling($p * $sorted.Count) - 1)
    return $sorted[[Math]::Max($i, 0)]
}

$files = Get-ChildItem -LiteralPath $Dir -Filter *.frames.xml | Sort-Object Name
if (-not $files) { Write-Output "no *.frames.xml in $Dir"; exit 1 }

foreach ($file in $files) {
    $lane = $file.Name -replace '\.frames\.xml$', ''
    if ($Thread -and $lane -ne $Thread) { continue }

    $names = @{}
    $freq = 1e7
    $frames = 0
    $firstFrame = $null; $lastFrame = $null
    $frameMs = New-Object System.Collections.Generic.List[double]
    $frameKB = 0.0
    $dropped = 0
    # per zone: per-frame ms list, calls, bytes, worst frame bytes
    $zoneMs = @{}; $zoneCalls = @{}; $zoneBytes = @{}; $zoneMaxBytes = @{}
    $counters = @{}
    $truncated = $false

    $inFrame = $false
    $fTicks = @{}; $fBytes = @{}

    $closeFrame = {
        foreach ($k in $fTicks.Keys) {
            if (-not $zoneMs.ContainsKey($k)) {
                $zoneMs[$k] = New-Object System.Collections.Generic.List[double]
                $zoneMaxBytes[$k] = 0.0
            }
            $zoneMs[$k].Add($fTicks[$k] * 1000.0 / $freq)
            if ($fBytes[$k] -gt $zoneMaxBytes[$k]) { $zoneMaxBytes[$k] = $fBytes[$k] }
        }
        $fTicks.Clear(); $fBytes.Clear()
    }

    $reader = [System.Xml.XmlReader]::Create($file.FullName)
    try {
        while ($reader.Read()) {
            if ($reader.NodeType -eq [System.Xml.XmlNodeType]::EndElement) {
                if ($reader.LocalName -eq 'F' -and $inFrame) { . $closeFrame; $inFrame = $false }
                continue
            }
            if ($reader.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
            switch ($reader.LocalName) {
                'FrameCapture' { $freq = [double]$reader.GetAttribute('Frequency') }
                'N' { $names[$reader.GetAttribute('I')] = $reader.GetAttribute('V') }
                'Batch' { $dropped += [int]$reader.GetAttribute('Dropped') }
                'F' {
                    $index = [long]$reader.GetAttribute('I')
                    $inFrame = $index -ge $From -and $index -le $To
                    if (-not $inFrame) { continue }
                    $frames++
                    if ($null -eq $firstFrame) { $firstFrame = $index }
                    $lastFrame = $index
                    $frameMs.Add([double]$reader.GetAttribute('D') * 1000.0 / $freq)
                    $frameKB += [double]$reader.GetAttribute('A') / 1024.0
                    if ($reader.IsEmptyElement) { . $closeFrame; $inFrame = $false }
                }
                'Z' {
                    if (-not $inFrame) { continue }
                    $n = $names[$reader.GetAttribute('N')]
                    $ticks = [double]$reader.GetAttribute('E') - [double]$reader.GetAttribute('B')
                    $a = $reader.GetAttribute('A')
                    $bytes = if ($a) { [double]$a } else { 0.0 }
                    $fTicks[$n] = [double]$fTicks[$n] + $ticks
                    $fBytes[$n] = [double]$fBytes[$n] + $bytes
                    $zoneCalls[$n] = [long]$zoneCalls[$n] + 1
                    $zoneBytes[$n] = [double]$zoneBytes[$n] + $bytes
                }
                'C' {
                    if (-not $inFrame) { continue }
                    $n = $names[$reader.GetAttribute('N')]
                    $counters[$n] = [double]$counters[$n] + [double]$reader.GetAttribute('V')
                }
            }
        }
    }
    catch [System.Xml.XmlException] { $truncated = $true }
    finally { $reader.Close() }
    if ($inFrame) { . $closeFrame }

    $header = "== $lane - $frames frames"
    if ($frames -gt 0) { $header += " (I $firstFrame..$lastFrame)" }
    if ($dropped -gt 0) { $header += ", $dropped dropped" }
    if ($truncated) { $header += ", TRUNCATED" }
    Write-Output $header
    if ($frames -eq 0) { continue }

    $sortedFrames = [double[]]($frameMs | Sort-Object)
    Write-Output ("  frame ms  p50 {0:N3}  p95 {1:N3}  max {2:N3}  mean {3:N3}   alloc {4:N1} KB/frame" -f `
        (Pct $sortedFrames 0.50), (Pct $sortedFrames 0.95), $sortedFrames[-1], ($frameMs | Measure-Object -Average).Average, ($frameKB / $frames))

    $rows = foreach ($k in $zoneMs.Keys) {
        if ($Zone -and $k -notlike "*$Zone*") { continue }
        $list = $zoneMs[$k]
        $sorted = [double[]]($list | Sort-Object)
        $sum = ($list | Measure-Object -Sum).Sum
        [pscustomobject]@{
            Zone = $k; Mean = $sum / $frames; Calls = $zoneCalls[$k] / $frames; KB = $zoneBytes[$k] / $frames / 1024.0
            Ran = $list.Count; P50 = (Pct $sorted 0.50); P95 = (Pct $sorted 0.95); Max = $sorted[-1]; MaxKB = $zoneMaxBytes[$k] / 1024.0
        }
    }
    if ($rows) {
        Write-Output ("  {0,-36} {1,9} {2,8} {3,9} | {4,6} {5,9} {6,9} {7,9} {8,9}" -f 'zone', 'ms/frame', 'calls/f', 'KB/frame', 'ran', 'p50', 'p95', 'max', 'maxKB')
        $rows | Sort-Object Mean -Descending | Select-Object -First $Top | ForEach-Object {
            Write-Output ("  {0,-36} {1,9:N3} {2,8:N1} {3,9:N1} | {4,6} {5,9:N3} {6,9:N3} {7,9:N3} {8,9:N1}" -f `
                $_.Zone, $_.Mean, $_.Calls, $_.KB, $_.Ran, $_.P50, $_.P95, $_.Max, $_.MaxKB)
        }
    }
    foreach ($k in ($counters.Keys | Sort-Object)) {
        if ($Zone -and $k -notlike "*$Zone*") { continue }
        Write-Output ("  #{0,-35} {1,9:N1} /frame" -f $k, ($counters[$k] / $frames))
    }
}
