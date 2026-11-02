#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// =========================================================================================================
// INDICADOR VISOR: PAS_PendienteNormalizada
// Desarrollado para Poker de Ases Sniper (PAS) en Gráficas de 610 Ticks (ES / MES).
// Calcula la Pendiente de Regresión Lineal de 5 barras sobre la Línea Media del Keltner (52, 3.5)
// y la normaliza dividiéndola por el ATR(14), obteniendo el ratio adimensional de aceleración institucional.
// =========================================================================================================

namespace NinjaTrader.NinjaScript.Indicators
{
    public class PAS_PendienteNormalizada : Indicator
    {
        private KeltnerChannel keltner;
        private ATR atr14;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = @"Visor de Pendiente Normalizada Institucional (LinReg 5 / ATR 14) para Poker de Ases Sniper.";
                Name                                        = "PAS_PendienteNormalizada";
                Calculate                                   = Calculate.OnBarClose;
                IsOverlay                                   = false; // Panel inferior separado
                DisplayInDataBox                            = true;
                DrawOnPricePanel                            = false;
                PaintPriceMarkers                           = true;
                ScaleJustification                          = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive                    = true;

                // Parámetros por defecto (idénticos a la estrategia PAS)
                KeltnerPeriod                               = 52;
                KeltnerMultiplier                           = 3.5;
                AtrPeriod                                   = 14;
                ThresholdNormal                             = 0.15;
                ThresholdHiper                              = 0.25;

                // Gráfica de Línea de Pendiente
                AddPlot(new Stroke(Brushes.DodgerBlue, 2), PlotStyle.Line, "PendienteNormalizada");

                // Líneas Guía Horizontales de Umbrales
                AddLine(Brushes.LimeGreen,  ThresholdHiper,  "Hiper Largo (+0.25)");
                AddLine(Brushes.ForestGreen, ThresholdNormal, "Umbral Cascada Largo (+0.15)");
                AddLine(Brushes.DimGray,     0.0,             "Cero (Neutral)");
                AddLine(Brushes.Crimson,    -ThresholdNormal, "Umbral Cascada Corto (-0.15)");
                AddLine(Brushes.Magenta,    -ThresholdHiper,  "Hiper Corto (-0.25)");
            }
            else if (State == State.DataLoaded)
            {
                keltner = KeltnerChannel(Close, KeltnerMultiplier, KeltnerPeriod);
                atr14   = ATR(AtrPeriod);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 15 || atr14 == null || keltner == null)
            {
                Value[0] = 0.0;
                return;
            }

            double atr = atr14[0];
            if (atr <= 0.0001)
            {
                Value[0] = 0.0;
                return;
            }

            // Regresión lineal exacta por mínimos cuadrados (OLS) sobre 5 barras centradas:
            // m = (2*Y[0] + 1*Y[1] - 1*Y[3] - 2*Y[4]) / 10.0
            double linRegSlope = ((2.0 * keltner.Midline[0]) + (1.0 * keltner.Midline[1]) - (1.0 * keltner.Midline[3]) - (2.0 * keltner.Midline[4])) / 10.0;

            // Ratio normalizado por volatilidad ATR(14)
            double normSlope = linRegSlope / atr;
            Value[0] = normSlope;

            // Color dinámico según aceleración institucional:
            if (normSlope >= ThresholdHiper)
                PlotBrushes[0][0] = Brushes.LimeGreen;      // Hiper-Aceleración Alcista
            else if (normSlope >= ThresholdNormal)
                PlotBrushes[0][0] = Brushes.ForestGreen;    // Aceleración Cascada Alcista
            else if (normSlope <= -ThresholdHiper)
                PlotBrushes[0][0] = Brushes.Magenta;        // Hiper-Aceleración Bajista
            else if (normSlope <= -ThresholdNormal)
                PlotBrushes[0][0] = Brushes.Crimson;        // Aceleración Cascada Bajista
            else
                PlotBrushes[0][0] = Brushes.DimGray;        // Rango / Mercado sin inercia
        }

        #region Propiedades Expuestas en NT8 UI
        [NinjaScriptProperty]
        [Range(5, 200)]
        [Display(Name = "Período Keltner", Description = "Período del Keltner Channel (52 por defecto)", GroupName = "1. Parámetros Keltner", Order = 1)]
        public int KeltnerPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 10.0)]
        [Display(Name = "Multiplicador Keltner", Description = "Multiplicador de la Banda Grande (3.5 por defecto)", GroupName = "1. Parámetros Keltner", Order = 2)]
        public double KeltnerMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Período ATR", Description = "Período del ATR para normalización (14 por defecto)", GroupName = "2. Normalización", Order = 1)]
        public int AtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 1.0)]
        [Display(Name = "Umbral Cascada Normal", Description = "Aceleración mínima para disparo estándar (0.15 por defecto)", GroupName = "3. Umbrales", Order = 1)]
        public double ThresholdNormal { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 2.0)]
        [Display(Name = "Umbral Hiper-Aceleración", Description = "Aceleración extrema para súper-tendencias (0.25 por defecto)", GroupName = "3. Umbrales", Order = 2)]
        public double ThresholdHiper { get; set; }
        #endregion
    }
}
