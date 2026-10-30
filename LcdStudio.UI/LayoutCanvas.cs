using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using LcdStudio.Core;

namespace LcdStudio.UI;

public sealed class LayoutCanvas : Control
{
	private enum Drag
	{
		None,
		Move,
		Resize
	}

	private readonly Engine _engine;

	private readonly ScreenRenderer _renderer;

	private Bitmap? _frame;

	private DateTime _lastRender;

	private Widget? _selected;

	private Widget? _clipboard;

	private Point _dragStart;

	private Rectangle _startRect;

	private Drag _drag;

	private bool _dragChanged;

	private readonly List<(float Pos, bool Vertical)> _guides = new List<(float, bool)>();

	private readonly Stack<string> _undo = new Stack<string>();

	private readonly Stack<string> _redo = new Stack<string>();

	public bool Snap { get; set; } = true;


	public Func<ScreenLayout>? Model { get; set; }

	public Func<ScreenLayout>? BeginEdit { get; set; }

	public Widget? Selected
	{
		get
		{
			return _selected;
		}
		set
		{
			_selected = value;
			Invalidate();
			this.SelectionChanged?.Invoke(value);
		}
	}

	public bool CanUndo => _undo.Count > 0;

	public bool CanRedo => _redo.Count > 0;

	private RectangleF Area
	{
		get
		{
			float num = 4f;
			float num2 = base.Width - Theme.S(8f);
			float num3 = num2 / num;
			if (num3 > (float)(base.Height - Theme.S(8f)))
			{
				num3 = base.Height - Theme.S(8f);
				num2 = num3 * num;
			}
			return new RectangleF(((float)base.Width - num2) / 2f, ((float)base.Height - num3) / 2f, num2, num3);
		}
	}

	private float Zoom => Area.Width / 1920f;

	public event Action<Widget?>? SelectionChanged;

	public event Action? Edited;

	public LayoutCanvas(Engine engine)
	{
		_engine = engine;
		_renderer = new ScreenRenderer(engine.Sensors);
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		base.TabStop = true;
	}

	private RectangleF ToScreen(Widget w)
	{
		RectangleF area = Area;
		return new RectangleF(area.X + (float)w.X * Zoom, area.Y + (float)w.Y * Zoom, (float)w.W * Zoom, (float)w.H * Zoom);
	}

	public void Tick(bool force = false)
	{
		if (base.Visible && Model != null && (force || !((DateTime.UtcNow - _lastRender).TotalMilliseconds < 150.0)))
		{
			_lastRender = DateTime.UtcNow;
			ScreenLayout layout;
			lock (_engine.Settings)
			{
				layout = Model().Clone();
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
		graphics.Clear(Theme.Window);
		Theme.Smooth(graphics);
		RectangleF area = Area;
		Theme.FillRound(graphics, RectangleF.Inflate(area, Theme.S(5f), Theme.S(5f)), Theme.S(8f), Color.FromArgb(10, 12, 26), Focused ? Theme.Cyan : Color.FromArgb(90, 100, 160));
		if (_frame != null)
		{
			graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
			graphics.DrawImage(_frame, area);
		}
		else
		{
			Tick(force: true);
		}
		if (Model != null)
		{
			using Pen pen = new Pen(Color.FromArgb(60, 255, 255, 255))
			{
				DashStyle = DashStyle.Dot
			};
			foreach (Widget item in Model().Widgets.Where((Widget w) => w.Visible && w != _selected))
			{
				if (Focused)
				{
					graphics.DrawRectangle(pen, Rectangle.Round(ToScreen(item)));
				}
			}
		}
		if (_selected != null)
		{
			RectangleF rectangleF = ToScreen(_selected);
			using Pen pen2 = new Pen(Color.FromArgb(255, 200, 0), 1.5f)
			{
				DashStyle = DashStyle.Dash
			};
			graphics.DrawRectangle(pen2, rectangleF.X, rectangleF.Y, rectangleF.Width, rectangleF.Height);
			using SolidBrush brush = new SolidBrush(Color.FromArgb(255, 200, 0));
			graphics.FillRectangle(brush, rectangleF.Right - (float)Theme.S(5f), rectangleF.Bottom - (float)Theme.S(5f), Theme.S(10f), Theme.S(10f));
			Theme.DrawText(graphics, $"{_selected.Kind}  {_selected.X},{_selected.Y}  {_selected.W}×{_selected.H}", Theme.Small, Color.FromArgb(255, 220, 90), new RectangleF(rectangleF.X, Math.Max(0f, rectangleF.Y - (float)Theme.S(18f)), Theme.S(260f), Theme.S(16f)));
		}
		using Pen pen3 = new Pen(Color.FromArgb(200, 255, 60, 200));
		foreach (var guide in _guides)
		{
			var (num, _) = guide;
			if (guide.Vertical)
			{
				float num2 = area.X + num * Zoom;
				graphics.DrawLine(pen3, num2, area.Y, num2, area.Bottom);
			}
			else
			{
				float num3 = area.Y + num * Zoom;
				graphics.DrawLine(pen3, area.X, num3, area.Right, num3);
			}
		}
	}

	private void Snapshot()
	{
		if (Model == null)
		{
			return;
		}
		_undo.Push(Store.ToJson(Model()));
		if (_undo.Count > 60)
		{
			List<string> list = _undo.Take(60).Reverse().ToList();
			_undo.Clear();
			foreach (string item in list)
			{
				_undo.Push(item);
			}
		}
		_redo.Clear();
	}

	public void Mutate(Action<ScreenLayout> change)
	{
		ScreenLayout obj = BeginEdit?.Invoke() ?? Model();
		Snapshot();
		lock (_engine.Settings)
		{
			change(obj);
		}
		AfterEdit();
	}

	private void AfterEdit()
	{
		Tick(force: true);
		this.Edited?.Invoke();
		_engine.SaveSoon();
	}

	public void Undo()
	{
		Restore(_undo, _redo);
	}

	public void Redo()
	{
		Restore(_redo, _undo);
	}

	private void Restore(Stack<string> from, Stack<string> to)
	{
		if (from.Count == 0 || Model == null)
		{
			return;
		}
		ScreenLayout screenLayout = Model();
		to.Push(Store.ToJson(screenLayout));
		ScreenLayout screenLayout2 = Store.LayoutFromJson(from.Pop());
		if (screenLayout2 != null)
		{
			int num = ((_selected == null) ? (-1) : screenLayout.Widgets.IndexOf(_selected));
			lock (_engine.Settings)
			{
				screenLayout.Widgets = screenLayout2.Widgets;
				screenLayout.Background = screenLayout2.Background;
				screenLayout.Color1 = screenLayout2.Color1;
				screenLayout.Color2 = screenLayout2.Color2;
				screenLayout.Accent = screenLayout2.Accent;
				screenLayout.Image = screenLayout2.Image;
				screenLayout.ImageOpacity = screenLayout2.ImageOpacity;
			}
			Selected = ((num >= 0 && num < screenLayout.Widgets.Count) ? screenLayout.Widgets[num] : null);
			AfterEdit();
		}
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		MouseEventArgs e2 = e;
		Focus();
		if (Model == null || e2.Button != MouseButtons.Left)
		{
			return;
		}
		if (_selected != null)
		{
			RectangleF rectangleF = ToScreen(_selected);
			if (Math.Abs((float)e2.X - rectangleF.Right) < (float)Theme.S(9f) && Math.Abs((float)e2.Y - rectangleF.Bottom) < (float)Theme.S(9f))
			{
				Start(e2.Location, Drag.Resize);
				return;
			}
		}
		Widget widget = Model().Widgets.LastOrDefault((Widget w) => w.Visible && ToScreen(w).Contains(e2.Location));
		if (widget != _selected)
		{
			Selected = widget;
		}
		if (widget != null)
		{
			Start(e2.Location, Drag.Move);
		}
		Invalidate();
	}

	private void Start(Point p, Drag kind)
	{
		_dragStart = p;
		_startRect = new Rectangle(_selected.X, _selected.Y, _selected.W, _selected.H);
		_drag = kind;
		_dragChanged = false;
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		if (_selected != null && _drag == Drag.None)
		{
			RectangleF rectangleF = ToScreen(_selected);
			Cursor = ((Math.Abs((float)e.X - rectangleF.Right) < (float)Theme.S(9f) && Math.Abs((float)e.Y - rectangleF.Bottom) < (float)Theme.S(9f)) ? Cursors.SizeNWSE : (rectangleF.Contains(e.Location) ? Cursors.SizeAll : Cursors.Default));
		}
		if (_drag == Drag.None || _selected == null || Model == null)
		{
			return;
		}
		int num = (int)Math.Round((float)(e.X - _dragStart.X) / Zoom);
		int num2 = (int)Math.Round((float)(e.Y - _dragStart.Y) / Zoom);
		if (!_dragChanged)
		{
			if (Math.Abs(e.X - _dragStart.X) + Math.Abs(e.Y - _dragStart.Y) < 3)
			{
				return;
			}
			int num3 = Model().Widgets.IndexOf(_selected);
			ScreenLayout screenLayout = BeginEdit?.Invoke() ?? Model();
			if (num3 >= 0 && num3 < screenLayout.Widgets.Count)
			{
				_selected = screenLayout.Widgets[num3];
			}
			Snapshot();
			_dragChanged = true;
		}
		_guides.Clear();
		lock (_engine.Settings)
		{
			if (_drag == Drag.Resize)
			{
				var (num4, flag) = SnapTo(_startRect.Right + num, TargetsX());
				var (num5, flag2) = SnapTo(_startRect.Bottom + num2, TargetsY());
				_selected.W = Math.Max(8, num4 - _selected.X);
				_selected.H = Math.Max(8, num5 - _selected.Y);
				if (flag)
				{
					_guides.Add((num4, true));
				}
				if (flag2)
				{
					_guides.Add((num5, false));
				}
			}
			else
			{
				int x = _startRect.X + num;
				int y = _startRect.Y + num2;
				int width = _startRect.Width;
				int height = _startRect.Height;
				List<int> targets = TargetsX().ToList();
				((int, bool), int) tuple3 = (from c in new((int, bool), int)[3]
					{
						(SnapTo(x, targets), 0),
						(SnapTo(x + width / 2, targets), width / 2),
						(SnapTo(x + width, targets), width)
					}
					orderby (!c.Item1.Item2) ? 1 : 0, Math.Abs(c.Item1.Item1 - c.Item2 - x)
					select c).First();
				_selected.X = tuple3.Item1.Item1 - tuple3.Item2;
				if (tuple3.Item1.Item2)
				{
					_guides.Add((tuple3.Item1.Item1, true));
				}
				List<int> targets2 = TargetsY().ToList();
				((int, bool), int) tuple4 = (from c in new((int, bool), int)[3]
					{
						(SnapTo(y, targets2), 0),
						(SnapTo(y + height / 2, targets2), height / 2),
						(SnapTo(y + height, targets2), height)
					}
					orderby (!c.Item1.Item2) ? 1 : 0, Math.Abs(c.Item1.Item1 - c.Item2 - y)
					select c).First();
				_selected.Y = tuple4.Item1.Item1 - tuple4.Item2;
				if (tuple4.Item1.Item2)
				{
					_guides.Add((tuple4.Item1.Item1, false));
				}
			}
		}
		Tick(force: true);
		this.SelectionChanged?.Invoke(_selected);
	}

	private IEnumerable<int> TargetsX()
	{
		return new int[3] { 0, 960, 1920 }.Concat(Model().Widgets.Where((Widget w) => w != _selected && w.Visible).SelectMany((Widget w) => new int[3]
		{
			w.X,
			w.X + w.W / 2,
			w.X + w.W
		}));
	}

	private IEnumerable<int> TargetsY()
	{
		return new int[3] { 0, 240, 480 }.Concat(Model().Widgets.Where((Widget w) => w != _selected && w.Visible).SelectMany((Widget w) => new int[3]
		{
			w.Y,
			w.Y + w.H / 2,
			w.Y + w.H
		}));
	}

	private (int Value, bool Guide) SnapTo(int v, IEnumerable<int> targets)
	{
		if (!Snap || Control.ModifierKeys.HasFlag(Keys.Alt))
		{
			return (Value: v, Guide: false);
		}
		int num = Math.Max(3, (int)Math.Ceiling((float)Theme.S(7f) / Math.Max(0.05f, Zoom)));
		int item = 0;
		int num2 = int.MaxValue;
		foreach (int target in targets)
		{
			int num3 = Math.Abs(target - v);
			if (num3 < num2)
			{
				num2 = num3;
				item = target;
			}
		}
		if (num2 <= num)
		{
			return (Value: item, Guide: true);
		}
		return (Value: (int)Math.Round((double)v / 10.0) * 10, Guide: false);
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		if (_dragChanged)
		{
			AfterEdit();
		}
		_drag = Drag.None;
		_dragChanged = false;
		_guides.Clear();
		Invalidate();
	}

	protected override void OnMouseDoubleClick(MouseEventArgs e)
	{
		base.OnMouseDoubleClick(e);
		if (_selected != null)
		{
			this.SelectionChanged?.Invoke(_selected);
		}
	}

	protected override bool IsInputKey(Keys keyData)
	{
		Keys keys = keyData & Keys.KeyCode;
		if (((uint)(keys - 37) > 3u && keys != Keys.Delete) || 1 == 0)
		{
			return base.IsInputKey(keyData);
		}
		return true;
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		if (e.Control && e.KeyCode == Keys.Z)
		{
			if (e.Shift)
			{
				Redo();
			}
			else
			{
				Undo();
			}
		}
		else if (e.Control && e.KeyCode == Keys.Y)
		{
			Redo();
		}
		else if (e.Control && e.KeyCode == Keys.V)
		{
			Paste();
		}
		else
		{
			if (_selected == null)
			{
				return;
			}
			if (e.Control && e.KeyCode == Keys.C)
			{
				_clipboard = _selected.Clone();
				return;
			}
			if (e.Control && e.KeyCode == Keys.D)
			{
				Duplicate();
				return;
			}
			if (e.KeyCode == Keys.Delete)
			{
				DeleteSelected();
				return;
			}
			int num = ((!e.Shift) ? 1 : 10);
			var (dx, dy) = e.KeyCode switch
			{
				Keys.Left => (-num, 0), 
				Keys.Right => (num, 0), 
				Keys.Up => (0, -num), 
				Keys.Down => (0, num), 
				_ => (0, 0), 
			};
			if (dx != 0 || dy != 0)
			{
				EditSelected(delegate(Widget w)
				{
					w.X += dx;
					w.Y += dy;
				});
			}
		}
	}

	public void EditSelected(Action<Widget> change)
	{
		Action<Widget> change2 = change;
		if (_selected == null || Model == null)
		{
			return;
		}
		int index = Model().Widgets.IndexOf(_selected);
		Mutate(delegate(ScreenLayout l)
		{
			if (index >= 0 && index < l.Widgets.Count)
			{
				_selected = l.Widgets[index];
				change2(_selected);
			}
		});
		Invalidate();
		this.SelectionChanged?.Invoke(_selected);
	}

	public void Add(Widget w)
	{
		Widget w2 = w;
		Mutate(delegate(ScreenLayout l)
		{
			l.Widgets.Add(w2);
		});
		Selected = w2;
	}

	public void Duplicate()
	{
		if (_selected != null)
		{
			Widget widget = _selected.Clone();
			widget.X += 20;
			widget.Y += 20;
			Add(widget);
		}
	}

	public void Paste()
	{
		if (_clipboard != null)
		{
			Widget widget = _clipboard.Clone();
			widget.X += 20;
			widget.Y += 20;
			_clipboard = widget.Clone();
			Add(widget);
		}
	}

	public void DeleteSelected()
	{
		if (_selected == null || Model == null)
		{
			return;
		}
		int index = Model().Widgets.IndexOf(_selected);
		Mutate(delegate(ScreenLayout l)
		{
			if (index >= 0 && index < l.Widgets.Count)
			{
				l.Widgets.RemoveAt(index);
			}
		});
		Selected = null;
	}

	public void BringForward(bool toFront)
	{
		if (_selected != null && Model != null)
		{
			int index = Model().Widgets.IndexOf(_selected);
			Mutate(delegate(ScreenLayout l)
			{
				Widget widget = l.Widgets[index];
				l.Widgets.RemoveAt(index);
				l.Widgets.Insert(toFront ? l.Widgets.Count : 0, widget);
				_selected = widget;
			});
		}
	}

	public void Align(string how)
	{
		string how2 = how;
		EditSelected(delegate(Widget w)
		{
			switch (how2)
			{
			case "left":
				w.X = 0;
				break;
			case "center":
				w.X = (1920 - w.W) / 2;
				break;
			case "right":
				w.X = 1920 - w.W;
				break;
			case "top":
				w.Y = 0;
				break;
			case "middle":
				w.Y = (480 - w.H) / 2;
				break;
			case "bottom":
				w.Y = 480 - w.H;
				break;
			}
		});
	}

	public void ResetHistory()
	{
		_undo.Clear();
		_redo.Clear();
	}

	protected override void OnGotFocus(EventArgs e)
	{
		Invalidate();
		base.OnGotFocus(e);
	}

	protected override void OnLostFocus(EventArgs e)
	{
		Invalidate();
		base.OnLostFocus(e);
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
