# Ficha Técnica - Equipo Dumbo v3.2 (Campaña de Relevos Monocontrato)

![Equipo Dumbo - Relevo en Equipo](EquipoDumboRelevos.jpg)

## 1. Identidad de la Versión
* **Nombre de la Estrategia:** `Tick610_DumboES` (v3.2 MES / ES)
* **Archivo Fuente:** `\Custom\Strategies\Tick610\Tick610_DumboES.cs`
* **Instrumento Primario:** Micro E-mini S&P 500 (`MES`)
* **Instrumento Secundario (Auto-Escalable):** E-mini S&P 500 (`ES`)
* **Tipo de Gráfica:** 610 Ticks (Barras Transaccionales OHLC)
* **Tick Size:** 0.25 ($1.25 USD por tick en MES / $12.50 USD en ES)
* **Horario Operativo:** RTH Estricto (09:35 AM EST hasta 04:00 PM EST, cortes de entrada a las 03:45 PM EST)
* **Interfaz de Control:** Botón de Pánico WPF en Gráfica + Atajo de Teclado `Ctrl + Espacio` (Inicia en PAUSA en tiempo real)

---

## 2. Concepto y Filosofía de Trading: La Carrera de Relevos (ELI5)
En lugar de arriesgar 3 contratos a la vez o dejar un único contrato expuesto a un retroceso salvaje de la tendencia, **Dumbo corre una carrera de relevos en equipo**:

1. **Ticket 1 (El Despegue Principal con Trailing Dinámico en Banda Media - v3.2):**
   * Dispara en la ruptura y aceleración inicial.
   * **Stop Loss Dinámico en Banda Media:** Anclado a **2 ticks detrás de la Banda Media (`keltnerBig.Midline[0]`)**, haciendo **trailing vivo barra a barra**. Si el precio despega y la línea blanca sube con la pendiente, el SL la persigue. Si el precio se desploma, corta inmediatamente al besar la línea blanca, asegurando salida en tablas o mínima pérdida.
   * Cobra en seco con **Orden Limit a +$60.00 USD** ($600 ES = 48 ticks).
   * *Regla Sagrada:* Si el primer envión falla y toca el SL, **NO entra en cuarentena**. Espera el cooldown normal (3 barras) y sigue acechando el despegue mientras la EMA 200 siga en el abismo.
2. **Ticket 2 y 3 (Los Relevos Quirúrgicos Optimizados a $40 USD):**
   * Al cerrar en TP el trade previo, la siguiente barra evalúa: ¿El precio sigue a **$\ge 30$ ticks** fuera de Banda 3.5 con filtros OK?
   * Relevos disparados por **Distancia Pura** sin asfixia de osciladores micro.
   * Cobra con **TP Limit a +$40.00 USD** cada uno ($400 ES = 32 ticks).
   * **Stop Loss Escalonado Dinámico (Opción B):** Trailing persiguiendo a la Banda 3.5:
     * Si la distancia es de 30 a 34 ticks $\rightarrow$ SL en Banda 3.5 (-1tk).
     * Si la distancia es de 35 a 39 ticks $\rightarrow$ SL a **+12 ticks adelante** de Banda 3.5.
     * Si la distancia es $\ge 40$ ticks $\rightarrow$ SL a **+20 ticks adelante** de Banda 3.5.
3. **Ticket 4 en adelante (El Gotero Blindado con Peaje Caro de 35 ticks):**
   * Exige que el mercado pague un **peaje caro** demostrando super-tendencia extrema: **$\ge 35$ ticks** fuera de Banda 3.5. Si no alcanza ese espacio, no se arriesga el botín acumulado de $140 USD y la campaña finaliza con victoria.
   * Si califica, muerde **+$20.00 USD** ($200 ES = 16 ticks) por relevo.
   * **SL Ceñido Dinámico:** El SL se sitúa y acompaña a **+12 ticks por delante de la Banda 3.5** (`Ticket4PlusSlAdvanceTicks = 12`).
4. **Cuarentena de Saciedad (10 minutos):**
   * Si cualquier ticket secundario toca su SL o si la distancia exterior cae por debajo del umbral mínimo, la campaña termina y el bot se congela por 10 minutos protegiendo todas las ganancias cosechadas.

---

## 3. Parámetros Nativos de Fábrica (Micro MES v3.2)

### A. Gestión de Posición
* **Contratos:** 1 contrato (`Contracts = 1`). Monocontrato puro.
* **Modo de Ejecución:** `Calculate.OnBarClose`.

### B. Gatillos y Filtros
* **Velas Flotando Requeridas (T1):** 3 velas consecutivas.
* **Separación Mínima (T1):** $\ge 12$ ticks por fuera de Banda 3.5.
* **Barras EMA 200 en Abismo:** $\ge 40$ barras continuas fuera de Banda 3.5 opuesta.
* **Pendiente EMA 200 Macro:** Activa (`EmaSlopeLookback = 3`).
* **Pendiente TEMA 9 Micro:** Activa (`TemaPeriod = 9`, `TemaSlopeLookback = 2`).

### C. Metas de Take Profit y Relevos (Calibración Ganadora)
* **TP Limit Ticket 1:** +$60.00 USD (48 ticks).
* **TP Limit Tickets 2 y 3:** +$40.00 USD (32 ticks).
* **TP Limit Tickets 4+:** +$20.00 USD (16 ticks).
* **Distancia Mínima Relevos T2/T3:** $\ge 30$ ticks.
* **Distancia Mínima Relevos T4+ (El Peaje Caro):** $\ge 35$ ticks.

### D. Stop Loss Dinámico y Protección v3.2
* **SL Dinámico Ticket 1:** 2 ticks detrás de la Banda Media viva (`keltnerBig.Midline[0]`).
* **Umbral Stage 1 (T2/T3):** 35 ticks $\rightarrow$ SL a +12 ticks adelante de Banda 3.5.
* **Umbral Stage 2 (T2/T3):** 40 ticks $\rightarrow$ SL a +20 ticks adelante de Banda 3.5.
* **Avance SL en Ticket 4+ (T4+):** +12 ticks por delante de Banda 3.5 (`Ticket4PlusSlAdvanceTicks = 12`).
* **Stop Catastrófico Máximo:** 80 ticks (20 puntos = $100 USD MES / $1000 ES).
* **Cuarentena de Saciedad:** 10 minutos tras finalizar campaña o SL secundario.
