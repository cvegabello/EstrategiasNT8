# 📊 Bitácora Oficial de Playback - Poker de Ases Sniper v14.3

* **Estrategia:** `Tick610_PokerDeAsesSniperES` (v14.3)
* **Instrumento:** ES 12-26 / MES (Futuros S&P 500)
* **Gráfica:** 610 Ticks (Barras transaccionales)
* **Horario:** RTH (09:50 AM - 12:00 PM EST Corte de Mediodía / 04:00 PM EST Modo Dulce)
* **Innovación Clave de la Versión 14.3:**
  1. **Filtro Macro Flexible en Ruptura Exterior:**
     * **Contexto de Mercado:** Cuando el precio rompe con violencia explosiva institucional hacia el exterior de las Bandas Keltner 3.5, esperar a que la EMA 200 cruce al otro lado de la Línea Media puede retrasar o bloquear entradas en tendencias limpias y súper aceleradas.
     * **Regla en Ruptura Exterior (`Close[0] < Lower 3.5` para Corto / `Close[0] > Upper 3.5` para Largo):**
       * **Cortos:** El precio ya perforó el piso exterior (`Close[0] < lowerBig`). La EMA 200 debe estar **arriba del precio** (`ema200[0] > Close[0]`) y **ubicada dentro del canal grande** (`ema200[0] >= lowerBig && ema200[0] <= upperBig`), **sin importar si está por encima o por debajo de la Línea Media**.
       * **Largos:** El precio ya perforó el techo exterior (`Close[0] > upperBig`). La EMA 200 debe estar **debajo del precio** (`ema200[0] < Close[0]`) y **ubicada dentro del canal grande** (`ema200[0] >= lowerBig && ema200[0] <= upperBig`), **sin importar si está por encima o por debajo de la Línea Media**.
     * **Regla en el Interior del Canal (`Close[0]` dentro de Bandas 3.5):**
       * Se preserva el filtro macro clásico estricto: EMA 200 debe estar por encima de la Línea Media para Cortos, y por debajo de la Línea Media para Largos.
  2. **Trailing Stop Banda 1.75 por Cierre Estricto de Vela (`Close[0]`):**
     * Preservado de v14.2: la salida por trailing solo se ejecuta si la barra cierra al otro lado de la banda con respiro, ignorando mechas de absorción/rechazo intra-vela.
  3. **Filtro Micro Momentum TEMA 9 (5 Barras):**
     * Cortos: `tema9[0] < tema9[5]`.
     * Largos: `tema9[0] > tema9[5]`.
  4. **Activación de Trailing Stop Estrictamente con Barra 100% Fuera:**
     * Stop Inicial en Línea Media hasta que la vela completa flote 100% afuera de la Banda Grande 3.5.
  5. **Reintento Quirúrgico con Enfriamiento $\ge 5$ Barras:**
     * 1 único Segundo Boleto por ciclo si la salida fue por mecha sin ganar $180 USD netos.
  6. **Cuarentena de Saciedad ($\ge \$180$ USD netos):**
     * Si un trade gana $180 USD netos, congelamiento inmediato de Cascada por 30 minutos sin reintentos.
  7. **Escudo Diario y Tranca de Adamantium:**
     * Cortacircuito -$650 ES, Escalón 1 (+$400 / Piso +$250), Escalón 2 (+$600 / Piso +$500), Modo Dulce (+$900 / Trailing Infinito $150).

---

## 📈 Historial de Rendimiento de Referencia (Versión 14.2)

| Métrica Consolidada | Versión 14.2 (Línea Base) | Versión 13.2 | Versión 11.4 | Objetivo Cuantitativo v14.3 |
| :--- | :---: | :---: | :---: | :--- |
| **Sesiones Auditadas** | **9 días** (21-25 Sep & 02-05 Nov) | 9 días | 9 días | Evaluar capturas de rupturas exteriores aceleradas |
| **PnL Neto Acumulado** | **+$1,953.50 USD** 💰🏆 | +$832.10 USD | -$762.14 USD 🔴 | Superar los +$2,000 USD maximizando capturas tempranas |
| **Profit Factor** | **2.31** 🌟 | 1.62 | 0.72 | Mantener PF $> 2.00$ |
| **Winrate Global** | **52.0% (13W / 12L)** | 56.5% | 52.4% | Mantener $> 50\%$ |
| **Días Positivos / Neutros**| **66.7% (6 de 9 días)** | 55.6% | 44.4% | Mantener $> 65\%$ |
| **Peor Día (Drawdown)** | **-$371.22 USD** (24/09) 🛡️ | -$250.58 USD | -$650.58 USD | Mantener lejísimos del cortacircuito (-$650) |
| **Trade Récord (Home Run)**| **+$845.64 USD** ⚾💥 | +$570.64 USD | +$258.14 USD | Capturar tendencias de alta aceleración |

---

## 🗓️ Registro de Pruebas de Playback - Versión 14.3

*(Los reportes `.xlsx` exportados de NinjaTrader Strategy Performance se registrarán aquí conforme se realicen las pruebas de Playback).*

| Fecha de Prueba | Trades | Wins / Losses | PnL Bruto | Comisiones | PnL Neto v14.3 | Observaciones Tácticas y Diferencias vs v14.2 |
| :---: | :---: | :---: | :---: | :---: | :---: | :--- |
| *Pendiente* | - | - | - | - | - | *Esperando primeras pruebas de playback del usuario.* |
