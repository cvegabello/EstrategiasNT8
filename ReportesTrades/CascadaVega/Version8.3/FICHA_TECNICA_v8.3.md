# Ficha Técnica - Cascada Vega v8.3 (Especialización Micro MES & Dual ES)

## 1. Identidad de la Versión
* **Nombre de la Estrategia:** `Tick610_CascadaVegaES` (v8.3 MES / ES)
* **Instrumento Primario:** Micro E-mini S&P 500 (`MES`)
* **Instrumento Secundario (Auto-Escalable):** E-mini S&P 500 (`ES`)
* **Gráfica:** 610 Ticks (Barras Transaccionales OHLC)
* **Tick Size:** 0.25 ($1.25 USD por tick en MES / $12.50 USD en ES)
* **Horario Operativo:** RTH (09:35 / 09:50 AM EST hasta 04:00 PM EST)
* **Interfaz de Control:** Botón de Pánico WPF en Gráfica + Atajo `Ctrl + Espacio` (Inicia en PAUSA)

---

## 2. Parámetros Nativos de Fábrica (Micro MES)

### A. Gestión de Posición
* **Número de Contratos:** 3 contratos
  * **C1 (El Cajero Automático):** Cobra en la Tranca Universal o Escalón 1.
  * **C2 (El Francotirador de Tendencia):** Liquida con orden Limit fija al tocar Banda 3.5 exterior (`Exit_Ratchet_C2_Upper35` / `Lower35`).
  * **C3 (El Astronauta / Runner):** Surfea con el Piso Estructural de Banda 1.75 / Línea Media o Ahogamiento Parabólico Ceñido.

### B. Escudo Diario de Cuenta (3 Pisos de Protección - MES)
* **Cortacircuito Diario Inicial:** -$65.00 USD (2.5 a 3 vidas holgadas en MES).
* **Meta Escalón 1:** +$40.00 USD.
* **Piso Protegido Escalón 1:** +$25.00 USD (anclaje de cemento indestructible).
* **Meta Escalón 2:** +$60.00 USD.
* **Piso Protegido Escalón 2:** +$50.00 USD.
* **Umbral Modo Dulce (Piso 3):** +$90.00 USD (Activa Trailing Diario Infinito).
* **Colchón Trailing Infinito:** $15.00 USD de retroceso máximo desde el pico más alto del día.
* **Máximo Strikes Consecutivos:** 3 pérdidas seguidas apagan el bot por el resto de la sesión.
* **Corte de Mediodía:** 12:00 PM EST (Si se alcanzó Escalón 1, no permite nuevas entradas).

### C. Tranca Universal de Adamantium (MES)
* **Sprint Bonus:** +$10.00 USD adicionales tras clavar la tranca.
* **Colchón de Comisiones:** $1.00 USD.
* **Distancia Mínima Filtro Anti-Asfixia:** $12.50 USD (10 ticks de autopista mínima).
* **Colchón Conquista Adamantium:** $5.00 USD (4 ticks de penetración requerida).
* **Anclaje de Titanio sobre Escalón:** +$2.00 USD.

### D. Los 3 Injertos Especiales (MES)
1. **Injerto 1 (Franco-Tirador TP Fijo en Trade 1):** +$47.00 USD (38 ticks).
2. **Injerto 2 (Clímax Ratchet Asfixiante Trades 2+):** Meta Flash +$30.00 USD con respiro ceñido de 3 ticks (0.75 pts = $3.75 USD).
3. **Injerto 3 (Victoria Temprana Trade 1):** Meta de +$45.00 USD. Si el Trade 1 supera este umbral estando flat, ¡nos vamos a esquiar ⛷️ y se apaga el bot!

---

## 3. Auto-Escalado Inteligente Dual (Mini ES)
Si el usuario monta la estrategia en una gráfica del contrato Mini (`ES`), `State.DataLoaded` detecta que el instrumento no contiene `"MES"` y multiplica automáticamente todos los umbrales monetarios $\times 10$:
* Cortacircuito: -$650.00 USD
* Escalón 1: +$400.00 USD (Piso +$250)
* Escalón 2: +$600.00 USD (Piso +$500)
* Modo Dulce: +$900.00 USD (Giveback $150)
* Tranca Bonus: +$100.00 USD
* TP Fijo T1: +$470.00 USD
* Victoria Temprana T1: +$450.00 USD
