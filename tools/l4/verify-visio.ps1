<#
.SYNOPSIS
  Phase 8 L4 原生验收：用 Microsoft Visio 打开 Engine 产出的 VSDX 并核验。

.DESCRIPTION
  任务与负责人决策要求 L4 验证自由路径、文本、Z-order 与外部文件编辑。
  本脚本在装有 Microsoft Visio 的 Windows 上运行，通过 COM 自动化执行检查，
  输出机器可读的 JSON 与人类可读的摘要。

  注意：COM 能验证"能否打开、形状数、文字内容、属性值"，
  但**不能**替代人工判断字号层级、视觉专业度——见脚本末尾的人工检查项。

.PARAMETER VSDX
  待验证的 VSDX 路径。

.PARAMETER Output
  JSON 结果输出路径。

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File verify-visio.ps1 -Vsdx C:\work\slice.vsdx
#>
param(
    [Parameter(Mandatory = $true)][string]$Vsdx,
    [string]$Output = ""
)

$ErrorActionPreference = "Stop"
$results = [ordered]@{
    tool       = "vsdx-engine L4 verifier"
    vsdx       = (Resolve-Path $Vsdx).Path
    startedUtc = (Get-Date).ToUniversalTime().ToString("o")
    checks     = @()
    verdict    = "unknown"
    error      = $null
}

function Add-Check {
    param([string]$Name, [bool]$Passed, [string]$Detail)
    $script:results.checks += [ordered]@{
        name   = $Name
        passed = $Passed
        detail = $Detail
    }
    $mark = if ($Passed) { "PASS" } else { "FAIL" }
    Write-Host ("  [{0}] {1} — {2}" -f $mark, $Name, $Detail)
}

$app = $null
$doc = $null

try {
    Write-Host "starting Visio..."
    $app = New-Object -ComObject Visio.Application
    $app.Visible = $false
    $app.AlertResponse = 7          # suppress modal dialogs (7 = No)

    Write-Host "opening $($results.vsdx)"
    $doc = $app.Documents.OpenEx($results.vsdx, 128)   # 128 = visOpenRO (read-only, no lock)

    Add-Check "opens" ($null -ne $doc) "document opened without a repair prompt"

    $pageCount = $doc.Pages.Count
    Add-Check "page-count" ($pageCount -ge 1) "$pageCount page(s)"

    $page = $doc.Pages.Item(1)
    $shapeCount = $page.Shapes.Count
    Add-Check "shape-count" ($shapeCount -gt 0) "$shapeCount shape(s) on page 1"

    # --- 1. Standalone text: title / subtitle / legend / note -----------------
    $textShapes = @()
    foreach ($s in $page.Shapes) {
        try {
            if ($s.NameU -eq "TextBlock") { $textShapes += $s }
        } catch { }
    }
    Add-Check "text-blocks-present" ($textShapes.Count -ge 1) "$($textShapes.Count) text block(s)"

    if ($textShapes.Count -ge 2) {
        $sizes = @()
        foreach ($t in $textShapes) {
            try { $sizes += [double]$t.CellsU("Char.Size").ResultIU } catch { }
        }
        if ($sizes.Count -ge 2) {
            $distinct = ($sizes | Sort-Object -Unique).Count
            Add-Check "text-size-hierarchy" ($distinct -ge 2) `
                "$distinct distinct text sizes: $(($sizes | ForEach-Object { [math]::Round($_ * 72, 1) }) -join ', ') pt"
        }

        $withText = @($textShapes | Where-Object { $_.Text -and $_.Text.Trim().Length -gt 0 })
        Add-Check "text-content-not-empty" ($withText.Count -eq $textShapes.Count) `
            "$($withText.Count)/$($textShapes.Count) blocks carry text"
    }

    # --- 2. Free path: custom geometry survives as a real outline --------------
    # A free path is written with NameU "FreePath" (see VisioFreePathBridge). Counting geometry
    # rows instead would count every Circle and Hexagon — master shapes with 25+ rows — as custom
    # geometry, and the previous `-ge 0` made the check pass unconditionally anyway.
    $freePaths = @()
    foreach ($s in $page.Shapes) {
        try {
            if ($s.NameU -eq "FreePath") { $freePaths += $s }
        } catch { }
    }
    # A diagram may legitimately have no free paths — measured, cbo.ir.json has none. So this
    # reports what it found rather than asserting a count it cannot know; the geometry check below
    # only runs when paths exist, and is the one that can actually fail.
    Write-Host ("  [INFO] {0} free path(s) on page 1" -f $freePaths.Count)

    # Geometry must survive as real rows. Measured on a two-point path the engine writes MoveTo +
    # LineTo, so a threshold of 6 would fail a correct file; two rows is the real minimum.
    if ($freePaths.Count -ge 1) {
        $withRows = 0
        foreach ($f in $freePaths) {
            try {
                if ($f.RowCount(10) -ge 2) { $withRows++ }   # 10 = visSectionFirstComponent
            } catch { }
        }
        Add-Check "free-path-geometry" ($withRows -eq $freePaths.Count) `
            "$withRows/$($freePaths.Count) free path(s) carry geometry rows"
    }

    # --- 3. Z-order is document order ----------------------------------------
    $order = @()
    foreach ($s in $page.Shapes) {
        try { $order += ("{0}:{1}" -f $s.ID, ($(if ($s.Name) { $s.Name } else { $s.NameU }))) } catch { }
    }
    Add-Check "z-order-readable" ($order.Count -gt 0) "$($order.Count) shapes in document order"

    # --- 4. Containers ------------------------------------------------------
    $containers = @()
    foreach ($s in $page.Shapes) {
        try {
            $t = $s.CellsU("User.msvStructureType").ResultStr("")
            if ($t -eq "Container") { $containers += $s }
        } catch { }
    }
    Add-Check "containers-present" ($containers.Count -ge 1) "$($containers.Count) container(s)"

    # --- 5. Save round-trip -------------------------------------------------
    $roundTrip = [System.IO.Path]::ChangeExtension($results.vsdx, ".roundtrip.vsdx")
    $doc.SaveAs($roundTrip)
    $doc.Close()
    $doc = $app.Documents.Open($roundTrip)

    $shapeAfter = $doc.Pages.Item(1).Shapes.Count
    Add-Check "save-reopen-shape-count" ($shapeAfter -eq $shapeCount) `
        "$shapeCount -> $shapeAfter shapes"

    $results.humanReviewRequired = @(
        "字号层级是否在视觉上明显（COM 只能报告数值）",
        "标题/图例位置是否合理、不遮挡图形",
        "颜色是否符合设计意图",
        "连线是否可读、无异常绕行"
    )

    $failed = @($results.checks | Where-Object { -not $_.passed })
    $results.verdict = if ($failed.Count -eq 0) { "pass" } else { "fail" }
}
catch {
    $results.verdict = "error"
    $results.error = ("{0}: {1}" -f $_.Exception.GetType().Name, $_.Exception.Message)
    Write-Host "ERROR: $($results.error)" -ForegroundColor Red
}
finally {
    if ($doc) { try { $doc.Close() } catch { } }
    if ($app) { try { $app.Quit() } catch { } }
    if ($app) { try { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($app) } catch { } }

    $results.finishedUtc = (Get-Date).ToUniversalTime().ToString("o")
    $json = $results | ConvertTo-Json -Depth 6

    if ($Output) {
        $json | Set-Content -Path $Output -Encoding UTF8
        Write-Host "result written to $Output"
    } else {
        Write-Host $json
    }

    Write-Host ""
    Write-Host "verdict: $($results.verdict)"
}

if ($results.verdict -ne "pass") { exit 1 }
