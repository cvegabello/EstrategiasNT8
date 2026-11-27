# Bitácora de Pruebas y Desempeño - Equipo Dumbo v3.21 (Micro MES)

Esta bitácora documenta las pruebas en Historical Playback de la **Campaña de Relevos en Equipo Monocontrato** de **Dumbo v3.21** en futuros del Micro E-mini S&P 500 (**MES**) en gráfica de **610 Ticks**.

---

## 1. Novedades y Arquitectura v3.21
1. **Take Profit Fijo con Orden Limit en el Broker:** Cosecha al milisegundo exacto en los picos de momentum.
2. **Campaña por Relevos ($60 / $30 / $20):**
   * **Ticket 1:** Despegue inicial con meta de **+$60.00 USD** ($600 ES) y **Stop Loss Dinámico Vivo en Banda Media (Keltner Midline - 2 ticks)** que persigue la pendiente del canal.
   * **Tickets 2 y 3:** Relevos con meta calibrada a **+$30.00 USD** cada uno ($300 ES = 24 ticks - exactamente la mitad de T1), exigiendo $\ge 30$ ticks fuera por Distancia Pura.
   * **Ticket 4 en adelante:** Gotero de **+$20.00 USD** ($200 ES = 16 ticks), exigiendo **Peaje Caro de $\ge 35$ ticks** fuera y **SL Ceñido a +12 ticks adelante de Banda 3.5** para blindar los $120 USD acumulados.
3. **Auditoría TEMA 9 en los Logs:** Impresión detallada de `tema9[0]`, `tema9[1]`, `tema9[TemaSlopeLookback]`, `temaSlope` y `RequireTemaSlope` al disparar T1 para verificar las condiciones exactas leídas por NinjaTrader en cada barra.
4. **Stop Loss Escalonado Dinámico (Opción B para T2/T3):** Trailing vivo a Banda 3.5, con avances de +12tk (Stage 1 $\ge 35$tk) y +20tk (Stage 2 $\ge 40$tk).
5. **Protección Asimétrica:** Fallo en Ticket 1 no entra en cuarentena; fallo o fin de campaña en Tickets 2+ activa **Cuarentena de Saciedad de 10 minutos**.

---

## 2. Registro de Sesiones Evaluadas en Playback (MES)

| # | Fecha | Día | Tickets Disparados | PnL Neto Total | Tickets Ganados / Perdidos | Observaciones de la Campaña | Estado |
| :---: | :---: | :---: | :---: | :---: | :---: | :--- | :---: |
| **1** | 11/09/2026 | Lunes | 1 (T1) | **+$57.45 USD** | 1 Win / 0 Loss | Disparo T1 Long a 7502.50. Salida en TP Limit a 7514.00 (+48tk). Fin de campaña por espacio < 30tk. | Cobrado |
| **2** | 11/11/2026 | Miércoles | 1 (T1) | **+$57.45 USD** | 1 Win / 0 Loss | Disparo T1 Long a 7647.50. Salida en TP Limit a 7659.50 (+48tk). Fin de campaña por espacio < 30tk. | Cobrado |
| **3** | 11/12/2026 | Jueves | 1 (T1) | **+$57.45 USD** | 1 Win / 0 Loss | Disparo T1 Long a 7659.00. Salida en TP Limit a 7671.00 (+48tk). Relevo no disparó (espacio 21.4tk < 30tk). | Cobrado |
| **4** | 11/04/2026 | Miércoles | 1 (T1) | **+$58.70 USD** | 1 Win / 0 Loss | Disparo T1 Long a 7706.25. Salida en TP Limit a 7718.25 (+48tk). Relevo no disparó (espacio 20.7tk < 30tk). | Cobrado |
| **5** | 11/02/2026 | Lunes | 1 (T1) | **-$41.30 USD** | 0 Win / 1 Loss | Trade evaluado para calibración: Entrada Short a 7597.00, SL en 7609.00. v3.21 añade auditoría TEMA9 en log. | Analizado |

---

## 3. Resumen Acumulado de Métricas
* **Campañas Evaluadas:** 5
* **Campañas Ganadoras:** 4 (80% Win Rate)
* **PnL Neto Acumulado:** +$189.75 USD
* **Trades Ganados:** 4
* **Trades Perdidos:** 1
