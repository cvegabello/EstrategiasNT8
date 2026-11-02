# Propuesta Estratégica Institucional: Filtro de Barreras S/R ("Room to Run")

> **Estado:** En "Agua Tibia" (Backlog Estratégico de Optimización - Post Auditoría v12.0)  
> **Fecha de Creación:** 01 de Noviembre, 2026  
> **Autor/Mentor:** Antigravity & Carlos Vega  

---

## 1. El Diagnóstico (Caso de Estudio: 25/09/2026 11:45 AM)
- **Contexto:** Cascada Short disparó a las 11:45:50 AM en `7767.25` con pendiente normalizada muy fuerte ($-0.21$, confirmada por 5 barras).
- **El Problema:** El soporte mayor de la sesión (suelo previo de 10:56 - 11:09 AM) estaba exactamente en `7765.25` (a solo **8 ticks** de distancia).
- **El Resultado:** El mercado chocó contra el soporte, rebotó en seco, sacó el stop y luego despegó en un rally de +$608 USD.
- **Lección:** Ningún sistema de momentum o tendencia debe vender si está a menos de 10-12 ticks de un soporte clave, ni comprar si está a menos de 10-12 ticks de una resistencia clave.

---

## 2. Lógica Matemática del Filtro "Room to Run"

```csharp
// Parámetro de Pista Libre
private int MinRoomToRunTicks = 10; // Mínimo 10 ticks (2.5 pts) de espacio antes del muro

// Evaluación en Corto:
double currentSessionLow = Lowest(Low, CurrentBar); // O mínimo de ventana de 200 barras
double distanceToFloor = (Close[0] - currentSessionLow) / TickSize;

// Si el precio aún está por encima del suelo pero a menos de 10 ticks del muro:
if (Close[0] >= currentSessionLow && distanceToFloor < MinRoomToRunTicks)
{
    // SEMÁFORO ROJO: Bloquea el tiro para no estrellarse
    return;
}
// Si el precio perfora el suelo (Ruptura Real / Breakout):
// distanceToFloor < 0 => SEMÁFORO VERDE HABILITADO

// Evaluación en Largo:
double currentSessionHigh = Highest(High, CurrentBar);
double distanceToCeiling = (currentSessionHigh - Close[0]) / TickSize;

if (Close[0] <= currentSessionHigh && distanceToCeiling < MinRoomToRunTicks)
{
    // SEMÁFORO ROJO: Bloquea la compra en el techo
    return;
}
```

---

## 3. Hoja de Ruta
- Mantener en observación mientras se auditan las 30 sesiones de la **Versión 12.0**.
- Si el patrón de "comprar en resistencia o vender en soporte" se repite como fuga sistemática, se implementará formalmente en una subversión dedicada con pruebas cuantitativas de impacto.
