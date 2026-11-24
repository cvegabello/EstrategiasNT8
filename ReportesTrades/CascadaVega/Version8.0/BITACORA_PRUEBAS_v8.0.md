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
| **17** | `PAS_11232026_Cascada10.xlsx` | 23/11/2026 | 4 | **+$3,245.06 USD** | Ratchet_Long -> Exit_Ratchet_C1_Tranca (+$358.14)<br>Ratchet_Long -> Exit_Ratchet_C2_Upper35 (+$1,320.64)<br>Ratchet_Long -> Exit_Ratchet_Choke4tk (+$1,708.14)<br>Ratchet_Long -> Cortacircuito_Diario_Piso_Exacto (-$141.86) |

---

## 3. Métricas Globales Consolidadas (v8.0)

* **Total de Sesiones Evaluadas:** 17 días operativos.
* **Sesiones Ganadoras (Días Verdes):** 11 (64.7%)
* **Sesiones Perdedoras (Días Rojos):** 4 (23.5%)
* **Sesiones Neutrales / Sin Operar (Días Flat):** 2 (11.8%)
* **Beneficio Neto Total:** **+$12,168.92 USD**
* **Ganancia Bruta (Gross Profit):** +$13,872.98 USD
* **Pérdida Bruta (Gross Loss):** -$1,704.06 USD
* **Profit Factor Institucional:** **8.14**
* **Promedio Neto por Sesión (Avg Day):** **+$715.82 USD / día**
* **Promedio Neto por Contrato (Avg Trade):** **+$296.80 USD / contrato**
* **Total Contratos Operados:** 41 contratos (28 ganadores / 13 perdedores, **68.3% Win Rate**).

### Desglose por Patrón de Entrada
1. **Cascada50_Long:** 2 trades | 2 wins (100% WR) | **+$941.28 USD**.
2. **Ratchet_Short:** 13 contratos | 10 wins / 3 losses (76.9% WR) | **+$4,222.10 USD**.
3. **Ratchet_Long:** 26 contratos | 16 wins / 10 losses (61.5% WR) | **+$7,005.54 USD**.

### Desglose por Mecanismo de Salida
* **`Exit_Ratchet_Choke4tk` (Ahogamiento C3):** 5 ejecuciones | 5 wins (100%) | **+$5,948.84 USD** (El motor nuclear de la estrategia).
* **`Exit_Ratchet_C1_Tranca` (Aseguramiento C1):** 12 ejecuciones | 12 wins (100%) | **+$2,360.18 USD** (100% de fiabilidad).
* **`Exit_Ratchet_C2_Lower35 / Upper35` (Salida C2 a Banda 3.5):** 6 ejecuciones | 6 wins (100%) | **+$3,273.84 USD**.
* **`Exit_T1_TP_FrancoTirador` (Cascada):** 2 ejecuciones | 2 wins (100%) | **+$941.28 USD**.
* **`Exit_Ratchet_SwingPrematuro`:** 6 ejecuciones | 1 win / 5 losses | **-$419.18 USD**.
* **`Exit_Ratchet_SlInicial`:** 2 ejecuciones | -$588.08 USD.
* **`Cortacircuito_Diario_Piso_Exacto`:** 3 ejecuciones | -$375.58 USD.

---

## 3. Notas Estratégicas y Puntos Abiertos
> [!IMPORTANT]
> **Consolidación Histórica Semana Nov 16 - Nov 20:**  
> La semana previa cerró con **5 de 5 días en verde** y más de **+$5,088 USD netos**. La versión 8.0 incorpora los filtros de calidad para blindar los días de choque como el 12 y 20 de noviembre sin afectar los corredores limpios.

---

## 4. Registro de Calibraciones y Mejoras Aprobadas (Roadmap Próxima Actualización)

Esta es la lista oficial de ajustes y optimizaciones validadas durante la fase de pruebas y simulación en tiempo real, listas para ser incorporadas en el código:

1. **🎯 Modo Monocontrato / Desescalado en Ratchet (Target Banda 3.5 Prioritario):**
   * *Diagnóstico:* Cuando Ratchet opera desescalado a 1 solo contrato (para proteger ganancias diarias) o en modo monocontrato, actualmente no cobra en Banda 3.5 porque `Exit_Ratchet_C2_Upper35` exige $\ge 3$ contratos. El trade se comporta como "Astronauta / C3", arriesgando que una ganancia flotante de +25 ticks en el techo de Banda 3.5 se devuelva hasta el piso de Banda 1.75.
   * *Solución aprobada:* Si `activeTradeContracts == 1`, el objetivo de salida prioritario será **liquidar inmediatamente al tocar la Banda 3.5 (Offset 0)**, asegurando de +20 a +30 ticks en el bolsillo en vez de dejarlo flotar.

2. **📐 Filtro Anti-Persecución Dinámico Porcentual:**
   * *Diagnóstico:* Los valores fijos en ticks (ej. 24 ticks o 50 ticks) fallan según la volatilidad: en canales gigantes (140 ticks) bloquean trades millonarios como el del 23/11, y en canales enanos (48 ticks) permiten comprar en el 86% del techo como el trade de 10:07 AM.
   * *Solución aprobada:* Medir la posición relativa de la vela 4 respecto a la altura total del canal ($H_{canal} = \text{Upper 3.5} - \text{Lower 3.5}$). Descartar la entrada si el precio ya consumió más del **40% - 45%** de la altura total hacia el techo (o hacia el piso en cortos).

3. **🎯 Offset C2 Banda 3.5 en 0 Ticks (`RatchetC2UpperBigOffsetTicks = 0`):**
   * Salida del Contrato 2 exactamente al tocar la Banda 3.5, sin exigir penetración externa.

4. **⏳ Respiro al Aborto por Swing Prematuro (`RatchetEarlySwingMaxBars = 10`):**
   * Aumentar de 6 a 10 barras para que el rebote tenga espacio y tiempo de desarrollarse antes de declarar fallo.

5. **🌊 Profundidad Mínima del Swing en Zona 1 (`RatchetMinSwingDepthTicks = 3`):**
   * Reducir de 10 a 3 ticks la exigencia de penetración para no filtrar giros limpios.

6. **🚀 Separación Mínima Base/Techo Swing (`RatchetMinLowSeparationTicks = 6`):**
   * Ajustar a 6 ticks (1.5 pts) de despegue mínimo respecto al pivote.

7. **📉 Pendiente Mínima de Entrada TEMA 9 en el Swing (`RatchetMinTemaSlope`):**
   * Exigir que el TEMA 9 lleve una pendiente viva ($\ge +0.20$ pts/bar en largos, $\le -0.20$ en cortos) para descartar rebotes muertos o planos.

8. **⚡ Pendiente Mínima de Cascada (`CascadaSlopeThreshold = 0.16`):**
   * Aumentar de 0.15 a 0.16 para que Cascada Apertura (10 min) solo dispare en auténticos cohetes institucionales.

9. **🪢 Ahogamiento C3 Más Temprano (`RatchetChokeOutsideTicks = 10` a `12`):**
   * Reducir de 16 ticks a 10-12 ticks la distancia exterior para que el Contrato 3 active el Choke ceñido a 4 ticks antes.
