# Bitácora de Pruebas y Desempeño - Equipo Dumbo v3.4 (Micro MES - "Dumbo Recargado")

Esta bitácora documenta las pruebas en Historical Playback de la **Campaña de Relevos en Equipo Monocontrato** de **Dumbo v3.4** en futuros del Micro E-mini S&P 500 (**MES**) en gráfica de **610 Ticks**.

---

## 1. Novedades y Arquitectura v3.4 (Dumbo Recargado)
1. **Take Profit Fijo con Orden Limit en el Broker:** Cosecha al milisegundo exacto en los picos de momentum.
2. **Campaña por Relevos ($60 / $30 / $20):**
   * **Ticket 1:** Despegue inicial con meta de **+$60.00 USD** ($600 ES) y **Stop Loss Dinámico Vivo en Banda Media (Keltner Midline - 2 ticks)** que persigue la pendiente del canal.
   * **Tickets 2 y 3:** Relevos con meta calibrada a **+$30.00 USD** cada uno ($300 ES = 24 ticks - exactamente la mitad de T1).
   * **Ticket 4 en adelante:** Gotero de **+$20.00 USD** ($200 ES = 16 ticks), exigiendo **Peaje Caro de $\ge 35$ ticks** fuera y **SL Ceñido a +12 ticks adelante de Banda 3.5** para blindar los $120 USD acumulados.
3. **Descanso Táctico en Relevos (3 Barras - v3.4):**
   * Al cobrar el TP, el bot da un respiro de **3 barras** (`RelayCooldownBars = 3`) permitiendo que la absorción y rebotes contra la mecha sucedan fuera del mercado.
4. **Filtro de Inercia TEMA 9 en Relevos (v3.4):**
   * Al concluir las 3 barras de descanso, el bot exige que la **TEMA 9 conserve la fuerza a favor** (`tema9[0] < tema9[1]` en short, `>` en long). Si el precio rebotó, TEMA 9 se dobla y el pase de posta se **aborta automáticamente**, protegiendo el botín ganado.
5. **Cuarentena en 0 Minutos (v3.4):**
   * Desactivada por defecto (`QuarantineMinutes = 0`), gobernada sanamente por las 3 barras de enfriamiento estructurales de Ticket 1 para no enjaular la operativa en megatendencias.
6. **Pausa Táctica de Confirmación EMA 40b (T1):**
   * Si el precio ya venía flotando afuera antes de que la EMA 200 completara sus 40 barras en el abismo, **NO se dispara en la barra 40**. Espera que termine esa barra y evalúa en la siguiente barra (41+) para evitar entrar en trampas de rebote y velas verdes engañosas.
7. **Alerta de Fina Coquetería EMA 40b:**
   * Al alcanzar exactamente 40 barras continuas en abismo, emite sonido configurable (`Ding.wav`), dibuja flecha y texto en la gráfica, e imprime reporte destacado en el log.
8. **Stop Loss Escalonado Dinámico (Opción B para T2/T3):** Trailing vivo a Banda 3.5, con avances de +12tk (Stage 1 $\ge 35$tk) y +20tk (Stage 2 $\ge 40$tk).

---

## 2. Registro de Sesiones Evaluadas en Playback (MES)

| # | Fecha | Día | Tickets Disparados | PnL Neto Total | Tickets Ganados / Perdidos | Observaciones de la Campaña | Estado |
| :---: | :---: | :---: | :---: | :---: | :---: | :--- | :---: |
| **1** | 11/09/2026 | Lunes | 1 (T1) | **+$57.45 USD** | 1 Win / 0 Loss | Disparo T1 Long a 7502.50. Salida en TP Limit a 7514.00 (+48tk). Fin de campaña por espacio < 30tk. | Cobrado |
| **2** | 11/11/2026 | Miércoles | 1 (T1) | **+$57.45 USD** | 1 Win / 0 Loss | Disparo T1 Long a 7647.50. Salida en TP Limit a 7659.50 (+48tk). Fin de campaña por espacio < 30tk. | Cobrado |
| **3** | 11/12/2026 | Jueves | 1 (T1) | **+$57.45 USD** | 1 Win / 0 Loss | Disparo T1 Long a 7659.00. Salida en TP Limit a 7671.00 (+48tk). Relevo no disparó (espacio 21.4tk < 30tk). | Cobrado |
| **4** | 11/04/2026 | Miércoles | 1 (T1) | **+$58.70 USD** | 1 Win / 0 Loss | Disparo T1 Long a 7706.25. Salida en TP Limit a 7718.25 (+48tk). Relevo no disparó (espacio 20.7tk < 30tk). | Cobrado |
| **5** | 11/02/2026 | Lunes | 2 (T1 + T2) | **+$87.40 USD** | 2 Win / 0 Loss | ¡Relevo perfecto! T1 Long cobrado a 7688.50 (+48tk = +$58.70) y T2 Long cobrado a 7693.50 (+24tk = +$28.70). T3 frena por distancia 8.1tk < 30tk y activa Cuarentena de Saciedad. Cero sobreoperar. | Doble Cobro |
| **6** | 11/05/2026 | Jueves | 13 (11 T1, 2 T2) | **+$434.35 USD** | 10 Win / 3 Loss | **¡Jornada Histórica Récord v3.4!** 10 cobros en TP (100% efectividad en Relevos T2). El descanso de 3b y TEMA9 eliminaron todas las pérdidas en T3 y T2. Solo 3 SLs sanos de exploración en T1 (-$42.55, -$11.30, -$18.80) ceñidos por Banda Media. | Mega-Récord |

---

## 3. Resumen Acumulado de Métricas (6 Sesiones en Playback)
* **Sesiones / Días Evaluados:** 6 de 6 Ganadores (**100% Win Rate de Sesiones**)
* **Trades Totales Ejecutados:** 19 trades
* **Trades Ganados:** 16 (**84.2% Win Rate**)
* **Trades Perdidos:** 3 (15.8% - solo micro-stops controlados en T1)
* **PnL Neto Acumulado en el Bolsillo:** **+$752.80 USD** (¡En ES serían **+$7,528.00 USD**!)
* **Profit Factor Global:** **11.36** ($825.45 USD brutos ganados / $72.65 USD en pérdidas)
