# Bitácora de Pruebas y Desempeño - Versión 8.0 (Cascada Vega ES)

Esta bitácora documenta las pruebas comparativas A/B y el rendimiento histórico en NinjaTrader 8 Playback sobre futuros del S&P 500 (ES / MES) en gráfica de **610 Ticks**, evaluando la arquitectura **Versión 8.0**.

---

## 1. Novedades y Candados de la Versión 8.0
* **Rescate de Contratos en Tranca Universal (Bugfix v8.0):** Cuando la Tranca Universal se activa por ganancia flotante y retroceso, **únicamente liquida el Contrato 1 (`Exit_Ratchet_C1_Tranca`)**. Los Contratos 2 y 3 quedan 100% blindados para buscar sus techos/pisos de Banda 3.5 y Choke Parabólico, recuperando trades de más de +$1,800 USD.
* **Filtro A: Profundidad del Swing en Zona 1 (`RatchetMinSwingDepthTicks = 10`):** Exige que el Swing Low penetre al menos 10 ticks por debajo de `Lower 1.75` (y el Swing High 10 ticks sobre `Upper 1.75`), eliminando pivotes tibios pegados a la frontera de Zona 2.
* **Filtro B1: Separación de EMA 200 respecto al Canal Pequeño (`RatchetEmaMinSeparationTicks = 8`):** Exige que la EMA 200 esté al menos 8 ticks por fuera de las bandas 1.75, evitando compresión y picadillo central.
* **Filtro B2: Pendiente Normalizada de EMA 200 (`RatchetEmaMinSlopeThreshold = 0.15`):** Exige aceleración institucional direccional ($\ge +0.15$ para compras, $\le -0.15$ para ventas), descartando mercados planos con "miringas" de pendiente.
* **Chequeo de Salud / Asfixia Opcional:** `RatchetHealthCheckEnabled = False` por defecto, permitiendo al trader decidir si activa la póliza de asfixia en mercados picados.

---

## 2. Registro de Días de Playback Evaluados (v8.0)

| Fecha | Modo Operativo | Trades Realizados | Resultado Neto | Duración / Salida | Observaciones Clave |
| :---: | :---: | :---: | :---: | :---: | :--- |
| *Pruebas v8.0 en curso* | — | — | — | — | Registro abierto para la nueva fase de validación. |

---

## 3. Notas Estratégicas y Puntos Abiertos
> [!IMPORTANT]
> **Consolidación Histórica Semana Nov 16 - Nov 20:**  
> La semana previa cerró con **5 de 5 días en verde** y más de **+$5,088 USD netos**. La versión 8.0 incorpora los filtros de calidad para blindar los días de choque como el 12 y 20 de noviembre sin afectar los corredores limpios.
