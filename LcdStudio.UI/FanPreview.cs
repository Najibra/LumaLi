using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using LcdStudio.Core;

namespace LcdStudio.UI;

public sealed class FanPreview : Control
{
	private readonly Engine _engine;

	private readonly Faces _faces = new Faces();

	private Bitmap? _local;

	private DateTime _last;

	public string Serial { get; set; } = "";


	public FanScreen? Fan { get; set; }

	private OutputStatus? Output => _engine.FanOutputs.FirstOrDefault((OutputStatus f) => f.Serial == Serial);

	public FanPreview(Engine engine)
	{
		_engine = engine;
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
	}

	public void Forget(string? path)
	{
		_faces.Forget(path);
	}

	public void Tick()
	{
		if (!base.Visible)
		{
			return;
		}
		OutputStatus output = Output;
		if (output != null && output.Running && output.Preview != null)
		{
			Invalidate();
		}
		else if (!((DateTime.UtcNow - _last).TotalMilliseconds < 120.0) && Fan != null)
		{
			_last = DateTime.UtcNow;
			FanScreen fanScreen;
			lock (_engine.Settings)
			{
				fanScreen = new FanScreen
				{
					Face = Fan.Face,
					Sensor = Fan.Sensor,
					Label = Fan.Label,
					Color1 = Fan.Color1,
					Color2 = Fan.Color2,
					Rotation = Fan.Rotation,
					Background = Fan.Background
				};
			}
			Bitmap local = _faces.Render(fanScreen, _engine.Sensors.Get(fanScreen.Sensor), _engine.Time);
			Bitmap? local2 = _local;
			_local = local;
			local2?.Dispose();
			Invalidate();
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.Clear((base.Parent is Card) ? Theme.Card : (base.Parent?.BackColor ?? Theme.Window));
		Theme.Smooth(graphics);
		float num = Math.Min(base.Width, base.Height) - Theme.S(4f);
		RectangleF rect = new RectangleF(((float)base.Width - num) / 2f, ((float)base.Height - num) / 2f, num, num);
		using (SolidBrush brush = new SolidBrush(Color.FromArgb(6, 6, 7)))
		{
			graphics.FillEllipse(brush, RectangleF.Inflate(rect, Theme.S(2f), Theme.S(2f)));
		}
		GraphicsState gstate = graphics.Save();
		using (GraphicsPath graphicsPath = new GraphicsPath())
		{
			graphicsPath.AddEllipse(rect);
			graphics.SetClip(graphicsPath);
		}
		graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
		OutputStatus output = Output;
		if (output != null && output.Running)
		{
			lock (output)
			{
				if (output.Preview != null)
				{
					graphics.DrawImage(output.Preview, rect);
				}
			}
		}
		else if (_local != null)
		{
			graphics.DrawImage(_local, rect);
		}
		graphics.Restore(gstate);
		using Pen pen = new Pen(Theme.FieldEdge, 1.5f);
		graphics.DrawEllipse(pen, rect);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_faces.Dispose();
			_local?.Dispose();
		}
		base.Dispose(disposing);
	}
}
