# Bitácora de Pruebas y Desempeño - Cascada Vega v8.3 (Micro MES)

Esta bitácora documenta el desempeño y validaciones operativas de la **Versión 8.3** de Cascada Vega, calibrada de fábrica para futuros del Micro S&P 500 (**MES**) en gráfica de **610 Ticks**.

---

## 1. Novedades y Candados Críticos de la Versión 8.2
1. **Blindaje de Victoria Temprana (`isDailyLocked`):**
   * Se corrigió la condición para declarar Victoria Temprana: ahora exige estrictamente `Position.MarketPosition == MarketPosition.Flat`.
   * `ManageDynamicExits()` se ejecuta SIEMPRE que haya contratos en el mercado, garantizando que C2 y C3 jamás queden huérfanos.
2. **Corrección de Salidas Multicontrato de Ratchet:**
   * Las órdenes de salida de Ratchet ahora utilizan firmas de cancelación seguras con nombres específicos y cantidades exactas por contrato.
3. **Calibración Nativa Nivel Micro en `SetDefaults`:**
   * La interfaz de NinjaTrader muestra por defecto los valores exactos de MES ($65 cortacircuito, $40 escalón 1, $25 piso 1, etc.) sin necesidad de editarlos manualmente.
4. **Propiedades con Rangos Flexibles `[Range(5.0, ...)]`:**
   * Resuelto el bug de validación de NinjaTrader para permitir valores pequeños de Micro sin lanzar alertas emergentes.

---

## 2. Registro de Sesiones Evaluadas en Micro MES (Periodo 16 al 25 de Noviembre)

| # | Fecha | Instrumento | Estrategia / As | Trades / Contratos | PnL Neto Día | Detalle Operativo y Salidas |
| :---: | :---: | :---: | :---: | :---: | :---: | :--- |
| **1** | **16/11/2026** | **MES 12-26** | Ratchet Short (As 3) | 1 trade (3 contratos) | **+$148.60 USD** | C1 Tranca (+11tk), C2 Lower35 (+77tk), C3 TechoLower175 (+34tk). Ganador limpio. |
| **2** | **17/11/2026** | **MES 12-26** | Ratchet Long (As 3) | 1 trade (3 contratos) | **-$78.90 USD** | Pérdida asumida con honor. AbortoCuna protegió contra caída profunda de 36 ticks. |
| **3** | **18/11/2026** | **MES 12-26** | Ratchet Short (As 3) | 1 trade (3 contratos) | **+$7.35 USD** | C1 Tranca (+25tk), C2/C3 TechoMidline (-8tk). Día cerrado en verde/breakeven. |
| **4** | **19/11/2026** | **MES 12-26** | Ratchet Long (As 3) | 1 trade (3 contratos) | **+$239.85 USD** | Peldaño flexible (Highs iguales aceptados). C1 Tranca, C2 Upper35, C3 Choke4tk. Victoria Temprana. |
| **5** | **20/11/2026** | **MES 12-26** | Ratchet Short (As 3) | 1 trade (3 contratos) | **+$81.10 USD** | C1 Tranca (+22tk), C2/C3 TechoLower175 (+23tk). Ganador sólido. |
| **6** | **23/11/2026** | **MES 12-26** | Ratchet Long (As 3) | 1 trade (3 contratos) | **+$294.85 USD** | C1 Tranca (+21tk), C2 Upper35 (+94tk), C3 Choke4tk (+124tk). Grosería de trade. Victoria Temprana. |
| **7** | **24/11/2026** | **MES 12-26** | Ratchet Long (As 3) | 1 trade (3 contratos) | **+$151.10 USD** | Entrada: 7219.00. C1 Tranca (+21tk), C2 Upper35 (+85tk), C3 PisoMidline (+18tk). |
| **8** | **25/11/2026** | **MES 12-26** | Ratchet Short (As 3) | 1 trade (3 contratos) | **+$64.85 USD** | C1 Tranca (+21tk), C2/C3 TechoMidline (+17tk). Sesión positiva. |

### Balance Global de la Muestra (8 Días):
* **Días Positivos:** 7 de 8 (87.5% Win Rate por día)
* **PnL Neto Total MES:** **+$908.80 USD** (Equivalente a **+$9,088 USD** en ES)
* **Profit Factor:** 12.51

---

## 3. Ajustes de Fábrica Oficiales Establecidos para v8.3
* **`RatchetMinTemaSlope = 0.45`**: Inercia mínima exigida para evitar rebotes planos o velas acostadas.
* **`RatchetStrictPeldanoEnabled = false`**: Permite continuaciones institucionales sin penalizar velas con extremos iguales.
* **`RatchetRequireOriginZoneOnly = true`**: Filtro Mayorista activado para operar exclusivamente con descuento respecto a la Línea Media.
* **`CascadaMaxOutsideTicks = 27`**: Espacio ampliado de 25 a 27 ticks fuera de Banda 3.5 en Cascada Surfer.

---

## 3. Comparativa de Control de Emociones (Lección 24/11/2026)
* **Con Intervención Humana (Botón de Pánico en retroceso):** +$1,282.78 USD (en Mini ES).
* **Con Piloto Automático Puro v8.2:** +$151.10 USD en Micro MES $\equiv$ **+$1,511.00 USD** en Mini ES.
* **Conclusión:** Dejar correr el algoritmo según sus reglas estructurales produjo **+$228.22 USD más** que el impulso emocional de cerrar antes de tiempo.
