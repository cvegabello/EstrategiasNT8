# 📊 Bitácora Oficial de Playback - Poker de Ases Sniper v14.2

* **Estrategia:** `Tick610_PokerDeAsesSniperES` (v14.2)
* **Instrumento:** ES 12-26 / MES (Futuros S&P 500)
* **Gráfica:** 610 Ticks (Barras transaccionales)
* **Horario:** RTH (09:50 AM - 12:00 PM EST Corte de Mediodía / 04:00 PM EST Modo Dulce)
* **Innovaciones Clave de la Versión 14.2:**
  1. **Trailing Stop Banda 1.75 por Cierre Estricto de Vela (`Close[0]`):**
     * **Parámetro:** `CascadaExitOnCloseOnly = true` (por defecto).
     * **Lógica:**
       * **Largos:** La posición solo se liquida si la barra **CIERRA** por debajo o igual a la Banda 1.75 (`Close[0] <= bandSl`). Se ignoran por completo las mechas de absorción/rechazo (`Low[0]`).
       * **Cortos:** La posición solo se liquida si la barra **CIERRA** por encima o igual a la Banda 1.75 (`Close[0] >= bandSl`). Se ignoran por completo las mechas de testeo intra-vela (`High[0]`).
     * **Beneficio Cuantitativo:** Elimina la expulsión prematura por simples martillos o testeos de mecha en retrocesos superficiales, permitiendo surfear la tendencia entera hasta el final sin aumentar el riesgo en pérdidas estáticas.
  2. **Activación de Trailing Stop Estrictamente con Barra 100% Fuera:**
     * El Stop Inicial se mantiene en la **Línea Media** hasta que una vela completa flote 100% afuera de la Banda Grande 3.5.
  3. **Filtro Micro Momentum TEMA 9 (5 Barras):**
     * Cortos: `tema9[0] < tema9[5]`.
     * Largos: `tema9[0] > tema9[5]`.
  4. **Filtro Macro EMA 200:**
     * Cortos: EMA 200 por encima de Línea Media.
     * Largos: EMA 200 por debajo de Línea Media.
  5. **Reintento Quirúrgico con Enfriamiento $\ge 5$ Barras:**
     * 1 único Segundo Boleto por ciclo si la salida fue por mecha sin ganar $180 USD netos.
  6. **Cuarentena de Saciedad ($\ge \$180$ USD netos):**
     * Si un trade gana $180 USD netos, congelamiento inmediato por 30 minutos sin reintentos.
  7. **Escudo Diario y Tranca de Adamantium:**
     * Cortacircuito -$650 ES, Escalón 1 (+$400 / Piso +$250), Escalón 2 (+$600 / Piso +$500), Modo Dulce (+$900 / Trailing Infinito $150).

---

## 📈 Resumen Ejecutivo Consolidado (Versión 14.2)

| Métrica Consolidada | Resultado Oficial v14.2 | Línea Base v14.1 (04/11) | Línea Base v14.0 (04/11) | Impacto Esperado |
| :--- | :---: | :---: | :---: | :--- |
| **Días Totales Auditados** | En curso de pruebas | 1 día (+$257.56) | 1 día (+$207.56) | Cero expulsión por mechas |
| **PnL Neto Acumulado** | Por registrar | +$257.56 USD | +$207.56 USD | Captura del rally completo del Trade 4 |
| **Winrate Global** | Por registrar | 75.0% (3W / 1L) | 75.0% (3W / 1L) | Alta consistencia |
| **Peor Drawdown Diario** | Por registrar | -$216.86 USD | -$116.86 USD | Salida limpia en cierre de vela |

---

## 🗓️ Registro Diario de Pruebas de Playback (Noviembre 2026)

| Fecha | Trades | Wins / Losses | PnL Bruto | Comisiones | PnL Neto v14.2 | Resumen de Trades y Observaciones Tácticas |
| :---: | :---: | :---: | :---: | :---: | :---: | :--- |
| **04/11/2026** | - | - | - | - | - | *Pendiente de corrida con v14.2 (Salida estricta Close[0]).* |
