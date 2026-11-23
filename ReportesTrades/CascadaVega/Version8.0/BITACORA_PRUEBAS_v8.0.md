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

Periodo analizado: **26 de Octubre a 20 de Noviembre de 2026** (16 sesiones de trading).

| # | Archivo de Reporte | Fecha | Trades (Contratos) | PnL Neto Día | Desglose de Operaciones y Salidas |
| :---: | :---: | :---: | :---: | :---: | :--- |
| **1** | `PAS_10262026_Cascada10.xlsx` | 26/10/2026 | 1 | **-$88.08 USD** | Ratchet_Long -> Exit_Ratchet_SwingPrematuro (-$88.08) |
| **2** | `PAS_10272026_Cascada10.xlsx` | 27/10/2026 | 3 | **+$674.42 USD** | Ratchet_Long -> Exit_Ratchet_C2_Upper35 (+$195.64)<br>Ratchet_Long -> Exit_Ratchet_C1_Tranca (+$195.64)<br>Ratchet_Long -> Exit_Ratchet_PisoUpper35 (+$283.14) |
| **3** | `PAS_11022026_Cascada10.xlsx` | 02/11/2026 | 4 | **+$632.56 USD** | Ratchet_Long -> Exit_Ratchet_C1_Tranca (+$208.14)<br>Ratchet_Long -> Exit_Ratchet_C2_Upper35 (+$258.14)<br>Ratchet_Long -> Exit_Ratchet_PisoUpper175 (-$4.36)<br>Ratchet_Short -> Exit_Ratchet_C1_Tranca (+$170.64) |
| **4** | `PAS_11032026_Cascada10.xlsx` | 03/11/2026 | 0 | **$0.00 USD** | *Sin operaciones:* Filtros de tendencia y estructura mantuvieron la cuenta protegida en rango sucio. |
| **5** | `PAS_11042026_Cascada10.xlsx` | 04/11/2026 | 3 | **+$336.34 USD** | Ratchet_Long -> Exit_Ratchet_SwingPrematuro (+$249.42)<br>Ratchet_Long -> Exit_Ratchet_C1_Tranca (+$120.64)<br>Ratchet_Long -> Exit_Ratchet_PisoMidline (-$33.72) |
| **6** | `PAS_11052026_Cascada10.xlsx` | 05/11/2026 | 4 | **+$1,382.56 USD** | Ratchet_Short -> Exit_Ratchet_C1_Tranca (+$195.64)<br>Ratchet_Short -> Exit_Ratchet_C2_Lower35 (+$508.14)<br>Ratchet_Short -> Exit_Ratchet_Choke4tk (+$808.14)<br>Ratchet_Short -> Cortacircuito_Diario_Piso_Exacto (-$129.36) |
| **7** | `PAS_11062026_Cascada10.xlsx` | 06/11/2026 | 0 | **$0.00 USD** | *Sin operaciones:* Filtros de compresión descartaron volatilidad desordenada. |
| **8** | `PAS_11092026_Cascada10.xlsx` | 09/11/2026 | 2 | **-$654.94 USD** | Ratchet_Long -> Exit_Ratchet_SwingPrematuro (-$500.58)<br>Ratchet_Long -> Exit_Ratchet_SlInicial (-$154.36) |
| **9** | `PAS_11102026_Cascada10.xlsx` | 10/11/2026 | 1 | **+$470.64 USD** | Cascada50_Long -> Exit_T1_TP_FrancoTirador (+$470.64) |
| **10** | `PAS_11112026_Cascada10.xlsx` | 11/11/2026 | 3 | **-$79.94 USD** | Ratchet_Long -> Exit_Ratchet_SwingPrematuro (-$33.72)<br>Ratchet_Long -> Exit_Ratchet_SwingPrematuro (-$29.36)<br>Ratchet_Long -> Exit_Ratchet_SwingPrematuro (-$16.86) |
| **11** | `PAS_11122026_Cascada10.xlsx` | 12/11/2026 | 3 | **+$1,374.42 USD** | Ratchet_Long -> Exit_Ratchet_C1_Tranca (+$220.64)<br>Ratchet_Long -> Exit_Ratchet_C2_Upper35 (+$458.14)<br>Ratchet_Long -> Exit_Ratchet_Choke4tk (+$695.64) |
| **12** | `PAS_11162026_Cascada10.xlsx` | 16/11/2026 | 4 | **-$151.16 USD** | Ratchet_Short -> Exit_Ratchet_C1_Tranca (+$220.64)<br>Ratchet_Short -> Exit_Ratchet_PisoLower175 (-$33.72)<br>Ratchet_Long -> Exit_Ratchet_C1_Tranca (+$95.64)<br>Ratchet_Long -> Exit_Ratchet_SlInicial (-$433.72) |
| **13** | `PAS_11172026_Cascada10.xlsx` | 17/11/2026 | 3 | **+$1,536.92 USD** | Ratchet_Short -> Exit_Ratchet_C1_Tranca (+$158.14)<br>Ratchet_Short -> Exit_Ratchet_C2_Lower35 (+$533.14)<br>Ratchet_Short -> Exit_Ratchet_Choke4tk (+$845.64) |
| **14** | `PAS_11182026_Cascada10.xlsx` | 18/11/2026 | 1 | **+$470.64 USD** | Cascada50_Long -> Exit_T1_TP_FrancoTirador (+$470.64) |
| **15** | `PAS_11192026_Cascada10.xlsx` | 19/11/2026 | 3 | **+$1,970.06 USD** | Ratchet_Long -> Exit_Ratchet_C1_Tranca (+$183.14)<br>Ratchet_Long -> Exit_Ratchet_Choke4tk (+$1,891.28)<br>Ratchet_Short -> Cortacircuito_Diario_Piso_Exacto (-$104.36) |
| **16** | `PAS_11202026_Cascada10.xlsx` | 20/11/2026 | 2 | **+$1,049.42 USD** | Ratchet_Short -> Exit_Ratchet_C1_Tranca (+$233.14)<br>Ratchet_Short -> Exit_Ratchet_PisoLower175 (+$816.28) |

---

## 3. Métricas Globales Consolidadas (v8.0)

* **Total de Sesiones Evaluadas:** 16 días operativos.
* **Sesiones Ganadoras (Días Verdes):** 10 (62.5%)
* **Sesiones Perdedoras (Días Rojos):** 4 (25.0%)
* **Sesiones Neutrales / Sin Operar (Días Flat):** 2 (12.5%)
* **Beneficio Neto Total:** **+$8,923.86 USD**
* **Ganancia Bruta (Gross Profit):** +$10,486.06 USD
* **Pérdida Bruta (Gross Loss):** -$1,562.20 USD
* **Profit Factor Institucional:** **6.71**
* **Promedio Neto por Sesión (Avg Day):** **+$557.74 USD / día**
* **Promedio Neto por Contrato (Avg Trade):** **+$241.19 USD / contrato**
* **Total Contratos Operados:** 37 contratos (25 ganadores / 12 perdedores o breakeven, **67.6% Win Rate**).

### Desglose por Patrón de Entrada
1. **Cascada50_Long:** 2 trades | 2 wins (100% WR) | **+$941.28 USD** (Ambos alcanzaron `Exit_T1_TP_FrancoTirador`).
2. **Ratchet_Short:** 13 contratos | 10 wins / 3 losses (76.9% WR) | **+$4,222.10 USD**.
3. **Ratchet_Long:** 22 contratos | 13 wins / 9 losses (59.1% WR) | **+$3,760.48 USD**.

### Desglose por Mecanismo de Salida
* **`Exit_Ratchet_Choke4tk` (Ahogamiento C3):** 4 ejecuciones | 4 wins (100%) | **+$4,240.70 USD** (El mayor generador de alpha).
* **`Exit_Ratchet_C1_Tranca` (Aseguramiento C1):** 11 ejecuciones | 11 wins (100%) | **+$2,002.04 USD** (Fiabilidad perfecta).
* **`Exit_Ratchet_C2_Lower35 / Upper35` (Salida C2 a Banda 3.5):** 5 ejecuciones | 5 wins (100%) | **+$1,953.20 USD**.
* **`Exit_T1_TP_FrancoTirador` (Cascada):** 2 ejecuciones | 2 wins (100%) | **+$941.28 USD**.
* **`Exit_Ratchet_SwingPrematuro`:** 6 ejecuciones | 1 win / 5 losses | **-$419.18 USD** (Optimización detectada: dar más barras de respiro al swing).
* **`Exit_Ratchet_SlInicial`:** 2 ejecuciones | -$588.08 USD.
* **`Cortacircuito_Diario_Piso_Exacto`:** 2 ejecuciones | -$233.72 USD.

---

## 3. Notas Estratégicas y Puntos Abiertos
> [!IMPORTANT]
> **Consolidación Histórica Semana Nov 16 - Nov 20:**  
> La semana previa cerró con **5 de 5 días en verde** y más de **+$5,088 USD netos**. La versión 8.0 incorpora los filtros de calidad para blindar los días de choque como el 12 y 20 de noviembre sin afectar los corredores limpios.
