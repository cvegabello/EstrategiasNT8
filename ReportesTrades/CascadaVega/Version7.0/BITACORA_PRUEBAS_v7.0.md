# Bitácora de Pruebas y Desempeño - Versión 7.0 (Cascada Vega ES)

Esta bitácora documenta las pruebas comparativas A/B realizadas en NinjaTrader 8 Playback sobre futuros del S&P 500 (ES / MES) en gráfica de **610 Ticks**, evaluando la arquitectura **Versión 7.0**.

---

## 1. Novedades y Candados de la Versión 7.0
* **Aborto Temprano por Swing Prematuro (Acción de Precio Pura):** Al llegar a la barra N (`RatchetEarlySwingMaxBars = 6` barras por defecto) tras entrar, evalúa el Swing visible más reciente. Si el precio no logró cerrar superándolo (`Close[0] <= swingHighPrice` en compras o `Close[0] >= swingLowPrice` en ventas), aborta inmediatamente la posición completa (`Exit_Ratchet_SwingPrematuro`) y activa la cuarentena total de 40 barras. Si la vela completica cerró superándolo, el trade pasa la prueba (`ratchetEarlySwingPassed = true`) y corre libre.
* **Cuarentena Total de Enfriamiento Post-Pérdida / Aborto (40 barras por defecto):** Tras cualquier Stop Loss o Aborto Prematuro, congela de inmediato a Ratchet en ambas direcciones (tanto compras como ventas) durante 40 barras completas sin excepciones, impidiendo revancha y ametrallamiento en picadillos o caídas libres.
* **Modo Operativo de Cascada:** `SoloApertura` (10:00 - 10:10 AM, 1 trade Francotirador de $470) vs `TodoElDia`.
* **Filtro Anti-Persecución (Ratchet):** `RatchetMaxSeparationTicks = 24` ticks (6.0 pts).
* **Hard Stop Loss de Ratchet:** Techo inflexible de pérdida máxima de 24 ticks ($900 en 3 contratos ES / $90 en MES).
* **Candado "A Esquiar" (Early Win):** Saldo $\ge +$450 / +$500 USD en Trade 1 bloquea la operativa diaria.

---

## 2. Registro de Pruebas Versión 7.0

| Fecha | Modo Operativo | Trades Realizados | Resultado Neto | Duración / Salida | Observaciones Clave |
| :---: | :---: | :---: | :---: | :---: | :--- |
| **21/09/2026** | Ratchet Solo v7.0 | Evaluación en curso | — | — | Verificando salida anticipada por Swing Prematuro en barra 6 y bloqueo de trades 2 y 3 por Cooldown. |
