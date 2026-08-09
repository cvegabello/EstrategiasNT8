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

namespace NinjaTrader.NinjaScript.Indicators
{
	public class RTHBackground : Indicator
	{
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Pinta el fondo del gráfico según el horario (RTH vs. No-RTH).";
				Name										= "RTH Background";
				Calculate									= Calculate.OnBarClose;
				IsOverlay									= true; // Se dibuja sobre las velas
				DisplayInDataBox							= false;
				DrawOnPricePanel							= true;
				
				// Definimos los colores por defecto
				// Para RTH usamos un fondo un poco más claro (ej. gris oscuro o nulo si tu fondo ya es gris)
				// Para No-RTH oscurecemos el fondo
				RTHColor = Brushes.Transparent; 
				NonRTHColor = Brushes.Black;
			}
		}

		protected override void OnBarUpdate()
		{
			// Filtramos barras insuficientes
			if (CurrentBar < 1) return;

			// ¿Qué hace esto?: Convierte la hora de la barra actual en un número entero (ej. 93000 para las 9:30:00).
			// ¿Por qué es necesario?: Para comparar matemáticamente si estamos dentro del horario operativo.
			int currentTime = ToTime(Time[0]);
			
			// Evaluamos si estamos DENTRO de RTH (09:30 AM a 04:00 PM EST)
			if (currentTime >= 93000 && currentTime < 160000)
			{
				// Pintar el fondo de las horas operativas
				BackBrush = RTHColor;
			}
			else
			{
				// Pintar el fondo de las horas fuera de sesión
				BackBrush = NonRTHColor;
			}
		}

		#region Properties
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="Color RTH (Operativo)", Description="Color para el horario 09:30 a 16:00", Order=1, GroupName="Parámetros")]
		public Brush RTHColor
		{ get; set; }

		[Browsable(false)]
		public string RTHColorSerializable
		{
			get { return Serialize.BrushToString(RTHColor); }
			set { RTHColor = Serialize.StringToBrush(value); }
		}			

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="Color No-RTH (Fuera de Horario)", Description="Color para fuera del horario RTH", Order=2, GroupName="Parámetros")]
		public Brush NonRTHColor
		{ get; set; }

		[Browsable(false)]
		public string NonRTHColorSerializable
		{
			get { return Serialize.BrushToString(NonRTHColor); }
			set { NonRTHColor = Serialize.StringToBrush(value); }
		}
		#endregion
	}
}
