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
		// the mark draws itself over the first second
		float m = 84f * k;
		float mx = size.Width / 2f - 118f * k, my = size.Height / 2f - m / 2f - 10f * k;
		Brand.Mark(g, new RectangleF(mx, my, m, m), Theme.Text, Math.Clamp(t / 1.1f, 0f, 1f));
		// the wordmark rises and fades in after it
		float w = Math.Clamp((t - 0.55f) / 0.6f, 0f, 1f);
		float ease = 1f - (1f - w) * (1f - w) * (1f - w);
		Brand.Word(g, "lumali", mx + m + 22f * k, my + m / 2f + (1f - ease) * 14f * k, 44f * k, Theme.Text, ease);
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
