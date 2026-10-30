using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LcdStudio.Core;

namespace LcdStudio.UI;

public sealed class Sparkline : Control
{
	private readonly Engine _engine;

	public string Sensor { get; set; } = "cpu.load";


	public float Max { get; set; } = 100f;


	public Color Color { get; set; } = Theme.Cyan;


	public Sparkline(Engine engine)
	{
		_engine = engine;
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = Color.Transparent;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		Theme.Smooth(graphics);
		float[] array = _engine.Sensors.History(Sensor);
		if (array.Length < 2)
		{
			return;
		}
		float[] d;
		lock (array)
		{
			d = array.Skip(array.Length - 60).ToArray();
		}
		PointF[] points = d.Select((float v, int i) => new PointF((float)i * ((float)base.Width - 1f) / (float)(d.Length - 1), (float)(base.Height - 2) - Math.Clamp(v / Max, 0f, 1f) * (float)(base.Height - 4))).ToArray();
		using Pen pen = new Pen(Color, Theme.S(1.6f));
		graphics.DrawLines(pen, points);
	}
}
