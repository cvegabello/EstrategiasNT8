# Ficha Técnica - Equipo Dumbo v3.4 (Campaña de Relevos Recargada)

![Equipo Dumbo - Relevo en Equipo](EquipoDumboRelevos.jpg)

## 1. Identidad de la Versión
* **Nombre de la Estrategia:** `Tick610_DumboES` (v3.4 MES / ES - "Dumbo Recargado")
* **Archivo Fuente:** `\Custom\Strategies\Tick610\Tick610_DumboES.cs`
* **Instrumento Primario:** Micro E-mini S&P 500 (`MES`)
* **Instrumento Secundario (Auto-Escalable):** E-mini S&P 500 (`ES`)
* **Tipo de Gráfica:** 610 Ticks (Barras Transaccionales OHLC)
* **Tick Size:** 0.25 ($1.25 USD por tick en MES / $12.50 USD en ES)
* **Horario Operativo:** RTH Estricto (09:35 AM EST hasta 04:00 PM EST, cortes de entrada a las 03:45 PM EST)
* **Interfaz de Control:** Botón de Pánico WPF en Gráfica + Atajo de Teclado `Ctrl + Espacio` (Inicia en PAUSA en tiempo real)

---

## 2. Concepto y Filosofía de Trading: La Carrera de Relevos Recargada (ELI5)
En lugar de arriesgar 3 contratos a la vez o dejar un único contrato expuesto a un retroceso salvaje de la tendencia, **Dumbo corre una carrera de relevos en equipo**:

1. **Ticket 1 (El Despegue Principal con Pausa Táctica y Trailing en Banda Media):**
   * Dispara en la ruptura y aceleración inicial.
   * **Pausa Táctica de Confirmación:** Si el precio ya venía flotando afuera antes de que la EMA 200 cumpliera sus 40 barras, no entra a ciegas en la barra 40; espera a la barra 41 para confirmar que no sea un rebote falso. Si la EMA ya venía con > 40 barras, dispara de inmediato.
   * **Alerta Visual y Sonora EMA 40b:** Ding.wav + flecha dorada en gráfica al cumplir 40 barras.
   * **Stop Loss Dinámico en Banda Media:** Anclado a 2 ticks detrás de la Banda Media (`keltnerBig.Midline[0]`). Si falla, no entra en cuarentena; sigue acechando mientras EMA 200 siga en abismo.
   * Cobra con **TP Limit a +$60.00 USD** ($600 ES = 48 ticks).
2. **Ticket 2 y 3 (Relevos Quirúrgicos a $30 USD con Descanso Táctico y Filtro TEMA 9 - v3.4):**
   * **Descanso Táctico de 3 Barras:** Al cobrar el TP, el bot da un respiro de 3 barras completas (`RelayCooldownBars = 3`) permitiendo que los rebotes naturales de absorción sucedan fuera de posición.
   * **Filtro de Inercia TEMA 9:** Tras el descanso, exige que la distancia sea $\ge 30$ ticks **Y que la TEMA 9 conserve la fuerza a favor** (`tema9[0] < tema9[1]` para short, `>` para long). Si el mercado rebotó, TEMA 9 se dobla en contra y **el relevo se aborta automáticamente**, protegiendo el botín ganado.
   * Cobra con **TP Limit a +$30.00 USD** cada uno ($300 ES = 24 ticks).
   * **Stop Loss Escalonado Dinámico (Opción B):** Trailing a Banda 3.5, con avances de +12tk ($\ge 35$tk) y +20tk ($\ge 40$tk).
3. **Ticket 4 en adelante (El Gotero Blindado con Peaje Caro de 35 ticks):**
   * Exige peaje de $\ge 35$ ticks fuera de Banda 3.5. TP de **+$20.00 USD** con SL ceñido a **+12 ticks adelante** de Banda 3.5.
4. **Cuarentena de Saciedad (0 minutos por defecto - v3.4):**
   * Cuarentena en **0 minutos**, gobernada inteligentemente por las 3 barras de enfriamiento estructurales de Ticket 1 para no enjaular la operativa en megatendencias históricas continuas.

---

## 3. Parámetros Nativos de Fábrica (Micro MES v3.4)

### A. Gestión de Posición
* **Contratos:** 1 contrato (`Contracts = 1`). Monocontrato puro.
* **Modo de Ejecución:** `Calculate.OnBarClose`.

### B. Gatillos y Filtros
* **Velas Flotando Requeridas (T1):** 3 velas consecutivas.
* **Separación Mínima (T1):** $\ge 12$ ticks por fuera de Banda 3.5.
* **Barras EMA 200 en Abismo:** $\ge 40$ barras continuas fuera de Banda 3.5 opuesta.
* **Pausa Táctica de Confirmación (T1):** Activa cuando precio flotaba antes de la barra 40 de EMA.
* **Alerta Sonora EMA 40b:** `Ding.wav` (Configurable en propiedades).
* **Alerta Visual EMA 40b:** Flecha y texto dorado en barra 40.
* **Pendiente EMA 200 Macro:** Activa (`EmaSlopeLookback = 3`).
* **Pendiente TEMA 9 Micro (T1 y Relevos):** Activa (`TemaPeriod = 9`, `TemaSlopeLookback = 2`).

### C. Metas de Take Profit y Relevos (Calibración Ganadora)
* **TP Limit Ticket 1:** +$60.00 USD (48 ticks).
* **TP Limit Tickets 2 y 3:** +$30.00 USD (24 ticks).
* **TP Limit Tickets 4+:** +$20.00 USD (16 ticks).
* **Barras de Descanso entre Relevos (v3.4):** 3 barras (`RelayCooldownBars = 3`).
* **Distancia Mínima Relevos T2/T3:** $\ge 30$ ticks.
* **Distancia Mínima Relevos T4+ (El Peaje Caro):** $\ge 35$ ticks.

### D. Stop Loss Dinámico y Protección v3.4
* **SL Dinámico Ticket 1:** 2 ticks detrás de la Banda Media viva (`keltnerBig.Midline[0]`).
* **Umbral Stage 1 (T2/T3):** 35 ticks $\rightarrow$ SL a +12 ticks adelante de Banda 3.5.
* **Umbral Stage 2 (T2/T3):** 40 ticks $\rightarrow$ SL a +20 ticks adelante de Banda 3.5.
* **Avance SL en Ticket 4+ (T4+):** +12 ticks por delante de Banda 3.5 (`Ticket4PlusSlAdvanceTicks = 12`).
* **Stop Catastrófico Máximo:** 80 ticks (20 puntos = $100 USD MES / $1000 ES).
* **Cuarentena de Saciedad:** 0 minutos por defecto (`QuarantineMinutes = 0`), gobernada por barras.
