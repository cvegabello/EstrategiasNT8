# Ficha Técnica - Equipo Dumbo v3.1 (Campaña de Relevos Monocontrato)

![Equipo Dumbo - Relevo en Equipo](EquipoDumboRelevos.jpg)

## 1. Identidad de la Versión
* **Nombre de la Estrategia:** `Tick610_DumboES` (v3.1 MES / ES)
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

1. **Ticket 1 (El Despegue Principal con Oxígeno Profundo en Banda Media - v3.1):**
   * Dispara en la ruptura y aceleración inicial.
   * **Stop Loss en Banda Media:** Anclado inicialmente **2 ticks detrás de la Banda Media (`keltnerBig.Midline[0]`)** sin trailing prematuro. Esto le da un colchón profundo de 45 a 65 ticks para que el precio consolide y respire sin ser sacado por ruido.
   * Cobra en seco con **Orden Limit a +$60.00 USD** ($600 ES = 48 ticks).
   * *Regla Sagrada:* Si el primer envión falla y toca el SL, **NO entra en cuarentena**. Espera el cooldown normal (3 barras) y sigue acechando el despegue mientras la EMA 200 siga en el abismo.
2. **Ticket 2 y 3 (Los Relevos Quirúrgicos):**
   * Al cerrar en TP el trade previo, la siguiente barra evalúa: ¿El precio sigue a **$\ge 30$ ticks** fuera de Banda 3.5 con filtros OK?
   * Relevos disparados por **Distancia Pura** sin asfixia de osciladores micro.
   * Cobra con **TP Limit a +$20.00 USD** cada uno ($200 ES = 16 ticks).
   * **Stop Loss Escalonado (Opción B):** Para no asfixiar el trade ni regalar demasiado:
     * Si la distancia es de 30 a 34 ticks $\rightarrow$ SL en Banda 3.5 (-1tk).
     * Si la distancia es de 35 a 39 ticks $\rightarrow$ SL a **+12 ticks adelante** de Banda 3.5.
     * Si la distancia es $\ge 40$ ticks $\rightarrow$ SL a **+20 ticks adelante** de Banda 3.5.
3. **Ticket 4 en adelante (El Gotero de Tendencia con Blindaje Ceñido):**
   * Mientras conserve **$\ge 25$ ticks** fuera de Banda 3.5, sigue mordiendo **+$10.00 USD** ($100 ES = 8 ticks) por relevo.
   * **SL Ceñido Fijo (Opción A):** Para evitar una asimetría de riesgo (arriesgar 30-40 ticks por buscar solo 8 ticks de TP), en T4+ el SL se sitúa directamente a **+12 ticks por delante de la Banda 3.5** (`Ticket4PlusSlAdvanceTicks = 12`). Si el mercado retrocede, el trade corta con una pérdida muy pequeña ($15 - $20 USD), blindando el 80% o más de los $100 USD ya cosechados entre T1, T2 y T3.
4. **Cuarentena de Saciedad (10 minutos):**
   * Si cualquier ticket secundario toca su SL o si la distancia exterior cae por debajo del umbral mínimo, la campaña termina y el bot se congela por 10 minutos protegiendo todas las ganancias cosechadas.

---

## 3. Parámetros Nativos de Fábrica (Micro MES v3.1)

### A. Gestión de Posición
* **Contratos:** 1 contrato (`Contracts = 1`). Monocontrato puro.
* **Modo de Ejecución:** `Calculate.OnBarClose`.

### B. Gatillos y Filtros
* **Velas Flotando Requeridas (T1):** 3 velas consecutivas.
* **Separación Mínima (T1):** $\ge 12$ ticks por fuera de Banda 3.5.
* **Barras EMA 200 en Abismo:** $\ge 40$ barras continuas fuera de Banda 3.5 opuesta.
* **Pendiente EMA 200 Macro:** Activa (`EmaSlopeLookback = 3`).
* **Pendiente TEMA 9 Micro:** Activa (`TemaPeriod = 9`, `TemaSlopeLookback = 2`).

### C. Metas de Take Profit y Relevos
* **TP Limit Ticket 1:** +$60.00 USD (48 ticks).
* **TP Limit Tickets 2 y 3:** +$20.00 USD (16 ticks).
* **TP Limit Tickets 4+:** +$10.00 USD (8 ticks).
* **Distancia Mínima Relevos T2/T3:** $\ge 30$ ticks.
* **Distancia Mínima Relevos T4+:** $\ge 25$ ticks.

### D. Stop Loss Escalonado y Protección v3.1
* **SL Inicial Ticket 1:** 2 ticks detrás de la Banda Media (`keltnerBig.Midline[0]`).
* **Umbral Stage 1 (T2/T3):** 35 ticks $\rightarrow$ SL a +12 ticks adelante de Banda 3.5.
* **Umbral Stage 2 (T2/T3):** 40 ticks $\rightarrow$ SL a +20 ticks adelante de Banda 3.5.
* **Avance SL en Ticket 4+ (T4+):** +12 ticks por delante de Banda 3.5 (`Ticket4PlusSlAdvanceTicks = 12`).
* **Stop Catastrófico Máximo:** 80 ticks (20 puntos = $100 USD MES / $1000 ES) para permitir el respiro de la Banda Media.
* **Cuarentena de Saciedad:** 10 minutos tras finalizar campaña o SL secundario.
