using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using LcdStudio.Core;

namespace LcdStudio.UI;

public sealed class ScreenPreview : Control
{
	private readonly Engine _engine;

	private readonly ScreenRenderer _renderer;

	private Bitmap? _frame;

	private DateTime _last;

	public Func<ScreenLayout>? Source { get; set; }

	public int IntervalMs { get; set; } = 200;


	public bool Selected { get; set; }

	public ScreenPreview(Engine engine)
	{
		_engine = engine;
		_renderer = new ScreenRenderer(engine.Sensors);
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		Cursor = Cursors.Hand;
	}

	public void Tick(bool force = false)
	{
		if (base.Visible && Source != null && (force || !((DateTime.UtcNow - _last).TotalMilliseconds < (double)IntervalMs)))
		{
			_last = DateTime.UtcNow;
			ScreenLayout layout;
			lock (_engine.Settings)
			{
				layout = Source().Clone();
			}
			Bitmap frame = _renderer.Render(layout, _engine.Time);
			Bitmap? frame2 = _frame;
			_frame = frame;
			frame2?.Dispose();
			Invalidate();
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.Clear((base.Parent is Card) ? Theme.Card : (base.Parent?.BackColor ?? Theme.Window));
		Theme.Smooth(graphics);
		float num = 4f;
		float num2 = base.Width - Theme.S(4f);
		float num3 = num2 / num;
		if (num3 > (float)(base.Height - Theme.S(4f)))
		{
			num3 = base.Height - Theme.S(4f);
			num2 = num3 * num;
		}
		RectangleF rectangleF = new RectangleF(((float)base.Width - num2) / 2f, ((float)base.Height - num3) / 2f, num2, num3);
		Theme.FillRound(graphics, rectangleF, Theme.S(6f), Color.FromArgb(12, 14, 30), Selected ? Theme.Cyan : Color.FromArgb(90, 100, 160), Selected ? 2.5f : 1f);
		RectangleF rect = RectangleF.Inflate(rectangleF, -Theme.S(4f), -Theme.S(4f));
		if (_frame != null)
		{
			graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
			if (_frame.Height > _frame.Width)
			{
				// a vertical layout lies on its side here (its top to the left), as the wide card can't hold it upright
				GraphicsState state = graphics.Save();
				graphics.TranslateTransform(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
				graphics.RotateTransform(-90f);
				graphics.DrawImage(_frame, new RectangleF(-rect.Height / 2f, -rect.Width / 2f, rect.Height, rect.Width));
				graphics.Restore(state);
				string tag = "VERTICAL";
				SizeF size = graphics.MeasureString(tag, Theme.Small);
				RectangleF pill = new RectangleF(rect.Right - size.Width - Theme.S(14f), rect.Y + Theme.S(6f), size.Width + Theme.S(10f), size.Height + Theme.S(2f));
				Theme.FillRound(graphics, pill, pill.Height / 2f, Color.FromArgb(170, 10, 12, 26), Color.FromArgb(120, 140, 200, 255));
				Theme.DrawText(graphics, tag, Theme.Small, Color.FromArgb(220, 230, 240, 255), new RectangleF(pill.X + Theme.S(5f), pill.Y + Theme.S(1f), size.Width + Theme.S(2f), size.Height));
			}
			else
			{
				graphics.DrawImage(_frame, rect);
			}
		}
		else
		{
			Tick(force: true);
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_renderer.Dispose();
			_frame?.Dispose();
		}
		base.Dispose(disposing);
	}
}
