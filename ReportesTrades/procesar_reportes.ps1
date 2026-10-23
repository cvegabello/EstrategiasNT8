$folder = "C:\Users\carlo\Documents\NinjaTrader 8\bin\Custom\ReportesTrades"
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false

$allTrades = @()

$files = Get-ChildItem -Path $folder -Filter "*.xlsx" | Where-Object { $_.Name -notlike "*Tabla_Maestra*" -and $_.Name -notlike "*test*" }

foreach ($file in $files) {
    Write-Host "Procesando: $($file.Name)"
    $wb = $excel.Workbooks.Open($file.FullName)
    $ws = $wb.Sheets.Item(1)
    
    # AutoAjustar columnas para que nunca devuelva '###'
    $ws.Columns.AutoFit() | Out-Null
    
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
            $cell = $ws.Cells.Item($row, $c)
            $textVal = [string]$cell.Text
            $val2 = $cell.Value2

            # Si es fecha/hora o si el texto vino como '###', convertir desde OADate numérico
            if ($h -in @("Entry time", "Exit time") -or $textVal -match '^#+$') {
                if ($val2 -is [double] -or $val2 -is [int]) {
                    try {
                        $dt = [DateTime]::FromOADate([double]$val2)
                        $textVal = $dt.ToString("MM/dd/yyyy HH:mm")
                    } catch {}
                }
            }
            
            $item[$h] = $textVal
        }
        
        # Extraer Fecha limpia (MM/dd/yyyy)
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
        
        if ([string]::IsNullOrWhiteSpace($fecha) -or $fecha -match '^#+$') {
            if ($file.Name -match "(\d{2})(\d{2})(\d{4})") {
                $fecha = "$($matches[1])/$($matches[2])/$($matches[3])"
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

# 1. Guardar CSV Maestro
$csvPath = Join-Path $folder "Tabla_Maestra_Trades.csv"
$allTrades | Export-Csv -Path $csvPath -NoTypeInformation -Encoding UTF8
Write-Host "CSV Maestro guardado con éxito: $csvPath"

# 2. Guardar Excel Maestro Consolidado
$xlsxPath = Join-Path $folder "Tabla_Maestra_Consolidada.xlsx"
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

# También intentar actualizar Tabla_Maestra_Trades.xlsx si no está bloqueado
$legacyPath = Join-Path $folder "Tabla_Maestra_Trades.xlsx"
try { Copy-Item $xlsxPath $legacyPath -Force } catch {}

$excel.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($detailWs) | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($summaryWs) | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($masterWb) | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($excel) | Out-Null
[GC]::Collect()
[GC]::WaitForPendingFinalizers()

# Limpiar temporales
Remove-Item (Join-Path $folder "test_*.ps1") -Force -ErrorAction SilentlyContinue

Write-Host "Excel Maestro Consolidado generado exitosamente: $xlsxPath"
