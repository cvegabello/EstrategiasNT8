# Bitácora de Pruebas y Desempeño - Versión 6.0 (Cascada Vega ES)

Esta bitácora documenta las pruebas comparativas A/B realizadas en NinjaTrader 8 Playback sobre futuros del S&P 500 (ES / MES) en gráfica de **610 Ticks**, evaluando la arquitectura **Versión 6.0**.

---

## 1. Novedades y Candados de la Versión 6.0
* **Modo Operativo de Cascada:** `SoloApertura` (10:00 - 10:10 AM, 1 trade Francotirador de $470) vs `TodoElDia`.
* **Filtro Anti-Persecución (Ratchet):** `RatchetMaxSeparationTicks = 24` ticks (6.0 pts). Descarta velas 4 de confirmación sobre-extendidas para no perseguir el precio ni vender en agotamientos/clímax.
* **Hard Stop Loss de Ratchet:** Techo inflexible de pérdida máxima de 24 ticks ($900 en 3 contratos ES / $90 en MES).
* **Candado "A Esquiar" (Early Win):** Si el Trade 1 o el saldo supera $450-$500, bloquea la operativa del día.

---

## 2. Registro de Días de Playback Evaluados

| Fecha | Modo Operativo | Trades Realizados | Resultado Neto | Duración / Salida | Observaciones Clave |
| :---: | :---: | :---: | :---: | :---: | :--- |
| **10/11/2026** | Cascada Apertura (10:00) | 1 (Long) | **+$470.64** | 51 segundos | Francotirador exacto en apertura. Se fue a esquiar. |
| **10/11/2026** | Ratchet Solo | 0 | **$0.00** | Sin trades | Filtro descartó short a 32 ticks del swing. **Evitó pérdida de -$1,213.08**. |
| **16/11/2026** | Con Cascada Todo el Día | 4 trades | **+$753.20** | 11:07 AM | Cascada a las 10:50 AM sumó +$245.64 clave. Ese colchón protegió el trade de las 11:06 AM (+345). **Diferencia de +$566.86 a favor**. |
| **16/11/2026** | Solo Ratchet | 4 trades | **+$186.34** | 11:10 AM | Sin Cascada, el trade de las 11:06 AM devolvió 2 contratos en MidLine (-$133), cerrando con apenas +$186.34. |
| **17/11/2026** | Ratchet Solo | 1 (Short 3 contratos) | **+$1,624.42** | 8 min (10:32 AM) | Trade de 10:14 descartado (31 ticks). A las 10:24 entró con 3 contratos plenos: C1 +$245, C2 +$533, C3 +$845. Candado diario bloqueó trade perdedor de las 11:56 AM. |
| **18/11/2026** | Cascada Apertura | 1 (Long) | **+$400+** | Temprano | Meta cumplida en apertura, bloqueo "A Esquiar". |
| **18/11/2026** | Ratchet Solo | 2 (Shorts) | **+$261.92** | Mañana | Trade 10:51 AM dio Tranca. Trade 11:51 AM (45 ticks del swing) descartado, **evitando reversión violenta en V de +30 puntos**. |

---

## 3. Notas Estratégicas y Puntos Abiertos
> [!IMPORTANT]
> **Punto a monitorear (Calibración de Distancia al Swing):**  
> El filtro en `RatchetMaxSeparationTicks = 24` demostró ser salvador el 10 y el 18 de nov. En el 17 de nov descartó el primer corto a 31 ticks, lo cual indirectamente permitió tomar el segundo con los 3 contratos completos para +$1,624.42.  
> Sin embargo, **queda abierta la hipótesis de probar calibraciones intermedias (ej. 28 o 32 ticks)** en los próximos días de prueba para ver el comportamiento general antes de tocar el valor por defecto.
