# PacePlanner UI end-to-end test (test plan L2/L3). Run with Windows PowerShell 5.1:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests/e2e/Run-E2E.ps1
# Drives the real app through UI Automation; file dialogs are driven with Win32 messages.
# The user's %LocalAppData%\PacePlanner\plans.db is backed up first and restored at the end.
param(
    [string]$Configuration = 'Release',
    [string]$OutDir = (Join-Path $PSScriptRoot 'out')
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$samples = Join-Path $repo 'samples'
$exe = Join-Path $repo "src\PacePlanner.App\bin\x64\$Configuration\net9.0-windows10.0.19041.0\win-x64\PacePlanner.exe"
if (-not (Test-Path $exe)) { throw "找不到 $exe，請先執行 dotnet build PacePlanner.sln -c $Configuration" }
if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
New-Item -ItemType Directory -Force $OutDir | Out-Null

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class E2EWin {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr h);
  [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  static string Cls(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
  public static IntPtr FindDialog(uint pid) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid && IsWindowVisible(h) && Cls(h) == "#32770") { found = h; return false; } return true; }, IntPtr.Zero);
    return found;
  }
  // File-name edit box: the Edit whose ancestor has control id 1148 (cmb13), else the first Edit.
  public static IntPtr FindFileNameEdit(IntPtr dlg) {
    IntPtr best = IntPtr.Zero, first = IntPtr.Zero;
    EnumChildWindows(dlg, (h, l) => {
      if (Cls(h) == "Edit") {
        if (first == IntPtr.Zero) first = h;
        for (var p = h; p != IntPtr.Zero && p != dlg; p = GetParent(p)) if (GetDlgCtrlID(p) == 1148) { best = h; return false; }
      }
      return true; }, IntPtr.Zero);
    return best != IntPtr.Zero ? best : first;
  }
}
"@
[E2EWin]::SetProcessDPIAware() | Out-Null

$A = [System.Windows.Automation.AutomationElement]
$TScope = [System.Windows.Automation.TreeScope]   # not $S: PowerShell names are case-insensitive and tests use $s
$CT = [System.Windows.Automation.ControlType]
$script:AppPid = 0
$script:Results = New-Object System.Collections.ArrayList

# ---------- result recording ----------
function Record([string]$Id, [string]$What, [bool]$Pass, [string]$Actual) {
    $status = if ($Pass) { 'PASS' } else { 'FAIL' }
    [void]$script:Results.Add([pscustomobject]@{ Id = $Id; Check = $What; Status = $status; Actual = $Actual })
    $color = if ($Pass) { 'Green' } else { 'Red' }
    Write-Host ("  [{0}] {1} - {2} ({3})" -f $status, $Id, $What, $Actual) -ForegroundColor $color
}
function Skip([string]$Id, [string]$What, [string]$Reason) {
    [void]$script:Results.Add([pscustomobject]@{ Id = $Id; Check = $What; Status = 'SKIP'; Actual = $Reason })
    Write-Host "  [SKIP] $Id - $What ($Reason)" -ForegroundColor Yellow
}
function Case([string]$Id, [string]$Title, [scriptblock]$Body) {
    Write-Host "== $Id $Title" -ForegroundColor Cyan
    try { & $Body } catch {
        Record $Id "執行時發生例外" $false ($_.Exception.Message + ' @ ' + $_.InvocationInfo.PositionMessage.Split("`n")[0])
        try { Shot "$Id-exception" } catch { }
    }
}

# ---------- app lifecycle ----------
function Test-Elevated {
    ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
function Start-App {
    Get-Process PacePlanner -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    # Run as a normal (non-elevated) user, like a coach would.
    if (Test-Elevated) { & runas.exe /trustlevel:0x20000 "`"$exe`"" | Out-Null } else { Start-Process $exe | Out-Null }
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 500
        $p = Get-Process PacePlanner -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
        if ($p) { $script:AppPid = $p.Id; Start-Sleep -Milliseconds 1500; return }
    }
    throw 'App 沒有在 30 秒內開啟視窗'
}
function App-Alive { [bool](Get-Process -Id $script:AppPid -ErrorAction SilentlyContinue) }
function Close-App {
    $w = Win
    $w.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    for ($i = 0; $i -lt 40 -and (App-Alive); $i++) { Start-Sleep -Milliseconds 250 }
    if (App-Alive) { throw 'App 關閉逾時' }
}

# ---------- UI Automation helpers ----------
function Prop($p, $v) { New-Object System.Windows.Automation.PropertyCondition($p, $v) }
function Win { $A::RootElement.FindFirst($TScope::Children, (Prop $A::ProcessIdProperty $script:AppPid)) }
function All($type) { @((Win).FindAll($TScope::Descendants, (Prop $A::ControlTypeProperty $type))) }
function ByName([string]$Name, $Type) {
    $el = (Win).FindFirst($TScope::Descendants, (New-Object System.Windows.Automation.AndCondition((Prop $A::NameProperty $Name), (Prop $A::ControlTypeProperty $Type))))
    if (-not $el) { throw "找不到 UI 元素：$Name" }
    $el
}
function Settle { Start-Sleep -Milliseconds 700 }
function Click([string]$Name) {
    (ByName $Name $CT::Button).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Settle
}
function Edit-ByPrefix([string]$Prefix) {
    $el = All $CT::Edit | Where-Object { $_.Current.Name.StartsWith($Prefix) } | Select-Object -First 1
    if (-not $el) { throw "找不到輸入框：$Prefix" }
    $el
}
function Set-Field([string]$Prefix, [string]$Value) {
    $el = Edit-ByPrefix $Prefix
    $el.SetFocus()
    $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value)
    (ByName '儲存' $CT::Button).SetFocus()   # NumberBox commits its value on lost focus
    Settle
}
function Get-Field([string]$Prefix) { (Edit-ByPrefix $Prefix).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
# ComboBoxes in layout order: 0 = 距離, 1 = 配速策略, 2 = 補給方式
function Select-Combo([int]$Index, [string]$Item) {
    $combo = (All $CT::ComboBox)[$Index]
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 400
    $li = $combo.FindFirst($TScope::Descendants, (New-Object System.Windows.Automation.AndCondition((Prop $A::NameProperty $Item), (Prop $A::ControlTypeProperty $CT::ListItem))))
    if (-not $li) { throw "下拉選單沒有「$Item」" }
    $li.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    try { $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse() } catch { }
    Settle
}
function Texts { All $CT::Text | ForEach-Object { $_.Current.Name } }
function Has-Text([string]$Fragment) { [bool](Texts | Where-Object { $_.Contains($Fragment) } | Select-Object -First 1) }
function Stat([string]$Label) {
    $t = @(Texts); $i = [array]::IndexOf($t, $Label)
    if ($i -lt 0) { throw "找不到統計標籤 $Label" }
    $t[$i + 1]
}
function Stats { [ordered]@{ 距離 = Stat '總距離'; 爬升 = Stat '累計爬升'; 下降 = Stat '累計下降'; 最高 = Stat '最高海拔'; 最低 = Stat '最低海拔' } }
function Rows {
    All $CT::ListItem | Where-Object { $_.Current.Name.StartsWith('SplitRow {') } | ForEach-Object {
        $o = [ordered]@{}
        foreach ($m in [regex]::Matches($_.Current.Name, '(\w+) = ([^,}]+)')) { $o[$m.Groups[1].Value] = $m.Groups[2].Value.Trim() }
        [pscustomobject]$o
    }
}
function Plans { All $CT::ListItem | Where-Object { -not $_.Current.Name.StartsWith('SplitRow {') } | ForEach-Object { $_.Current.Name } }
function Select-Plan([string]$Name) {
    $li = All $CT::ListItem | Where-Object { $_.Current.Name -eq $Name } | Select-Object -First 1
    if (-not $li) { throw "清單中沒有方案 $Name" }
    $li.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Settle
}
function Dialog-Input([string]$Button, [string]$Title, [string]$Text) {
    Click $Button
    $dlg = ByName $Title $CT::Window
    $edit = $dlg.FindFirst($TScope::Descendants, (Prop $A::ControlTypeProperty $CT::Edit))
    if ($Text) { $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Text) }
    $dlg.FindFirst($TScope::Descendants, (Prop $A::AutomationIdProperty 'PrimaryButton')).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Settle
}
function Use-FileDialog([string]$Button, [string]$Path) {
    Click $Button
    $h = [IntPtr]::Zero
    for ($i = 0; $i -lt 40 -and $h -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 250; $h = [E2EWin]::FindDialog([uint32]$script:AppPid) }
    if ($h -eq [IntPtr]::Zero) { throw '檔案對話框沒有出現' }
    Start-Sleep -Milliseconds 500
    $edit = [E2EWin]::FindFileNameEdit($h)
    [E2EWin]::SendMessage($edit, 0x000C, [IntPtr]::Zero, $Path) | Out-Null          # WM_SETTEXT
    [E2EWin]::SendMessage([E2EWin]::GetDlgItem($h, 1), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null  # BM_CLICK on Open/Save
    for ($i = 0; $i -lt 20 -and [E2EWin]::FindDialog([uint32]$script:AppPid) -ne [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 250 }
    Start-Sleep -Milliseconds 1200
}
function Import([string]$File) { Use-FileDialog '匯入 GPX…' (Join-Path $samples $File) }
function Shot([string]$Name) {
    $h = (Get-Process -Id $script:AppPid).MainWindowHandle
    $r = New-Object E2EWin+RECT; [E2EWin]::GetWindowRect($h, [ref]$r) | Out-Null
    $bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
    [E2EWin]::PrintWindow($h, $dc, 2) | Out-Null; $g.ReleaseHdc($dc)
    $bmp.Save((Join-Path $OutDir "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
}

# ---------- value helpers ----------
function Sec([string]$t) { $p = $t.Split(':') | ForEach-Object { [int]$_ }; $s = 0; foreach ($x in $p) { $s = $s * 60 + $x }; $s }
function Check-Consistent([string]$Id, $rows) {
    $sum = 0; $bad = @()
    foreach ($r in $rows) { $sum += Sec $r.Duration; if ($sum -ne (Sec $r.Cumulative)) { $bad += $r.Index } }
    Record $Id '各段用時累加 = 累計欄（每一列）' ($bad.Count -eq 0 -and @($rows).Count -gt 0) ("不一致列: " + ($(if ($bad.Count) { $bad -join ',' } else { '無' })))
}
function Snapshot { [pscustomobject]@{ Stats = ((Stats).Values -join ' / '); Target = (Get-Field '目標完賽時間'); Rows = ((Rows | ForEach-Object { "$($_.Index)|$($_.Pace)|$($_.Duration)|$($_.Cumulative)|$($_.AidStop)" }) -join ';') } }

# ================= run =================
$db = Join-Path $env:LOCALAPPDATA 'PacePlanner\plans.db'
$backup = Join-Path $env:TEMP ("plans.db.e2e-backup-" + [guid]::NewGuid().ToString('N'))
$hadDb = Test-Path $db
if ($hadDb) { Copy-Item $db $backup }

try {
    if ($hadDb) { Remove-Item $db }

    Case 'E01' '空資料庫啟動' {
        Start-App
        Record 'E01' '程式開啟且未崩潰' (App-Alive) "pid=$script:AppPid"
        Record 'E01' '自動建立「我的第一個方案」' ((Plans) -contains '我的第一個方案') ((Plans) -join ', ')
        Record 'E01' '資料庫建立於 %LocalAppData%\PacePlanner\plans.db' (Test-Path $db) $db
    }

    Case 'E02' '匯入一般賽道 river-park-10k.gpx' {
        Import 'river-park-10k.gpx'
        $s = Stats
        Record 'E02' '統計 10.00 km / 60 m / 60 m / 55 m / 10 m' ($s.距離 -eq '10.00 km' -and $s.爬升 -eq '60 m' -and $s.下降 -eq '60 m' -and $s.最高 -eq '55 m' -and $s.最低 -eq '10 m') ($s.Values -join ' / ')
        Shot 'E02-river-import'
    }

    Case 'E05' '匯入損毀/空檔/非 XML/非 GPX/無軌跡點' {
        $cases = @(
            @('invalid\empty.gpx', '檔案是空的'),
            @('invalid\not-xml.gpx', '不是有效的 XML'),
            @('invalid\truncated.gpx', '不是有效的 XML'),
            @('invalid\kml-file.gpx', '這不是 GPX 檔案'),
            @('invalid\no-track-points.gpx', '沒有任何軌跡點'))
        foreach ($c in $cases) {
            Import $c[0]
            $msg = Texts | Where-Object { $_.StartsWith('無法匯入') } | Select-Object -First 1
            Record 'E05' "$($c[0]) 顯示錯誤訊息" ([bool]$msg -and $msg.Contains($c[1])) $msg
            Record 'E05' "$($c[0]) 後程式仍存活、原賽道保留" ((App-Alive) -and (Stat '總距離') -eq '10.00 km') ("alive=$(App-Alive), 距離=$(Stat '總距離')")
            if ($c[0] -like '*truncated*') { Shot 'E05-invalid-gpx-error' }
        }
    }

    Case 'E06' '等速 10K 0:50:00' {
        Select-Combo 0 '10K'; Set-Field '目標完賽時間' '0:50:00'; Select-Combo 1 '等速'; Select-Combo 2 '無補給'
        $rows = @(Rows)
        Record 'E06' '共 10 段' ($rows.Count -eq 10) "段數=$($rows.Count)"
        Record 'E06' '每段配速相同 (5:00)' ((@($rows | Select-Object -ExpandProperty Pace -Unique)).Count -eq 1 -and $rows[0].Pace -eq '5:00') (($rows | Select-Object -ExpandProperty Pace -Unique) -join ',')
        Record 'E06' '最後累計 = 0:50:00' ($rows[-1].Cumulative -eq '0:50:00') $rows[-1].Cumulative
        Record 'E06' '配速以 分:秒 顯示' (@($rows | Where-Object { $_.Pace -notmatch '^\d+:\d\d$' }).Count -eq 0) $rows[0].Pace
        Check-Consistent 'E06' $rows
    }

    Case 'E07' '負分段 N=10' {
        Select-Combo 1 '負分段'; Set-Field '後半程每公里快幾秒' '10'
        $rows = @(Rows)
        $first = $rows[0..4] | ForEach-Object { Sec $_.Pace }; $second = $rows[5..9] | ForEach-Object { Sec $_.Pace }
        Record 'E07' '前半每段都慢於後半' (($first | Measure-Object -Minimum).Minimum -gt ($second | Measure-Object -Maximum).Maximum) ("前半=" + ($rows[0..4].Pace -join ',') + " 後半=" + ($rows[5..9].Pace -join ','))
        Record 'E07' '最後累計 = 0:50:00' ($rows[-1].Cumulative -eq '0:50:00') $rows[-1].Cumulative
        Check-Consistent 'E07' $rows
    }

    Case 'E08' '坡度調整' {
        Select-Combo 1 '坡度調整'
        $rows = @(Rows)
        $up = $rows | Where-Object { $_.Grade -eq '+3.0' } | Select-Object -First 1
        $flat = $rows | Where-Object { $_.Grade -eq '0.0' } | Select-Object -First 1
        $down = $rows | Where-Object { $_.Grade -eq '-3.0' } | Select-Object -First 1
        Record 'E08' '上坡 > 平路 > 下坡（配速秒數）' ((Sec $up.Pace) -gt (Sec $flat.Pace) -and (Sec $flat.Pace) -gt (Sec $down.Pace)) "上坡 $($up.Pace) / 平路 $($flat.Pace) / 下坡 $($down.Pace)"
        Record 'E08' '最後累計 = 0:50:00' ($rows[-1].Cumulative -eq '0:50:00') $rows[-1].Cumulative
        Check-Consistent 'E08' $rows
    }

    Case 'E09' '補給：每 2.5 km 停 30 秒' {
        Select-Combo 2 '每 N 公里'; Set-Field '每隔幾公里' '2.5'; Set-Field '每站停留秒數' '30'
        $rows = @(Rows)
        $aid = @($rows | Where-Object { $_.AidStop -ne '0' } | ForEach-Object { "$($_.Index):$($_.AidStop)" })
        Record 'E09' '第 3、5、8 段各停 30 秒' (($aid -join ',') -eq '3:30,5:30,8:30') ($aid -join ',')
        Record 'E09' '最後累計仍 = 0:50:00' ($rows[-1].Cumulative -eq '0:50:00') $rows[-1].Cumulative
        Check-Consistent 'E09' $rows
        Shot 'E09-aid-every-2.5km'
    }

    Case 'E10' '補給：指定 3, 7.5' {
        Select-Combo 2 '指定公里數'; Set-Field '補給站公里數' '3, 7.5'
        $rows = @(Rows)
        $aid = @($rows | Where-Object { $_.AidStop -ne '0' } | ForEach-Object { $_.Index })
        Record 'E10' '第 3、8 段有停留' (($aid -join ',') -eq '3,8') ($aid -join ',')
        Record 'E10' '最後累計 = 0:50:00' ($rows[-1].Cumulative -eq '0:50:00') $rows[-1].Cumulative
        Check-Consistent 'E10' $rows
    }

    Case 'E11' '目標時間格式錯誤' {
        Set-Field '目標完賽時間' 'abc'
        Record 'E11' '顯示格式錯誤訊息' (Has-Text '目標完賽時間格式錯誤') ((Texts | Where-Object { $_ -like '*格式*' }) -join ' | ')
        Record 'E11' '分段表清空且程式存活' (@(Rows).Count -eq 0 -and (App-Alive)) "段數=$(@(Rows).Count)"
        Set-Field '目標完賽時間' '0:50:00'
        Record 'E11' '改回正確值後恢復' (@(Rows).Count -eq 10) "段數=$(@(Rows).Count)"
    }

    Case 'E12' '補給清單格式錯誤' {
        Set-Field '補給站公里數' 'x'
        Record 'E12' '顯示公里數無效訊息' (Has-Text '補給站公里數「x」無效') ((Texts | Where-Object { $_ -like '*補給站公里數*' }) -join ' | ')
        Set-Field '補給站公里數' '3, 7.5'
    }

    Case 'E13' '自訂距離 12 km' {
        Select-Combo 0 '自訂'
        Record 'E13' '自訂距離預設帶入 GPX 實測距離' ((Get-Field '自訂距離') -eq '10') (Get-Field '自訂距離')
        Set-Field '自訂距離' '12'
        Record 'E13' '共 12 段' (@(Rows).Count -eq 12) "段數=$(@(Rows).Count)"
        Record 'E13' '摘要顯示等比例縮放' (Has-Text '已等比例縮放為 12 km') ((Texts | Where-Object { $_ -like '*縮放*' }) -join ' | ')
        Select-Combo 0 '10K'
        Shot 'E13-main-after-settings'
    }

    Case 'E14' '方案改名 / 複製 / 新增 / 刪除' {
        Dialog-Input '重新命名' '重新命名方案' 'E2E 河濱 10K'
        Record 'E14' '改名後清單與標題更新' ((Plans) -contains 'E2E 河濱 10K' -and (Has-Text 'E2E 河濱 10K') -and -not ((Plans) -contains '我的第一個方案')) ((Plans) -join ', ')
        Dialog-Input '複製' '複製方案' 'E2E 複本'
        $copyRows = (Snapshot).Rows
        Select-Plan 'E2E 河濱 10K'
        Record 'E14' '複本出現且內容與原方案相同' ((Plans) -contains 'E2E 複本' -and $copyRows -eq (Snapshot).Rows) ((Plans) -join ', ')
        Dialog-Input '新增' '新增方案' 'E2E 刪除用'
        Record 'E14' '新增方案出現' ((Plans) -contains 'E2E 刪除用') ((Plans) -join ', ')
        Select-Plan 'E2E 刪除用'
        Dialog-Input '刪除' '刪除方案' $null
        Record 'E14' '刪除後從清單消失' (-not ((Plans) -contains 'E2E 刪除用')) ((Plans) -join ', ')
    }

    Case 'E15' '多 trkseg 賽道（新方案）' {
        Dialog-Input '新增' '新增方案' 'E2E 多段'
        Import 'multi-segment-5k.gpx'
        $s = Stats
        Record 'E03' '多段統計 4.80 km / 42 m / 18 m / 35 m / 5 m' ($s.距離 -eq '4.80 km' -and $s.爬升 -eq '42 m' -and $s.下降 -eq '18 m' -and $s.最高 -eq '35 m' -and $s.最低 -eq '5 m') ($s.Values -join ' / ')
        Select-Combo 0 '5K'; Set-Field '目標完賽時間' '0:25:00'
        $rows = @(Rows)
        Record 'E15' '5K 共 5 段，最後累計 0:25:00' ($rows.Count -eq 5 -and $rows[-1].Cumulative -eq '0:25:00') "段數=$($rows.Count), 最後=$($rows[-1].Cumulative)"
    }

    Case 'E04' '雜訊賽道與無海拔賽道' {
        Dialog-Input '新增' '新增方案' 'E2E 雜訊與無海拔'
        Import 'noisy-gps-3k.gpx'
        $s = Stats
        Record 'E04' '雜訊檔 3.00 km / 爬升 30 m / 下降 0 m' ($s.距離 -eq '3.00 km' -and $s.爬升 -eq '30 m' -and $s.下降 -eq '0 m') ($s.Values -join ' / ')
        Import 'no-elevation-5k.gpx'
        $s = Stats
        Record 'E04' '無海拔檔 5.00 km，海拔顯示無資料' ($s.距離 -eq '5.00 km' -and $s.最高 -eq '無資料' -and $s.最低 -eq '無資料') ($s.Values -join ' / ')
        Record 'E04' '顯示沒有海拔資料提示' (Has-Text '此 GPX 沒有海拔資料') ''
        Shot 'E04-no-elevation'
    }

    Case 'E17' '匯出 CSV' {
        Select-Plan 'E2E 河濱 10K'
        $csv = Join-Path $OutDir 'e2e-export.csv'
        Use-FileDialog '匯出 CSV' $csv
        Record 'E17' 'CSV 檔已建立' (Test-Path $csv) $csv
        $bytes = [IO.File]::ReadAllBytes($csv)
        Record 'E17' 'UTF-8 BOM 開頭' ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) (($bytes[0..2] | ForEach-Object { '{0:X2}' -f $_ }) -join ' ')
        $lines = [IO.File]::ReadAllLines($csv, [Text.Encoding]::UTF8)
        Record 'E17' '標題 + 10 段 = 11 列' ($lines.Count -eq 11) "列數=$($lines.Count)"
        $xl = $null
        try { $xl = New-Object -ComObject Excel.Application } catch { }
        if ($xl) {
            try {
                $xl.Visible = $false; $xl.DisplayAlerts = $false
                $wb = $xl.Workbooks.Open($csv); $ws = $wb.Worksheets.Item(1)
                $a1 = $ws.Range('A1').Text; $f1 = $ws.Range('F1').Text; $j1 = $ws.Range('J1').Text; $j11 = $ws.Range('J11').Text
                $wb.Close($false)
                Record 'E17' 'Excel 開啟中文不亂碼' ($a1 -eq '段' -and $f1 -eq '目標配速(分:秒/km)' -and $j1 -eq '累計時間') "A1=$a1, F1=$f1, J1=$j1"
                Record 'E17' 'Excel 讀到最後累計 0:50:00' ($j11 -match '^0?0:50:00$') "J11=$j11"
            } finally { $xl.Quit(); [void][Runtime.InteropServices.Marshal]::ReleaseComObject($xl) }
        } else { Skip 'E17' 'Excel 開啟檢查' '此電腦沒有 Excel' }
    }

    Case 'E18' '匯出列印版 HTML' {
        $html = Join-Path $OutDir 'e2e-export.html'
        Use-FileDialog '匯出列印版' $html
        Record 'E18' 'HTML 檔已建立' (Test-Path $html) $html
        $edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($edge) {
            $pdf = Join-Path $OutDir 'e2e-export.pdf'
            Start-Process -FilePath $edge -ArgumentList @('--headless=new', '--disable-gpu', '--no-pdf-header-footer', "--user-data-dir=$OutDir\edge-profile", "--print-to-pdf=$pdf", $html) -Wait -WindowStyle Hidden
            $raw = [IO.File]::ReadAllText($pdf, [Text.Encoding]::GetEncoding(28591))
            $pages = ([regex]::Matches($raw, '/Type\s*/Page[^s]')).Count
            $box = [regex]::Match($raw, '/MediaBox\s*\[([^\]]+)\]').Groups[1].Value.Trim()
            Record 'E18' '列印為 1 頁' ($pages -eq 1) "頁數=$pages"
            Record 'E18' '紙張為 A4 直式 (595x842 pt)' ($box -match '^0 0 59[45]\.\d* 84[12]\.\d*$') "MediaBox=[$box]"
            Start-Process -FilePath $edge -ArgumentList @('--headless=new', '--disable-gpu', '--hide-scrollbars', "--user-data-dir=$OutDir\edge-profile", "--screenshot=$OutDir\E18-print-preview.png", '--window-size=794,1123', $html) -Wait -WindowStyle Hidden
        } else { Skip 'E18' 'PDF 頁數檢查' '找不到 Edge' }
    }

    Case 'E19' '自動儲存與重開後內容不變' {
        $before = @{}
        foreach ($name in (Plans)) { Select-Plan $name; $before[$name] = Snapshot }
        Select-Plan 'E2E 河濱 10K'
        Set-Field '目標完賽時間' '0:49:00'      # unsaved edit; must be auto-saved on close
        $before['E2E 河濱 10K'] = Snapshot
        $namesBefore = (Plans | Sort-Object) -join ', '
        Close-App
        Record 'E19' '關閉視窗正常結束' (-not (App-Alive)) ''
        Start-App
        $namesAfter = (Plans | Sort-Object) -join ', '
        Record 'E19' '重開後方案清單相同' ($namesBefore -eq $namesAfter) $namesAfter
        foreach ($name in $before.Keys) {
            Select-Plan $name
            $now = Snapshot; $old = $before[$name]
            Record 'E19' "「$name」統計/目標/分段表與關閉前相同" ($now.Stats -eq $old.Stats -and $now.Target -eq $old.Target -and $now.Rows -eq $old.Rows) "目標=$($now.Target), 統計=$($now.Stats)"
        }
        Select-Plan 'E2E 河濱 10K'
        Record 'E19' '未按儲存的修改 (0:49:00) 已自動儲存' ((Get-Field '目標完賽時間') -eq '0:49:00') (Get-Field '目標完賽時間')
        Shot 'E19-after-restart'
    }
}
finally {
    Get-Process PacePlanner -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    if (Test-Path $db) { Copy-Item $db (Join-Path $OutDir 'e2e-plans.db') -Force; Remove-Item $db }
    if ($hadDb) { Move-Item $backup $db }
}

$pass = @($script:Results | Where-Object Status -eq 'PASS').Count
$fail = @($script:Results | Where-Object Status -eq 'FAIL').Count
$skip = @($script:Results | Where-Object Status -eq 'SKIP').Count
$md = @("# E2E 結果", "", "執行時間：$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')　PASS $pass / FAIL $fail / SKIP $skip", "", "| ID | 檢查 | 結果 | 實際值 |", "|---|---|---|---|")
$md += $script:Results | ForEach-Object { "| $($_.Id) | $($_.Check) | $($_.Status) | $(($_.Actual -replace '\|', '/') -replace "`r?`n", ' ') |" }
[IO.File]::WriteAllLines((Join-Path $OutDir 'results.md'), $md, (New-Object Text.UTF8Encoding $true))
Write-Host ""
Write-Host "PASS $pass / FAIL $fail / SKIP $skip  ->  $(Join-Path $OutDir 'results.md')"
exit $fail
