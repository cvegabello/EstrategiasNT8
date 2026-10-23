$folder = "C:\Users\carlo\Documents\NinjaTrader 8\bin\Custom\ReportesTrades"
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false

$allTrades = @()

$files = Get-ChildItem -Path $folder -Filter "*.xlsx" | Where-Object { $_.Name -notlike "*Tabla_Maestra*" }

foreach ($file in $files) {
    Write-Host "Leyendo: $($file.Name)"
    $wb = $excel.Workbooks.Open($file.FullName)
    $ws = $wb.Sheets.Item(1)
    
    $row = 1
    $headers = @()
    $col = 1
    while ($true) {
        $val = $ws.Cells.Item($row, $col).Text
        if ([string]::IsNullOrWhiteSpace($val)) { break }
        $headers += $val.Trim()
        $col++
    }
    
    $row = 2
    while ($true) {
        $firstCell = $ws.Cells.Item($row, 1).Text
        if ([string]::IsNullOrWhiteSpace($firstCell)) { break }
        
        $item = [ordered]@{}
        for ($c = 1; $c -le $headers.Count; $c++) {
            $h = $headers[$c - 1]
            $v = $ws.Cells.Item($row, $c).Text
            $item[$h] = $v
        }
        
        # Extraer Fecha limpia de 'Entry time'
        $entryTimeStr = $item["Entry time"]
        $fecha = ""
        if (-not [string]::IsNullOrWhiteSpace($entryTimeStr)) {
            if ($entryTimeStr -match "(\d{1,2}/\d{1,2}/\d{4})") {
                $fecha = $matches[1]
            } elseif ($entryTimeStr -match "(\d{4}-\d{2}-\d{2})") {
                $fecha = $matches[1]
            } else {
                $fecha = $entryTimeStr.Split(' ')[0]
            }
        }
        
        $enriched = [ordered]@{
            "ArchivoOrigen" = $file.Name
            "Fecha"         = $fecha
        }
        foreach ($k in $item.Keys) {
            $enriched[$k] = $item[$k]
        }
        
        $allTrades += [PSCustomObject]$enriched
        $row++
    }
    
    $wb.Close($false)
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($ws) | Out-Null
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($wb) | Out-Null
}

Write-Host "Total trades acumulados: $($allTrades.Count)"

# 1. Exportar CSV Maestro (Siempre disponible para cualquier herramienta)
$csvPath = Join-Path $folder "Tabla_Maestra_Trades.csv"
$allTrades | Export-Csv -Path $csvPath -NoTypeInformation -Encoding UTF8
Write-Host "CSV Maestro guardado: $csvPath"

# 2. Generar Excel Maestro Oficial
$xlsxPath = Join-Path $folder "Tabla_Maestra_Trades.xlsx"
if (Test-Path $xlsxPath) { 
    try { Remove-Item $xlsxPath -Force } catch {} 
}

$masterWb = $excel.Workbooks.Add()
$detailWs = $masterWb.Sheets.Item(1)
$detailWs.Name = "Base_Trades"

$cols = $allTrades[0].PSObject.Properties.Name
for ($c = 1; $c -le $cols.Count; $c++) {
    $detailWs.Cells.Item(1, $c).Formula = [string]$cols[$c - 1]
    $detailWs.Cells.Item(1, $c).Font.Bold = $true
}

for ($r = 0; $r -lt $allTrades.Count; $r++) {
    $rowIdx = $r + 2
    $trade = $allTrades[$r]
    for ($c = 1; $c -le $cols.Count; $c++) {
        $propName = $cols[$c - 1]
        $val = [string]$trade.$propName
        $detailWs.Cells.Item($rowIdx, $c).Formula = $val
    }
}

$detailWs.Columns.AutoFit() | Out-Null

# 3. Hoja Resumen por Setup
$summaryWs = $masterWb.Sheets.Add([System.Reflection.Missing]::Value, $detailWs)
$summaryWs.Name = "Resumen_Setups"

$summaryWs.Cells.Item(1, 1).Formula = "Fecha"
$summaryWs.Cells.Item(1, 2).Formula = "Estrategia"
$summaryWs.Cells.Item(1, 3).Formula = "Setup (Entry Name)"
$summaryWs.Cells.Item(1, 4).Formula = "Trades"
$summaryWs.Cells.Item(1, 5).Formula = "Profit Neto Total"
$summaryWs.Range("A1:E1").Font.Bold = $true

$groups = $allTrades | Group-Object Fecha, Strategy, "Entry name"
$sRow = 2
foreach ($g in $groups) {
    $fecha = [string]$g.Values[0]
    $strat = [string]$g.Values[1]
    $setup = [string]$g.Values[2]
    $count = [string]$g.Count
    $sum = ($g.Group | ForEach-Object {
        $c = $_.Profit -replace '[\$,\s]', ''
        if ($c -match '^\((.*)\)$') { -[double]$matches[1] } else { [double]$c }
    } | Measure-Object -Sum).Sum
    $sumStr = [string][math]::Round($sum, 2)

    $summaryWs.Cells.Item($sRow, 1).Formula = $fecha
    $summaryWs.Cells.Item($sRow, 2).Formula = $strat
    $summaryWs.Cells.Item($sRow, 3).Formula = $setup
    $summaryWs.Cells.Item($sRow, 4).Formula = $count
    $summaryWs.Cells.Item($sRow, 5).Formula = $sumStr
    $sRow++
}

$summaryWs.Columns.AutoFit() | Out-Null

$masterWb.SaveAs($xlsxPath)
$masterWb.Close($true)

$excel.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($detailWs) | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($summaryWs) | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($masterWb) | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($excel) | Out-Null
[GC]::Collect()
[GC]::WaitForPendingFinalizers()

Write-Host "Excel Maestro listo en: $xlsxPath"
