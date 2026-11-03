using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace LcdStudio.UI;

/// <summary>
/// The startup screen: a small dark card where the mark draws itself (the ring sweeps round, the bar slides in, the
/// red dot pops), the wordmark rises in beside it, and a thin line runs along the foot while LumaLi starts. It runs
/// on its own thread, so it keeps moving while the sensors and devices open, and fades out once the window is up.
/// </summary>
internal sealed class Splash : Form
{
	public const int W = 520, H = 300;

	private readonly Stopwatch _clock = Stopwatch.StartNew();

	private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 15 };

	private double _closeAt = double.NaN;

	private static string _status = "Starting";

	private Splash()
	{
		FormBorderStyle = FormBorderStyle.None;
		StartPosition = FormStartPosition.CenterScreen;
		ShowInTaskbar = false;
		TopMost = true;
		BackColor = Theme.Window;
		ClientSize = new Size(Theme.S(W), Theme.S(H));
		DoubleBuffered = true;
		_timer.Tick += delegate
		{
			double t = _clock.Elapsed.TotalSeconds;
			if (!double.IsNaN(_closeAt))
			{
				double k = (t - _closeAt) / 0.35;
				if (k >= 1.0)
				{
					_timer.Stop();
					Close();
					return;
				}
				Opacity = Math.Clamp(1.0 - k, 0.0, 1.0);
			}
			Invalidate();
		};
		_timer.Start();
	}

	protected override bool ShowWithoutActivation => true;

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		try
		{
			// rounded corners on Windows 11
			int round = 2;
			DwmSetWindowAttribute(Handle, 33, ref round, 4);
		}
		catch
		{
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Draw(e.Graphics, ClientSize, (float)_clock.Elapsed.TotalSeconds, _status);
	}

	/// <summary>One frame of the startup screen at time t (seconds), for the window and for previews.</summary>
	public static void Draw(Graphics g, Size size, float t, string status)
	{
		g.Clear(Theme.Window);
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
		float k = size.Width / (float)W;
		// a hairline edge so the card reads on a dark desktop
		using (var edge = new Pen(Theme.CardEdge))
		{
			g.DrawRectangle(edge, 0, 0, size.Width - 1, size.Height - 1);
		}
		// a white station clock builds itself and swings to the real time
		float dr = 84f * k, dcx = 150f * k, dcy = 128f * k;
		StationDial(g, dcx, dcy, dr, t, DateTime.Now);
		// the wordmark rises and fades in after it
		float w = Math.Clamp((t - 0.55f) / 0.6f, 0f, 1f);
		float ease = 1f - (1f - w) * (1f - w) * (1f - w);
		Brand.Word(g, "lumali", 274f * k, dcy - 8f * k + (1f - ease) * 14f * k, 48f * k, Theme.Text, ease);
		using (var small = new Font(Theme.TextFamily, 8f * k, GraphicsUnit.Point))
		using (var sb = new SolidBrush(Color.FromArgb((int)(255 * Math.Clamp((t - 0.85f) / 0.5f, 0f, 1f)), Theme.SubText)))
		{
			g.DrawString(DateTime.Now.ToString("dddd d MMMM").ToUpperInvariant(), small, sb, 278f * k, dcy + 30f * k);
		}
		// the line along the foot: a short accent segment gliding across a faint track
		float y = size.Height - 46f * k, x0 = 60f * k, x1 = size.Width - 60f * k;
		using (var track = new Pen(Theme.CardEdge, Math.Max(1f, 2f * k)))
		{
			g.DrawLine(track, x0, y, x1, y);
		}
		float p = (t * 0.7f) % 1f;
		float seg = 0.28f;
		float a = x0 + (x1 - x0) * Math.Clamp(p * (1f + seg) - seg, 0f, 1f), b = x0 + (x1 - x0) * Math.Clamp(p * (1f + seg), 0f, 1f);
		if (b > a)
		{
			using var lit = new Pen(Theme.Accent, Math.Max(1f, 2f * k)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
			g.DrawLine(lit, a, y, b, y);
		}
		using var font = new Font(Theme.TextFamily, 8.5f * k, GraphicsUnit.Point);
		using var brush = new SolidBrush(Color.FromArgb((int)(255 * Math.Clamp((t - 0.9f) / 0.5f, 0f, 1f)), Theme.SubText));
		using var sf = new StringFormat { Alignment = StringAlignment.Center };
		g.DrawString(status, font, brush, new RectangleF(0, y + 12f * k, size.Width, 24f * k), sf);
	}

	/// <summary>
	/// A white station clock: the face fades up, the markers appear round it like a sweep, the black hands swing up to
	/// the time with a little overshoot, then the slim red second hand (the mark's red dot rides on it as a ring)
	/// sweeps round in 58.5 s and waits at twelve.
	/// </summary>
	private static void StationDial(Graphics g, float cx, float cy, float r, float t, DateTime now)
	{
		static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);
		static float Back(float u)
		{
			u = Clamp01(u) - 1f;
			return 1f + u * u * (3.0f * u + 2.0f);
		}
		static PointF At(float cx, float cy, float deg, float rr)
		{
			float a = deg * MathF.PI / 180f;
			return new PointF(cx + MathF.Sin(a) * rr, cy - MathF.Cos(a) * rr);
		}
		void Bar(float deg, float r0, float r1, float width, Color c)
		{
			var st = g.Save();
			g.TranslateTransform(cx, cy);
			g.RotateTransform(deg);
			using var b = new SolidBrush(c);
			g.FillRectangle(b, -width / 2f, -r1, width, r1 - r0);
			g.Restore(st);
		}
		void Hand(float deg, float back, float len, float wBase, float wTip, Color c)
		{
			var st = g.Save();
			g.TranslateTransform(cx, cy);
			g.RotateTransform(deg);
			using var b = new SolidBrush(c);
			g.FillPolygon(b, new[] { new PointF(-wBase / 2f, back), new PointF(wBase / 2f, back), new PointF(wTip / 2f, -len), new PointF(-wTip / 2f, -len) });
			g.Restore(st);
		}

		float face = Clamp01(t / 0.35f);
		if (face <= 0f)
		{
			return;
		}
		face = 1f - (1f - face) * (1f - face);
		float fr = r * (0.9f + 0.1f * face);
		int fa = (int)(255 * face);
		using (var rim = new LinearGradientBrush(new RectangleF(cx - fr - 6f, cy - fr - 6f, (fr + 6f) * 2f, (fr + 6f) * 2f), Color.FromArgb(fa, 236, 236, 238), Color.FromArgb(fa, 120, 122, 126), 90f))
		{
			g.FillEllipse(rim, cx - fr - 5f, cy - fr - 5f, (fr + 5f) * 2f, (fr + 5f) * 2f);
		}
		using (var path = new GraphicsPath())
		{
			path.AddEllipse(cx - fr, cy - fr, fr * 2f, fr * 2f);
			using var shade = new PathGradientBrush(path) { CenterPoint = new PointF(cx, cy - fr * 0.35f), CenterColor = Color.FromArgb(fa, 255, 255, 255), SurroundColors = new[] { Color.FromArgb(fa, 228, 228, 230) } };
			g.FillPath(shade, path);
		}
		Color ink = Color.FromArgb(22, 22, 24);
		// the markers appear clockwise
		for (int i = 0; i < 60; i++)
		{
			float a = Clamp01((t - 0.12f - i / 60f * 0.6f) / 0.08f);
			if (a <= 0f)
			{
				continue;
			}
			Color c = Color.FromArgb((int)(255 * a), ink);
			if (i % 5 == 0)
			{
				Bar(i * 6f, r * 0.70f, r * 0.94f, r * 0.08f, c);
			}
			else
			{
				Bar(i * 6f, r * 0.86f, r * 0.94f, r * 0.026f, c);
			}
		}
		// the hands swing up from twelve to the time
		float s = now.Second + now.Millisecond / 1000f;
		float swing = Back((t - 0.55f) / 0.65f);
		if (t > 0.55f)
		{
			float hour = (now.Hour % 12 + now.Minute / 60f) * 30f, minute = now.Minute * 6f;
			Hand(hour * swing, r * 0.22f, r * 0.62f, r * 0.11f, r * 0.09f, ink);
			Hand(minute * swing, r * 0.22f, r * 0.92f, r * 0.09f, r * 0.065f, ink);
		}
		// the red second hand: it fades in at twelve, then sweeps from there to catch up with the real seconds
		float sa = Clamp01((t - 1.0f) / 0.25f);
		if (sa > 0f)
		{
			float real = Math.Min(s / 58.5f, 1f) * 360f;
			float catchUp = Clamp01((t - 1.0f) / 0.7f);
			catchUp = 1f - (1f - catchUp) * (1f - catchUp) * (1f - catchUp);
			float deg = real * catchUp;
			Color red = Color.FromArgb((int)(255 * sa), Brand.Red);
			float w = Math.Max(1.5f, r * 0.026f);
			Hand(deg, r * 0.28f, r * 0.58f, w, w, red);
			Hand(deg, -r * 0.74f, r * 0.9f, w, w, red);
			PointF p = At(cx, cy, deg, r * 0.66f);
			using (var pen = new Pen(red, w * 1.25f))
			{
				g.DrawEllipse(pen, p.X - r * 0.075f, p.Y - r * 0.075f, r * 0.15f, r * 0.15f);
			}
			using var dot = new SolidBrush(red);
			g.FillEllipse(dot, cx - r * 0.045f, cy - r * 0.045f, r * 0.09f, r * 0.09f);
		}
	}

	private static Splash? _current;

	private static readonly ManualResetEventSlim _ready = new ManualResetEventSlim();

	/// <summary>Shows the startup screen on its own thread and returns at once.</summary>
	public static void Begin()
	{
		var thread = new Thread(() =>
		{
			try
			{
				_current = new Splash();
				_current.HandleCreated += delegate
				{
					_ready.Set();
				};
				Application.Run(_current);
			}
			catch
			{
				_ready.Set();
			}
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.IsBackground = true;
		thread.Name = "splash";
		thread.Start();
		_ready.Wait(1500);
	}

	/// <summary>The line under the logo.</summary>
	public static void Status(string text)
	{
		_status = text;
	}

	/// <summary>Fades the startup screen out, once it has shown for at least 1.4 seconds.</summary>
	public static void End()
	{
		Splash? s = _current;
		if (s == null || !s.IsHandleCreated)
		{
			return;
		}
		try
		{
			s.BeginInvoke(() =>
			{
				double t = s._clock.Elapsed.TotalSeconds;
				s._closeAt = Math.Max(t, 1.4);
			});
		}
		catch
		{
		}
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);
}
