# Bitácora de Pruebas y Desempeño - Dumbo v2.0 (Micro MES)

Esta bitácora documenta las sesiones de prueba en Historical Playback y desempeño operativo de **Dumbo v2.0** en el contrato Micro E-mini S&P 500 (**MES**) en gráfica de **610 Ticks**.

---

## 1. Novedades y Configuración de Fábrica (v2.0)
1. **Filtro Macro:** 40 barras continuas requeridas con la EMA 200 en el abismo opuesto (fuera de Banda 3.5).
2. **Espacio Inicial Dumbo:** Stop Loss anclado en la **Línea Azul (Banda 1.75)** con 2 ticks de respiro.
3. **Ascenso a Banda Externa 3.5:** Se activa al tocar **+$60.00 USD** (12 puntos en ES / $60 MES).
4. **Tranca Piso Protegido de $40 USD:** Al tocar los $60, el SL nunca es inferior a **+$40.00 USD** de ganancia asegurada.
5. **Cuarentena de Saciedad:** **10 minutos** de congelamiento tras salir de un trade que tocó/superó $60 USD.

---

## 2. Registro de Sesiones Evaluadas en Playback (MES)

| # | Fecha | Día | Trades Tomados | PnL Neto (MES) | MFE Pico | Salida / Observaciones | Estado |
| :---: | :---: | :---: | :---: | :---: | :---: | :--- | :---: |
| **1** | **13/11/2026** | Vie | 3 trades | -$12.65 USD (v1.41) | +$36.25 USD | Prueba preliminar v1.41. Stop brincó prematuro a Banda 3.5. Corregido en v2.0. | 🔄 En Validación v2.0 |
| **2** | | | | | | | |
| **3** | | | | | | | |
| **4** | | | | | | | |
| **5** | | | | | | | |

---

## 3. Resumen Acumulado de Métricas
* **Sesiones Evaluadas:** 0
* **Días Ganadores:** 0
* **Win Rate:** 0.0%
* **PnL Neto Total:** $0.00 USD
* **Profit Factor:** 0.00
