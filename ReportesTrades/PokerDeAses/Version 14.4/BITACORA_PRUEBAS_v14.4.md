# 📊 Bitácora Oficial de Playback - Poker de Ases Sniper v14.4

* **Estrategia:** `Tick610_PokerDeAsesSniperES` (v14.4)
* **Instrumento:** ES 12-26 / MES (Futuros S&P 500)
* **Gráfica:** 610 Ticks (Barras transaccionales)
* **Horario:** RTH (09:50 AM - 12:00 PM EST Corte de Mediodía / 04:00 PM EST Modo Dulce)
* **Innovación Clave de la Versión 14.4 (Kriptonita del "Trade Suicida"):**
  1. **Filtro de Confirmación de Vela y Continuación Real en Ruptura Exterior (Opción B):**
     * **Problema Detectado en v14.3:** Al disparar en sobreextensión exterior (fuera de Banda 3.5), si el precio ya hizo suelo y empezó a rebotar con barras verdes, un amague bajista de apenas 1 tick disparaba una venta en pleno soporte ("Trade Suicida"). Esa pérdida innecesaria perforaba el piso del Escudo Diario y bloqueaba la operativa, impidiendo tomar el posterior rally descomunal de +40 puntos.
     * **Regla Quirúrgica en Cortos (`Short`):**
       * **En el interior del canal:** Vela actual bajista (`Close[0] < Open[0]`) con cierre descendente (`Close[0] < Close[1]`).
       * **En Ruptura Exterior (`Close[0] < lowerBig`):** Exige **presión bajista real continua**. La vela actual debe ser roja (`Close[0] < Open[0]`), la vela inmediatamente previa **DEBE haber sido bajista o neutra** (`Close[1] <= Open[1]`), y debe existir **continuación descendente** (`Close[0] < Close[1]`). Si la vela anterior fue una barra verde de rechazo/rebote, **¡EL CORTO SE CANCELA INMEDIATAMENTE!**
     * **Regla Quirúrgica en Largos (`Long`):**
       * **En el interior del canal:** Vela actual alcista (`Close[0] > Open[0]`) con cierre ascendente (`Close[0] > Close[1]`).
       * **En Ruptura Exterior (`Close[0] > upperBig`):** Exige **presión alcista real continua**. La vela actual debe ser verde (`Close[0] > Open[0]`), la vela previa **DEBE haber sido alcista o neutra** (`Close[1] >= Open[1]`), y debe existir **continuación ascendente** (`Close[0] > Close[1]`). Si la vela anterior fue una barra roja de rechazo/techo, **¡EL LARGO SE CANCELA INMEDIATAMENTE!**
  2. **Filtro Macro Flexible en Ruptura Exterior (Heredado de v14.3):**
     * Si el precio rompe afuera de Banda 3.5, solo exige que EMA 200 esté dentro del canal grande (3.5) y a favor del precio, sin importar si está arriba o abajo de la Línea Media.
  3. **Trailing Stop Banda 1.75 por Cierre Estricto de Vela (`Close[0]`):**
     * Salida trailing solo se activa si la vela completa flotó 100% afuera de Banda 3.5 y cierra al otro lado de Banda 1.75 con respiro, ignorando mechas intra-vela.
  4. **Filtro Micro Momentum TEMA 9 (5 Barras):**
     * Cortos: `tema9[0] < tema9[5]`. Largos: `tema9[0] > tema9[5]`.
  5. **Reintento Quirúrgico con Enfriamiento $\ge 5$ Barras:**
     * 1 único Segundo Boleto por ciclo si la salida fue por mecha sin ganar $180 USD netos.
  6. **Cuarentena de Saciedad ($\ge \$180$ USD netos):**
     * Congelamiento inmediato por 30 minutos sin reintentos tras alcanzar meta neta.
  7. **Escudo Diario y Tranca de Adamantium:**
     * Cortacircuito -$650 ES, Escalón 1 (+$400 / Piso +$250), Escalón 2 (+$600 / Piso +$500), Modo Dulce (+$900 / Trailing Infinito $150).

---

## 📈 Historial de Rendimiento de Referencia (Versiones Anteriores)

| Métrica Consolidada | Versión 14.4 (Objetivo) | Versión 14.2 (Línea Base Oficial) | Versión 11.4 (Histórico) |
| :--- | :---: | :---: | :---: |
| **PnL Neto Global (9 días)**| **Eliminar trades suicidas en soportes/techos y capturar rallies posteriores** | **+$1,953.50 USD** 💰🏆 | -$762.14 USD 🔴 |
| **Profit Factor** | **$> 2.50$** | **2.31** 🌟 | 0.72 |
| **Protección de Pisos**| **Evitar que trades falsos en sobreextensión perforan el Piso de $500** | Protegió $361+ en 25/09 pero bloqueó rally de +40 pts | Colapsó al cortacircuito -$650 |

---

## 🗓️ Registro de Pruebas de Playback - Versión 14.4

*(Los reportes `.xlsx` exportados de NinjaTrader Strategy Performance se registrarán aquí conforme se realicen las pruebas de Playback).*

| Fecha de Prueba | Trades | Wins / Losses | PnL Bruto | Comisiones | PnL Neto v14.4 | Observaciones Tácticas y Comparación vs v14.3 / v14.2 |
| :---: | :---: | :---: | :---: | :---: | :---: | :--- |
| *Pendiente* | - | - | - | - | - | *Esperando primeras pruebas de playback del usuario.* |
