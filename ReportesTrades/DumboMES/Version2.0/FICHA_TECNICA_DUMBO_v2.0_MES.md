# Ficha Técnica - Dumbo v2.0 (Especialización Micro MES & Dual ES)

## 1. Identidad de la Estrategia
* **Nombre de la Estrategia:** `Tick610_DumboES` (v2.0 MES / ES)
* **Archivo Fuente:** `\Custom\Strategies\Tick610\Tick610_DumboES.cs`
* **Instrumento Primario:** Micro E-mini S&P 500 (`MES`)
* **Instrumento Secundario (Auto-Escalable):** E-mini S&P 500 (`ES`)
* **Tipo de Gráfica:** 610 Ticks (Barras Transaccionales OHLC)
* **Tick Size:** 0.25 ($1.25 USD por tick en MES / $12.50 USD en ES)
* **Horario Operativo:** RTH Estricto (09:35 AM EST hasta 04:00 PM EST, cortes de entrada a las 03:45 PM EST)
* **Interfaz de Control:** Botón de Pánico WPF en Gráfica + Atajo de Teclado `Ctrl + Espacio` (Inicia en PAUSA en tiempo real)

---

## 2. Concepto y Filosofía de Trading (ELI5)
La gran mayoría de estrategias esperan que el precio vuelva a su valor medio (reversión a la media), pero cuando el mercado institucional entra en una **super-expansión tendencial**, el precio rompe el techo o suelo de la estratósfera (**Banda Keltner 3.5**) y sigue volando en esa dirección.

**Dumbo es el elefante que aprende a volar.** En lugar de luchar contra esa inercia o intentar atrapar un cuchillo que cae, Dumbo se monta a favor del viento con tres candados estrictos:
1. **La Gravedad Abandonada:** La EMA 200 (la gravedad) quedó atrapada y aislada en el abismo opuesto por **al menos 40 barras continuas** por fuera del canal grande.
2. **Super-Expansión Estructural:** El precio flota despegado por fuera de la Banda 3.5 con un mínimo de **12 ticks de distancia exterior**.
3. **Inercia Micro Acelerada:** La TEMA 9 apunta con pendiente estricta a favor de la tendencia.

---

## 3. Parámetros Nativos de Fábrica (Micro MES v2.0)

### A. Gestión de Posición
* **Número de Contratos:** 1 contrato (`Contracts = 1`).
* **Modo de Ejecución:** `Calculate.OnBarClose`.

### B. Gatillo y Filtros de Disparo
* **Velas Flotando Fuera Requeridas:** 3 velas consecutivas (`FloatingBarsRequired = 3`).
* **Separación Estricta:** `true` (Todo el rango de la vela por fuera de Banda 3.5: `Low > Upper 3.5` para Longs / `High < Lower 3.5` para Shorts).
* **Separación Mínima Exterior:** 12 ticks (`MinSeparationFromBand35Ticks = 12`).
* **Barras Mínimas EMA en Abismo:** 40 barras continuas (`MinBarsEmaOpposite = 40`).
* **Banda Opuesta de Referencia:** Banda Grande 3.5 (`BandaOpuestaEnum.BandaGrande35`).
* **Filtro Pendiente EMA 200:** Activo (`RequireEmaSlope = true`, lookback 3 barras).
* **Filtro Micro TEMA 9:** Activo (`RequireTemaSlope = true`, lookback 2 barras, periodo 9).

### C. Gestión de Stop Loss (Banda Escalonada y Espacio Dumbo)
* **Anclaje Inicial:** **Banda Azul 1.75** (Keltner Pequeña) para darle espacio amplio al elefante para correr.
* **Colchón de Respiro Inicial:** 2 ticks detrás de la Banda Azul (`SlBufferTicks = 2`).
* **Stop Catastrófico de Emergencia:** 40 ticks = 10 puntos ($50 USD en MES / $500 ES).
* **Promoción de Stop Loss por Ganancia:**
  * **Meta de Upgrade:** **+$60.00 USD** (12 puntos en ES / 48 ticks).
  * **Destino del SL al Promover:** Sube a la **Banda Grande 3.5** (+1 tick por delante).
  * **Piso Protegido Mínimo Asegurado:** **+$40.00 USD** (`ProfitFloorDollars = 40.0`). Jamás entrega más de $20 de lo ganado una vez alcanzada la meta.
  * **Trailing Ratchet:** Si la Banda 3.5 sigue avanzando a favor de la tendencia, el SL sube con la banda protegiendo aún más beneficio.

### D. Cuarentena de Saciedad tras Victoria
* **Estado:** Activa (`UseQuarantine = true`).
* **Duración:** **10 minutos configurables** (`QuarantineMinutes = 10`).
* **Condición de Activación:** Cualquier trade que haya alcanzado o cerrado con ganancia $\ge \$60.00$ USD.
* **Comportamiento:** Congela inmediatamente el sistema, bloqueando cualquier entrada hasta que transcurran los 10 minutos para digerir la ganancia y evitar sobreoperativa emocional.

---

## 4. Canales e Indicadores Técnicos
* **Keltner Channel Grande:** Multiplicador 3.5 ATR, Periodo 52.
* **Keltner Channel Pequeño:** Multiplicador 1.75 ATR, Periodo 52.
* **Media Móvil Gravedad:** EMA 200 periodos.
* **Media Móvil Rápida Micro:** TEMA 9 periodos.
