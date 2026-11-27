# Bitácora de Pruebas y Desempeño - Equipo Dumbo v3.3 (Micro MES)

Esta bitácora documenta las pruebas en Historical Playback de la **Campaña de Relevos en Equipo Monocontrato** de **Dumbo v3.3** en futuros del Micro E-mini S&P 500 (**MES**) en gráfica de **610 Ticks**.

---

## 1. Novedades y Arquitectura v3.3
1. **Take Profit Fijo con Orden Limit en el Broker:** Cosecha al milisegundo exacto en los picos de momentum.
2. **Campaña por Relevos ($60 / $30 / $20):**
   * **Ticket 1:** Despegue inicial con meta de **+$60.00 USD** ($600 ES) y **Stop Loss Dinámico Vivo en Banda Media (Keltner Midline - 2 ticks)** que persigue la pendiente del canal.
   * **Tickets 2 y 3:** Relevos con meta calibrada a **+$30.00 USD** cada uno ($300 ES = 24 ticks - exactamente la mitad de T1), exigiendo $\ge 30$ ticks fuera por Distancia Pura.
   * **Ticket 4 en adelante:** Gotero de **+$20.00 USD** ($200 ES = 16 ticks), exigiendo **Peaje Caro de $\ge 35$ ticks** fuera y **SL Ceñido a +12 ticks adelante de Banda 3.5** para blindar los $120 USD acumulados.
3. **Pausa Táctica de Confirmación EMA 40b (v3.3):**
   * Si el precio ya venía flotando afuera antes de que la EMA 200 completara sus 40 barras en el abismo, **NO se dispara en la barra 40**. Espera que termine esa barra y evalúa en la siguiente barra (41+) para evitar entrar en trampas de rebote y velas verdes engañosas.
   * Si la EMA 200 ya venía con $> 40$ barras maduras en el abismo en el momento en que el precio da sus 3 velas flotando, el disparo es **inmediato** (impulso fresco).
4. **Alerta de Fina Coquetería EMA 40b:**
   * Al alcanzar exactamente 40 barras continuas en abismo, emite sonido configurable (`Ding.wav`), dibuja flecha y texto en la gráfica, e imprime reporte destacado en el log.
5. **Auditoría TEMA 9 en los Logs:** Impresión detallada de `tema9[0]`, `tema9[1]`, `tema9[TemaSlopeLookback]`, `temaSlope` y `RequireTemaSlope` al disparar T1.
6. **Stop Loss Escalonado Dinámico (Opción B para T2/T3):** Trailing vivo a Banda 3.5, con avances de +12tk (Stage 1 $\ge 35$tk) y +20tk (Stage 2 $\ge 40$tk).
7. **Protección Asimétrica:** Fallo en Ticket 1 no entra en cuarentena; fallo o fin de campaña en Tickets 2+ activa **Cuarentena de Saciedad de 10 minutos**.

---

## 2. Registro de Sesiones Evaluadas en Playback (MES)

| # | Fecha | Día | Tickets Disparados | PnL Neto Total | Tickets Ganados / Perdidos | Observaciones de la Campaña | Estado |
| :---: | :---: | :---: | :---: | :---: | :---: | :--- | :---: |
| **1** | 11/09/2026 | Lunes | 1 (T1) | **+$57.45 USD** | 1 Win / 0 Loss | Disparo T1 Long a 7502.50. Salida en TP Limit a 7514.00 (+48tk). Fin de campaña por espacio < 30tk. | Cobrado |
| **2** | 11/11/2026 | Miércoles | 1 (T1) | **+$57.45 USD** | 1 Win / 0 Loss | Disparo T1 Long a 7647.50. Salida en TP Limit a 7659.50 (+48tk). Fin de campaña por espacio < 30tk. | Cobrado |
| **3** | 11/12/2026 | Jueves | 1 (T1) | **+$57.45 USD** | 1 Win / 0 Loss | Disparo T1 Long a 7659.00. Salida en TP Limit a 7671.00 (+48tk). Relevo no disparó (espacio 21.4tk < 30tk). | Cobrado |
| **4** | 11/04/2026 | Miércoles | 1 (T1) | **+$58.70 USD** | 1 Win / 0 Loss | Disparo T1 Long a 7706.25. Salida en TP Limit a 7718.25 (+48tk). Relevo no disparó (espacio 20.7tk < 30tk). | Cobrado |
| **5** | 11/02/2026 | Lunes | 2 (T1 + T2) | **+$87.40 USD** | 2 Win / 0 Loss | ¡Relevo perfecto! T1 Long cobrado a 7688.50 (+48tk = +$58.70) y T2 Long cobrado a 7693.50 (+24tk = +$28.70). T3 frena por distancia 8.1tk < 30tk y activa Cuarentena de Saciedad. Cero sobreoperar. | Doble Cobro |

---

## 3. Resumen Acumulado de Métricas
* **Campañas Evaluadas:** 5
* **Campañas Ganadoras:** 5 (100% Win Rate)
* **PnL Neto Acumulado:** +$318.45 USD
* **Trades Totales Ganados:** 6 (5 de T1 + 1 de T2)
* **Trades Totales Perdidos:** 0 (0% Loss Rate)
* **Profit Factor:** Infinito (Cero pérdidas en campañas evaluadas)
