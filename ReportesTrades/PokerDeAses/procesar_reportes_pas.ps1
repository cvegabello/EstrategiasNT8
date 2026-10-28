$folder = "C:\Users\carlo\Documents\NinjaTrader 8\bin\Custom\ReportesTrades\PokerDeAses"
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false

$allTrades = @()

$files = Get-ChildItem -Path $folder -Filter "*.xlsx" | Where-Object { 
    $_.Name -notlike "*Tabla_Maestra*" -and $_.Name -notlike "~$*" -and $_.Name -notlike "*test*" 
}

Write-Host "Encontrados $($files.Count) archivo(s) para procesar en PokerDeAses..."

foreach ($file in $files) {
    Write-Host "Procesando archivo: $($file.Name)"
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
                        $textVal = $dt.ToString("MM/dd/yyyy HH:mm:ss")
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
        
        # Clasificar As Simplificado para agrupaciones fáciles
        $entryName = $item["Entry name"]
        $asName = "Otro"
        if ($entryName -match "Z1|Zona1") {
            $asName = "As 1: Zona 1 (100B)"
        } elseif ($entryName -match "SOBB") {
            $asName = "As 2: SOBB"
        } elseif ($entryName -match "Ratchet") {
            $asName = "As 3: Ratchet"
        } elseif ($entryName -match "Cascada") {
            $asName = "As 4: Cascada 25"
        }

        # Extraer valor numérico puro de Profit para cálculos matemáticos
        $profitRaw = [string]$item["Profit"]
        $profitClean = $profitRaw -replace '[\$,\s]', ''
        $profitNum = 0.0
        if ($profitClean -match '^\((.*)\)$') {
            $profitNum = -[double]$matches[1]
        } elseif (-not [string]::IsNullOrWhiteSpace($profitClean)) {
            try { $profitNum = [double]$profitClean } catch { $profitNum = 0.0 }
        }

        $enriched = [ordered]@{
            "ArchivoOrigen" = $file.Name
            "Fecha"         = $fecha
            "As"            = $asName
            "ProfitNumerico"= $profitNum
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

Write-Host "Total trades acumulados de Poker de Ases: $($allTrades.Count)"

if ($allTrades.Count -eq 0) {
    Write-Host "No se encontraron trades para procesar."
    $excel.Quit()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($excel) | Out-Null
    exit
}

# 1. Guardar CSV Maestro PAS
$csvPath = Join-Path $folder "Tabla_Maestra_PAS.csv"
$allTrades | Export-Csv -Path $csvPath -NoTypeInformation -Encoding UTF8
Write-Host "CSV Maestro guardado con éxito: $csvPath"

# 2. Guardar Excel Maestro Consolidado PAS
$xlsxPath = Join-Path $folder "Tabla_Maestra_PAS.xlsx"
if (Test-Path $xlsxPath) { 
    try { Remove-Item $xlsxPath -Force } catch {} 
}

$masterWb = $excel.Workbooks.Add()

# -------------------------------------------------------------
# Hoja 1: Base_Trades
# -------------------------------------------------------------
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

# -------------------------------------------------------------
# Hoja 2: Resumen_Diario
# -------------------------------------------------------------
$dailyWs = $masterWb.Sheets.Add([System.Reflection.Missing]::Value, $detailWs)
$dailyWs.Name = "Resumen_Diario"

$dailyHeaders = @("Fecha", "Trades Totales", "Trades Ganadores", "Trades Perdedores", "Win Rate %", "PnL Neto Total ($)")
for ($c = 1; $c -le $dailyHeaders.Count; $c++) {
    $dailyWs.Cells.Item(1, $c).Formula = $dailyHeaders[$c - 1]
}
$dailyWs.Range("A1:F1").Font.Bold = $true

$dayGroups = $allTrades | Group-Object Fecha
$dRow = 2
foreach ($dg in $dayGroups) {
    $fecha = [string]$dg.Name
    $totalTrades = $dg.Count
    $wins = ($dg.Group | Where-Object { $_.ProfitNumerico -gt 0 }).Count
    $losses = ($dg.Group | Where-Object { $_.ProfitNumerico -lt 0 }).Count
    $winRate = if ($totalTrades -gt 0) { [math]::Round(($wins / $totalTrades) * 100, 1) } else { 0.0 }
    $pnlNeto = ($dg.Group | Measure-Object -Property ProfitNumerico -Sum).Sum

    $dailyWs.Cells.Item($dRow, 1).Formula = $fecha
    $dailyWs.Cells.Item($dRow, 2).Formula = [string]$totalTrades
    $dailyWs.Cells.Item($dRow, 3).Formula = [string]$wins
    $dailyWs.Cells.Item($dRow, 4).Formula = [string]$losses
    $dailyWs.Cells.Item($dRow, 5).Formula = "$winRate%"
    $dailyWs.Cells.Item($dRow, 6).Formula = [string][math]::Round($pnlNeto, 2)
    $dRow++
}
$dailyWs.Columns.AutoFit() | Out-Null

# -------------------------------------------------------------
# Hoja 3: Resumen_Por_As
# -------------------------------------------------------------
$asWs = $masterWb.Sheets.Add([System.Reflection.Missing]::Value, $dailyWs)
$asWs.Name = "Resumen_Por_As"

$asHeaders = @("As / Setup", "Trades Totales", "Trades Ganadores", "Trades Perdedores", "Win Rate %", "PnL Neto Total ($)")
for ($c = 1; $c -le $asHeaders.Count; $c++) {
    $asWs.Cells.Item(1, $c).Formula = $asHeaders[$c - 1]
}
$asWs.Range("A1:F1").Font.Bold = $true

$asGroups = $allTrades | Group-Object As
$aRow = 2
foreach ($ag in $asGroups) {
    $asName = [string]$ag.Name
    $totalTrades = $ag.Count
    $wins = ($ag.Group | Where-Object { $_.ProfitNumerico -gt 0 }).Count
    $losses = ($ag.Group | Where-Object { $_.ProfitNumerico -lt 0 }).Count
    $winRate = if ($totalTrades -gt 0) { [math]::Round(($wins / $totalTrades) * 100, 1) } else { 0.0 }
    $pnlNeto = ($ag.Group | Measure-Object -Property ProfitNumerico -Sum).Sum

    $asWs.Cells.Item($aRow, 1).Formula = $asName
    $asWs.Cells.Item($aRow, 2).Formula = [string]$totalTrades
    $asWs.Cells.Item($aRow, 3).Formula = [string]$wins
    $asWs.Cells.Item($aRow, 4).Formula = [string]$losses
    $asWs.Cells.Item($aRow, 5).Formula = "$winRate%"
    $asWs.Cells.Item($aRow, 6).Formula = [string][math]::Round($pnlNeto, 2)
    $aRow++
}
$asWs.Columns.AutoFit() | Out-Null

$masterWb.SaveAs($xlsxPath)
$masterWb.Close($true)

$excel.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($detailWs) | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($dailyWs) | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($asWs) | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($masterWb) | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($excel) | Out-Null
[GC]::Collect()
[GC]::WaitForPendingFinalizers()

Write-Host "Excel Maestro Poker de Ases generado con éxito: $xlsxPath"
