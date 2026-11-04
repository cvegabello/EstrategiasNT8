# 📊 Bitácora Oficial de Playback - Poker de Ases Sniper v13.0

* **Estrategia:** `Tick610_PokerDeAsesSniperES` (v13.0)
* **Instrumento:** ES 12-26 / MES (Futuros S&P 500)
* **Gráfica:** 610 Ticks
* **Horario:** RTH (09:50 AM - 12:00 PM EST Corte de Mediodía / 04:00 PM EST Modo Dulce)
* **Innovación Clave v13.0:**
  * **Cascada Entrada Pura:** Disparo rápido con Pendiente Normalizada $|m| \ge 0.15$ persistente por 2 barras (sin retrasos de distancia).
  * **Gestión Asimétrica de Stop Loss en Cascada:**
    * Si entra adentro o a `< 8` ticks fuera de Banda 3.5: Tratada como ruptura fresca con SL estructural en Línea Media.
    * Si entra en sobreextensión exterior $\ge 8$ ticks fuera de Banda 3.5: SL ceñido en Banda 3.5 (Ruptura Fallida), limitando el riesgo a solo 6-8 ticks (~$75 a $100 USD) para evitar que una trampa de agotamiento active el Cortacircuito.
  * **Candado Garantizado:** Suelo mínimo en Breakeven +4 ticks (+$50 USD) al tocar MFE $\ge \$150$ USD.
  * **Escudos Diarios:** Cortacircuito -$650 USD, Escalón 1 (+$400 / Piso +$250), Escalón 2 (+$600 / Piso +$500), Modo Dulce (+$900 / Trailing Infinito $150).

---

## 📈 Resumen Ejecutivo Consolidado

| Métrica Consolidada | Resultado v13.0 |
| :--- | :--- |
| **Días Totales Auditados** | *En curso...* |
| **Días Ganadores vs Perdedores** | - |
| **Total Trades Disparados** | - |
| **Winrate de Trades** | - |
| **PnL Total Neto Acumulado** | - |
| **Profit Factor** | - |

---

## 📅 Registro Detallado Día a Día

| Fecha | Trades | Wins / Losses | PnL Bruto | Comisiones | PnL Neto | Resumen de Operaciones y Observaciones |
| :---: | :---: | :---: | :---: | :---: | :---: | :--- |
| **25/09/2026** | | | | | | *Trade benchmark: Corto 11:45 AM (SL en Banda 3.5) y Largo 11:59 AM* |

---

## 📁 Archivos de Respaldo en esta Carpeta:
* Exportar reportes diarios de NinjaTrader en formato Excel como `PAS_MMDDYYYY.xlsx` (ej. `PAS_09252026.xlsx`) directamente en esta carpeta.
