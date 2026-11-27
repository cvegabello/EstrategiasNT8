# Ficha Técnica - Equipo Dumbo v3.0 (Campaña de Relevos Monocontrato)

![Equipo Dumbo - Relevo en Equipo](EquipoDumboRelevos.jpg)

## 1. Identidad de la Versión
* **Nombre de la Estrategia:** `Tick610_DumboES` (v3.0 MES / ES)
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

1. **Ticket 1 (El Despegue Principal):**
   * Dispara en la ruptura y aceleración inicial.
   * SL amplio en la **Banda Azul 1.75** (2 ticks de respiro) para darle espacio de volar.
   * Cobra en seco con **Orden Limit a +$60.00 USD** ($600 ES = 48 ticks).
   * *Regla Sagrada:* Si el primer envión falla y toca el SL, **NO entra en cuarentena**. Espera el cooldown normal (3 barras) y sigue acechando el despegue mientras la EMA 200 siga en el abismo.
2. **Ticket 2 y 3 (Los Relevos Quirúrgicos):**
   * Al cerrar en TP el trade previo, la siguiente barra evalúa: ¿El precio sigue a **$\ge 30$ ticks** fuera de Banda 3.5 con filtros OK?
   * Si sí, ¡el testigo pasa al siguiente Dumbo! Dispara con **TP Limit a +$20.00 USD** ($200 ES = 16 ticks).
   * **Stop Loss Escalonado (Opción B):** Para no asfixiar el trade ni regalar demasiado:
     * Si la distancia es de 30 a 34 ticks $\rightarrow$ SL en Banda 3.5 (-1tk).
     * Si la distancia es de 35 a 39 ticks $\rightarrow$ SL a **+12 ticks adelante** de Banda 3.5.
     * Si la distancia es $\ge 40$ ticks $\rightarrow$ SL a **+20 ticks adelante** de Banda 3.5.
3. **Ticket 4 en adelante (El Gotero de Tendencia):**
   * Mientras conserve **$\ge 25$ ticks** fuera de Banda 3.5, sigue mordiendo **+$10.00 USD** ($100 ES = 8 ticks) por relevo.
4. **Cuarentena de Saciedad (10 minutos):**
   * Si cualquier ticket secundario toca su SL o si la distancia exterior cae por debajo del umbral mínimo, la campaña termina y el bot se congela por 10 minutos protegiendo todas las ganancias cosechadas.

---

## 3. Parámetros Nativos de Fábrica (Micro MES v3.0)

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

### D. Opción B: Salto Escalonado de Stop Loss
* **Umbral Stage 1:** 35 ticks $\rightarrow$ SL a +12 ticks adelante de Banda 3.5.
* **Umbral Stage 2:** 40 ticks $\rightarrow$ SL a +20 ticks adelante de Banda 3.5.
* **Stop Catastrófico Máximo:** 40 ticks (10 puntos = $50 USD MES / $500 ES).
* **Cuarentena de Saciedad:** 10 minutos tras finalizar campaña o SL secundario.
