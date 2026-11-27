# Bitácora de Pruebas y Desempeño - Equipo Dumbo v3.1 (Micro MES)

Esta bitácora documenta las pruebas en Historical Playback de la **Campaña de Relevos en Equipo Monocontrato** de **Dumbo v3.1** en futuros del Micro E-mini S&P 500 (**MES**) en gráfica de **610 Ticks**.

---

## 1. Novedades y Arquitectura v3.1
1. **Take Profit Fijo con Orden Limit en el Broker:** Cosecha al milisegundo exacto en los picos de momentum.
2. **Campaña por Relevos:**
   * **Ticket 1:** Despegue inicial con meta de **+$60.00 USD** ($600 ES) y **Stop Loss Inicial en Banda Media (Keltner Midline - 2 ticks)** sin trailing prematuro para darle máximo respiro a la consolidación.
   * **Tickets 2 y 3:** Relevos con meta de **+$20.00 USD** cada uno ($200 ES), exigiendo $\ge 30$ ticks fuera por Distancia Pura.
   * **Ticket 4 en adelante:** Gotero de **+$10.00 USD** ($100 ES), exigiendo $\ge 25$ ticks fuera con **SL Ceñido a +12 ticks adelante de Banda 3.5 (Opción A)** para blindar ganancias acumuladas ($100 USD de T1+T2+T3).
3. **Stop Loss Escalonado (Opción B para T2/T3):** Sin asfixia en distancias de 30 a 34 ticks, y con avances de +12tk (Stage 1 $\ge 35$tk) y +20tk (Stage 2 $\ge 40$tk).
4. **Protección Asimétrica:** Fallo en Ticket 1 no entra en cuarentena; fallo o fin de campaña en Tickets 2+ activa **Cuarentena de Saciedad de 10 minutos**.

---

## 2. Registro de Sesiones Evaluadas en Playback (MES)

| # | Fecha | Día | Tickets Disparados | PnL Neto Total | Tickets Ganados / Perdidos | Observaciones de la Campaña | Estado |
| :---: | :---: | :---: | :---: | :---: | :---: | :--- | :---: |
| **1** | | | | | | | |
| **2** | | | | | | | |
| **3** | | | | | | | |
| **4** | | | | | | | |
| **5** | | | | | | | |

---

## 3. Resumen Acumulado de Métricas
* **Campañas Evaluadas:** 0
* **Campañas Ganadoras:** 0
* **Win Rate:** 0.0%
* **PnL Neto Acumulado:** $0.00 USD
* **Profit Factor:** 0.00
