# Bitácora de Pruebas y Desempeño - Versión 8.0 (Cascada Vega ES)

Esta bitácora documenta las pruebas comparativas A/B y el rendimiento histórico en NinjaTrader 8 Playback sobre futuros del S&P 500 (ES / MES) en gráfica de **610 Ticks**, evaluando la arquitectura **Versión 8.0**.

---

## 1. Novedades y Candados de la Versión 8.0
* **Rescate de Contratos en Tranca Universal (Bugfix v8.0):** Cuando la Tranca Universal se activa por ganancia flotante y retroceso, **únicamente liquida el Contrato 1 (`Exit_Ratchet_C1_Tranca`)**. Los Contratos 2 y 3 quedan 100% blindados para buscar sus techos/pisos de Banda 3.5 y Choke Parabólico, recuperando trades de más de +$1,800 USD.
* **Filtro A: Profundidad del Swing en Zona 1 (`RatchetMinSwingDepthTicks = 10`):** Exige que el Swing Low penetre al menos 10 ticks por debajo de `Lower 1.75` (y el Swing High 10 ticks sobre `Upper 1.75`), eliminando pivotes tibios pegados a la frontera de Zona 2.
* **Filtro B1: Separación de EMA 200 respecto al Canal Pequeño (`RatchetEmaMinSeparationTicks = 8`):** Exige que la EMA 200 esté al menos 8 ticks por fuera de las bandas 1.75, evitando compresión y picadillo central.
* **Filtro B2: Pendiente Normalizada de EMA 200 (`RatchetEmaMinSlopeThreshold = 0.15`):** Exige aceleración institucional direccional ($\ge +0.15$ para compras, $\le -0.15$ para ventas), descartando mercados planos con "miringas" de pendiente.
* **Chequeo de Salud / Asfixia Opcional:** `RatchetHealthCheckEnabled = False` por defecto, permitiendo al trader decidir si activa la póliza de asfixia en mercados picados.

---

## 2. Registro de Días de Playback Evaluados (v8.0 - Cascada 10 Min + Ratchet)

Periodo analizado: **19 de Octubre a 23 de Noviembre de 2026** (23 sesiones de trading completas).

| # | Archivo de Reporte | Fecha | Trades (Contratos) | PnL Neto Día | Desglose de Operaciones y Salidas |
| :---: | :---: | :---: | :---: | :---: | :--- |
| **1** | `PAS_10192026_Cascada10.xlsx` | 19/10/2026 | 3 | **-$305.52 USD** | Ratchet_Long -> Exit_Ratchet_SwingPrematuro (-$163.08)<br>Ratchet_Long -> Exit_Ratchet_PisoUpper175 (+$95.64)<br>Ratchet_Long -> Exit_Ratchet_SwingPrematuro (-$238.08) |
| **2** | `PAS_10202026_Cascada10.xlsx` | 20/10/2026 | 2 | **-$354.94 USD** | Ratchet_Long -> Exit_Ratchet_SlInicial (-$313.08)<br>Ratchet_Short -> Exit_Ratchet_SwingPrematuro (-$41.86) |
| **3** | `PAS_10212026_Cascada10.xlsx` | 21/10/2026 | 0 | **$0.00 USD** | *Sin operaciones:* Filtros de tendencia/EMA mantuvieron la cuenta 100% blindada en rango sucio. |
| **4** | `PAS_10222026_Cascada10.xlsx` | 22/10/2026 | 0 | **$0.00 USD** | *Sin operaciones:* Filtros descartaron compresión sin volumen direccional. |
| **5** | `PAS_10232026_Cascada10.xlsx` | 23/10/2026 | 1 | **+$136.92 USD** | Ratchet_Long -> Exit_Ratchet_SwingPrematuro (+$136.92) |
| **6** | `PAS_10262026_Cascada10.xlsx` | 26/10/2026 | 1 | **-$88.08 USD** | Ratchet_Long -> Exit_Ratchet_SwingPrematuro (-$88.08) |
| **7** | `PAS_10272026_Cascada10.xlsx` | 27/10/2026 | 3 | **+$674.42 USD** | Ratchet_Long -> Exit_Ratchet_C2_Upper35 (+$195.64)<br>Ratchet_Long -> Exit_Ratchet_C1_Tranca (+$195.64)<br>Ratchet_Long -> Exit_Ratchet_PisoUpper35 (+$283.14) |
| **8** | `PAS_10282026_Cascada10.xlsx` | 28/10/2026 | 0 | **$0.00 USD** | *Sin operaciones:* Filtros de calidad preservaron la cuenta intacta. |
| **9** | `PAS_11022026_Cascada10.xlsx` | 02/11/2026 | 4 | **+$632.56 USD** | Ratchet_Long -> Exit_Ratchet_C1_Tranca (+$208.14)<br>Ratchet_Long -> Exit_Ratchet_C2_Upper35 (+$258.14)<br>Ratchet_Long -> Exit_Ratchet_PisoUpper175 (-$4.36)<br>Ratchet_Short -> Exit_Ratchet_C1_Tranca (+$170.64) |
| **10** | `PAS_11032026_Cascada10.xlsx` | 03/11/2026 | 0 | **$0.00 USD** | *Sin operaciones:* Filtros de tendencia y estructura mantuvieron la cuenta protegida en rango sucio. |
| **11** | `PAS_11042026_Cascada10.xlsx` | 04/11/2026 | 3 | **+$336.34 USD** | Ratchet_Long -> Exit_Ratchet_SwingPrematuro (+$249.42)<br>Ratchet_Long -> Exit_Ratchet_C1_Tranca (+$120.64)<br>Ratchet_Long -> Exit_Ratchet_PisoMidline (-$33.72) |
| **12** | `PAS_11052026_Cascada10.xlsx` | 05/11/2026 | 4 | **+$1,382.56 USD** | Ratchet_Short -> Exit_Ratchet_C1_Tranca (+$195.64)<br>Ratchet_Short -> Exit_Ratchet_C2_Lower35 (+$508.14)<br>Ratchet_Short -> Exit_Ratchet_Choke4tk (+$808.14)<br>Ratchet_Short -> Cortacircuito_Diario_Piso_Exacto (-$129.36) |
| **13** | `PAS_11062026_Cascada10.xlsx` | 06/11/2026 | 0 | **$0.00 USD** | *Sin operaciones:* Filtros de compresión descartaron volatilidad desordenada. |
| **14** | `PAS_11092026_Cascada10.xlsx` | 09/11/2026 | 2 | **-$654.94 USD** | Ratchet_Long -> Exit_Ratchet_SwingPrematuro (-$500.58)<br>Ratchet_Long -> Exit_Ratchet_SlInicial (-$154.36) |
| **15** | `PAS_11102026_Cascada10.xlsx` | 10/11/2026 | 1 | **+$470.64 USD** | Cascada50_Long -> Exit_T1_TP_FrancoTirador (+$470.64) |
| **16** | `PAS_11112026_Cascada10.xlsx` | 11/11/2026 | 3 | **-$79.94 USD** | Ratchet_Long -> Exit_Ratchet_SwingPrematuro (-$33.72)<br>Ratchet_Long -> Exit_Ratchet_SwingPrematuro (-$29.36)<br>Ratchet_Long -> Exit_Ratchet_SwingPrematuro (-$16.86) |
| **17** | `PAS_11122026_Cascada10.xlsx` | 12/11/2026 | 3 | **+$1,374.42 USD** | Ratchet_Long -> Exit_Ratchet_C1_Tranca (+$220.64)<br>Ratchet_Long -> Exit_Ratchet_C2_Upper35 (+$458.14)<br>Ratchet_Long -> Exit_Ratchet_Choke4tk (+$695.64) |
| **18** | `PAS_11162026_Cascada10.xlsx` | 16/11/2026 | 4 | **-$151.16 USD** | Ratchet_Short -> Exit_Ratchet_C1_Tranca (+$220.64)<br>Ratchet_Short -> Exit_Ratchet_PisoLower175 (-$33.72)<br>Ratchet_Long -> Exit_Ratchet_C1_Tranca (+$95.64)<br>Ratchet_Long -> Exit_Ratchet_SlInicial (-$433.72) |
| **19** | `PAS_11172026_Cascada10.xlsx` | 17/11/2026 | 3 | **+$1,536.92 USD** | Ratchet_Short -> Exit_Ratchet_C1_Tranca (+$158.14)<br>Ratchet_Short -> Exit_Ratchet_C2_Lower35 (+$533.14)<br>Ratchet_Short -> Exit_Ratchet_Choke4tk (+$845.64) |
| **20** | `PAS_11182026_Cascada10.xlsx` | 18/11/2026 | 1 | **+$470.64 USD** | Cascada50_Long -> Exit_T1_TP_FrancoTirador (+$470.64) |
| **21** | `PAS_11192026_Cascada10.xlsx` | 19/11/2026 | 3 | **+$1,970.06 USD** | Ratchet_Long -> Exit_Ratchet_C1_Tranca (+$183.14)<br>Ratchet_Long -> Exit_Ratchet_Choke4tk (+$1,891.28)<br>Ratchet_Short -> Cortacircuito_Diario_Piso_Exacto (-$104.36) |
| **22** | `PAS_11202026_Cascada10.xlsx` | 20/11/2026 | 2 | **+$1,049.42 USD** | Ratchet_Short -> Exit_Ratchet_C1_Tranca (+$233.14)<br>Ratchet_Short -> Exit_Ratchet_PisoLower175 (+$816.28) |
| **23** | `PAS_11232026_Cascada10.xlsx` | 23/11/2026 | 4 | **+$3,245.06 USD** | Ratchet_Long -> Exit_Ratchet_C1_Tranca (+$358.14)<br>Ratchet_Long -> Exit_Ratchet_C2_Upper35 (+$1,320.64)<br>Ratchet_Long -> Exit_Ratchet_Choke4tk (+$1,708.14)<br>Ratchet_Long -> Cortacircuito_Diario_Piso_Exacto (-$141.86) |

---

## 3. Métricas Globales Consolidadas (v8.0)

* **Total de Sesiones Evaluadas:** 23 días operativos de mercado (más de un mes calendario de trading).
* **Sesiones Ganadoras (Días Verdes):** 12 (52.2%)
* **Sesiones Perdedoras (Días Rojos):** 6 (26.1%)
* **Sesiones Neutrales / Sin Operar (Días Flat):** 5 (21.7%) *(En más del 20% de los días la cuenta estuvo 100% protegida sin arriesgar un solo centavo)*.
* **Beneficio Neto Total:** **+$11,645.38 USD**
* **Ganancia Bruta (Gross Profit):** +$14,105.54 USD
* **Pérdida Bruta (Gross Loss):** -$2,460.16 USD
* **Profit Factor Institucional:** **5.73**
* **Promedio Neto por Sesión (Avg Day):** **+$506.32 USD / día**
* **Promedio Neto por Contrato (Avg Trade):** **+$247.77 USD / contrato**
* **Total Contratos Operados:** 47 contratos (30 ganadores / 17 perdedores, **63.8% Win Rate**).

### Desglose por Patrón de Entrada
1. **Cascada50_Long:** 2 trades | 2 wins (100% WR) | **+$941.28 USD**.
2. **Ratchet_Short:** 14 contratos | 10 wins / 4 losses (71.4% WR) | **+$4,180.24 USD**.
3. **Ratchet_Long:** 31 contratos | 18 wins / 13 losses (58.1% WR) | **+$6,523.86 USD**.

### Desglose por Mecanismo de Salida
* **`Exit_Ratchet_Choke4tk` (Ahogamiento C3):** 5 ejecuciones | 5 wins (100%) | **+$5,948.84 USD**.
* **`Exit_Ratchet_C1_Tranca` (Aseguramiento C1):** 12 ejecuciones | 12 wins (100%) | **+$2,360.18 USD** (100% de fiabilidad).
* **`Exit_Ratchet_C2_Lower35 / Upper35` (Salida C2 a Banda 3.5):** 6 ejecuciones | 6 wins (100%) | **+$3,273.84 USD**.
* **`Exit_T1_TP_FrancoTirador` (Cascada):** 2 ejecuciones | 2 wins (100%) | **+$941.28 USD**.
* **`Exit_Ratchet_PisoLower175 / Upper175 / Upper35`:** Resguardo en ganancias flotantes aseguradas.
* **`Exit_Ratchet_SwingPrematuro`:** 10 ejecuciones | 2 wins / 8 losses | **-$725.28 USD** (Cortó pérdidas pequeñas evitando males mayores).
* **`Exit_Ratchet_SlInicial`:** 3 ejecuciones | -$901.16 USD.
* **`Cortacircuito_Diario_Piso_Exacto`:** 3 ejecuciones | -$375.58 USD.

---

## 3. Notas Estratégicas y Puntos Abiertos
> [!IMPORTANT]
> **Consolidación Histórica Semana Nov 16 - Nov 20:**  
> La semana previa cerró con **5 de 5 días en verde** y más de **+$5,088 USD netos**. La versión 8.0 incorpora los filtros de calidad para blindar los días de choque como el 12 y 20 de noviembre sin afectar los corredores limpios.

---

## 4. Implementación y Congelamiento Oficial: Versión 8.1

Todas las mejoras y calibraciones analizadas a partir del consolidado de 23 sesiones fueron formalmente implementadas y congeladas en la **Versión 8.1 - Cascada Vega ES**:

1. **🎯 Liquidación Monocontrato / Desescalado en Banda 3.5 (Offset 0):** Implementado en `ManageAs3_Ratchet_Long` y `ManageAs3_Ratchet_Short`. Si `activeTradeContracts == 1` y toca Banda 3.5, liquida inmediatamente el contrato cobrando de +20 a +30 ticks en el banco sin flotar.
2. **📐 Filtro Anti-Persecución Dinámico Porcentual (`RatchetMaxSeparationPercent = 45.0%`):** Reemplazó los ticks fijos por el 45% del ancho total del canal ($H_{canal}$).
3. **🌊 Profundidad Mínima del Swing en Zona 1 Porcentual (`RatchetMinSwingDepthPercent = 15.0%`):** Exige penetrar al menos el 15% del espesor de Zona 1 respecto a Banda 1.75.
4. **📊 Separación Mínima EMA 200 Porcentual (`RatchetEmaMinSeparationPercent = 5.0%`):** Exige separación de al menos el 5% del canal fuera de Banda 1.75.
5. **📈 Umbral Pendiente EMA 200 (`RatchetEmaMinSlopeThreshold = 0.0`):** Exige $\ge 0.0$ para Longs y $\le 0.0$ para Shorts.
6. **📉 Pendiente Mínima Inercia TEMA 9 (`RatchetMinTemaSlope = 0.20` pts/barra):** Descarta rebotes muertos o planos.
7. **⏳ Respiro al Aborto por Swing Prematuro (`RatchetEarlySwingMaxBars = 10`):** Aumentado a 10 barras de respiro.
8. **🚀 Despegue Mínimo Base Swing (`RatchetMinLowSeparationTicks = 6`):** Calibrado a 6 ticks (1.5 pts).
9. **🪢 Ahogamiento C3 en Super-Expansión (`RatchetChokeOutsideTicks = 12`):** Activa el Choke ceñido a 12 ticks fuera de Banda 3.5.
10. **⚡ Pendiente Cascada Surfer (`CascadaSlopeThreshold = 0.16`, `CascadaSlopeTriggerThreshold = 0.17`):** Calibración de alta aceleración institucional.
11. **🎨 Corrección UI WPF:** Prefijos de dos dígitos (`01.` a `11.`) para ordenamiento perfecto en NinjaTrader 8 y Tooltips descriptivos en español.

> **Documento de Referencia Técnica:** Ver [FICHA_TECNICA_RATCHET_v8.1.md](file:///c:/Users/carlo/Documents/NinjaTrader%208/bin/Custom/ReportesTrades/CascadaVega/Version8.0/FICHA_TECNICA_RATCHET_v8.1.md) para la especificación matemática completa.
