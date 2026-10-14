# Regenerates the sample GPX files in this folder. Run: pwsh ./samples/generate-samples.ps1
# Courses run along a meridian, so each step's Haversine distance is exactly R·Δφ and the
# expected distance can be checked by hand. Elevation steps are binary-exact (0.75, 1.5, 3.0 m).
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$inv = [Globalization.CultureInfo]::InvariantCulture
$R = 6371000.0
$lat0 = 25.0300000000
$lon0 = 121.5600000000
function LatAt([double]$metres) { $lat0 + $metres / $R * 180 / [Math]::PI }
function F([double]$v, [string]$fmt) { $v.ToString($fmt, $inv) }

function Pt([double]$metres, $ele, [double]$lonOffset = 0) {
    $lat = F (LatAt $metres) '0.0000000000'
    $lon = F ($lon0 + $lonOffset) '0.0000000000'
    if ($null -eq $ele) { return "      <trkpt lat=""$lat"" lon=""$lon""/>" }
    return "      <trkpt lat=""$lat"" lon=""$lon""><ele>$(F $ele '0.00')</ele></trkpt>"
}

function Write-Gpx([string]$file, [string]$name, [string[][]]$segments) {
    $sb = [Text.StringBuilder]::new()
    [void]$sb.AppendLine('<?xml version="1.0" encoding="UTF-8"?>')
    [void]$sb.AppendLine('<gpx version="1.1" creator="PacePlanner sample generator" xmlns="http://www.topografix.com/GPX/1/1">')
    [void]$sb.AppendLine("  <trk><name>$name</name>")
    foreach ($seg in $segments) {
        [void]$sb.AppendLine('    <trkseg>')
        foreach ($line in $seg) { [void]$sb.AppendLine($line) }
        [void]$sb.AppendLine('    </trkseg>')
    }
    [void]$sb.AppendLine('  </trk>')
    [void]$sb.AppendLine('</gpx>')
    [IO.File]::WriteAllText((Join-Path $here $file), $sb.ToString().Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
}

# 1) Out-and-back 10 km, 100 m spacing.
$ele = [Collections.Generic.List[double]]::new()
$e = 10.0
$ele.Add($e)
foreach ($i in 1..15) { $ele.Add($e) }                        # 0–1.5 km flat 10 m
foreach ($i in 1..15) { $e += 3.0;  $ele.Add($e) }            # 1.5–3.0 km +45 m (3 %)
foreach ($i in 1..20) { $e -= 0.75; $ele.Add($e) }            # 3.0–5.0 km −15 m
foreach ($i in 1..20) { $e += 0.75; $ele.Add($e) }            # 5.0–7.0 km +15 m
foreach ($i in 1..15) { $e -= 3.0;  $ele.Add($e) }            # 7.0–8.5 km −45 m
foreach ($i in 1..15) { $ele.Add($e) }                        # 8.5–10 km flat
$pts = for ($i = 0; $i -le 100; $i++) {
    $m = if ($i -le 50) { $i * 100.0 } else { (100 - $i) * 100.0 }
    Pt $m $ele[$i]
}
Write-Gpx 'river-park-10k.gpx' '河濱公園 10K 折返' @(, [string[]]$pts)

# 2) 5 km course recorded in three <trkseg> with a 200 m GPS pause between seg 1 and seg 2.
$seg1 = for ($i = 0; $i -le 20; $i++) { Pt ($i * 100.0) (5.0 + 1.5 * $i) }            # 0–2.0 km, 5→35 m
$seg2 = for ($i = 0; $i -le 12; $i++) { Pt (2200 + $i * 100.0) (35.0 - 1.5 * $i) }    # 2.2–3.4 km, 35→17 m
$seg3 = for ($i = 0; $i -le 16; $i++) { Pt (3400 + $i * 100.0) (17.0 + 0.75 * $i) }   # 3.4–5.0 km, 17→29 m
Write-Gpx 'multi-segment-5k.gpx' '多段軌跡 5K' @([string[]]$seg1, [string[]]$seg2, [string[]]$seg3)

# 3) Noisy 3 km: ±1 m elevation jitter, ±0.00001° longitude jitter, every 10th point lacks <ele>, no <time>.
$pts = for ($i = 0; $i -le 60; $i++) {
    $base = if ($i -le 20) { 50.0 } elseif ($i -le 40) { 50.0 + 1.5 * ($i - 20) } else { 80.0 }
    $jitter = if ($i % 2 -eq 0) { 1.0 } else { -1.0 }
    $value = if ($i % 10 -eq 5) { $null } else { $base + $jitter }
    Pt ($i * 50.0) $value ($jitter * 0.00001)
}
Write-Gpx 'noisy-gps-3k.gpx' 'GPS 雜訊 3K' @(, [string[]]$pts)

# 4) 5 km without any elevation or time data.
$pts = for ($i = 0; $i -le 50; $i++) { Pt ($i * 100.0) $null }
Write-Gpx 'no-elevation-5k.gpx' '無海拔 5K' @(, [string[]]$pts)

# Invalid files for manual error-handling tests.
$bad = Join-Path $here 'invalid'
New-Item -ItemType Directory -Force $bad | Out-Null
[IO.File]::WriteAllText((Join-Path $bad 'empty.gpx'), '')
[IO.File]::WriteAllText((Join-Path $bad 'not-xml.gpx'), "這不是 XML，只是一般文字。`n")
[IO.File]::WriteAllText((Join-Path $bad 'truncated.gpx'), '<?xml version="1.0"?><gpx><trk><trkseg><trkpt lat="25.03" lon="121.56"><ele>10</ele></trkpt><trkpt lat="25.04"')
[IO.File]::WriteAllText((Join-Path $bad 'no-track-points.gpx'), '<?xml version="1.0"?><gpx version="1.1"><wpt lat="25.03" lon="121.56"><name>只有航點</name></wpt></gpx>')
[IO.File]::WriteAllText((Join-Path $bad 'kml-file.gpx'), '<?xml version="1.0"?><kml xmlns="http://www.opengis.net/kml/2.2"><Document/></kml>')
Write-Host 'Samples generated.'
