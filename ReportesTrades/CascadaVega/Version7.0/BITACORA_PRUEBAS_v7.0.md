# Bitácora de Pruebas y Desempeño - Versión 7.0 (Cascada Vega ES)

Esta bitácora documenta las pruebas comparativas A/B realizadas en NinjaTrader 8 Playback sobre futuros del S&P 500 (ES / MES) en gráfica de **610 Ticks**, evaluando la arquitectura **Versión 7.0**.

---

## 1. Novedades y Candados de la Versión 7.0
* **Aborto Temprano por Swing Prematuro (Acción de Precio Pura):** Si en las primeras 4 a 8 barras tras entrar se confirma un Swing en contra (Swing High en compras o Swing Low en ventas) y el precio no lo rompió, aborta la posición a mercado (`Exit_Ratchet_SwingPrematuro`) evitando pérdidas mayores.
* **Candado de Enfriamiento Post-Pérdida (Cooldown 20 barras):** Tras cualquier Stop Loss o aborto temprano, congela nuevas entradas en la misma dirección durante 20 barras para evitar ametrallamientos en picadillos o caídas libres.
* **Modo Operativo de Cascada:** `SoloApertura` (10:00 - 10:10 AM, 1 trade Francotirador de $470) vs `TodoElDia`.
* **Filtro Anti-Persecución (Ratchet):** `RatchetMaxSeparationTicks = 24` ticks (6.0 pts).
* **Hard Stop Loss de Ratchet:** Techo inflexible de pérdida máxima de 24 ticks ($900 en 3 contratos ES / $90 en MES).
* **Candado "A Esquiar" (Early Win):** Saldo $\ge +$450 / +$500 USD en Trade 1 bloquea la operativa diaria.

---

## 2. Registro de Pruebas Versión 7.0

| Fecha | Modo Operativo | Trades Realizados | Resultado Neto | Duración / Salida | Observaciones Clave |
| :---: | :---: | :---: | :---: | :---: | :--- |
| **21/09/2026** | Ratchet Solo v7.0 | Evaluación en curso | — | — | Verificando salida anticipada por Swing Prematuro en barra 6 y bloqueo de trades 2 y 3 por Cooldown. |
