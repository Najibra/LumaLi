using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LcdStudio.Core;
using LcdStudio.Devices;
using LcdStudio.Native;
using LcdStudio.UI;

namespace LcdStudio;

public static class Program
{
	private static bool[] args0Reset = new bool[1];

	public const string ExitEventName = "Local\\LumaLi.Exit";

	[STAThread]
	private static int Main(string[] args)
	{
		string[] args2 = args;
		if (args2.Contains("--test"))
		{
			return SelfTest.Run(args2);
		}
		if (args2.Contains("--exit"))
		{
			return RequestExit();
		}
		if (args2.Contains("--devices"))
		{
			return ListDevices();
		}
		if (args2.Contains("--restart-stuck"))
		{
			List<UsbDeviceInfo> devices = WinUsb.Enumerate().Where(delegate(UsbDeviceInfo d)
			{
				DeviceKind kind = d.Kind;
				return ((uint)kind <= 1u || (uint)(kind - 3) <= 1u) ? true : false;
			}).ToList();
			try
			{
				using Engine engine2 = new Engine();
				return (!engine2.RestartDevices(devices, takeOver: false)) ? 2 : 0;
			}
			catch (Exception ex)
			{
				Log.Write("restart-stuck: " + ex);
				return 1;
			}
		}
		if (args2.Contains("--resync"))
		{
			string text = args2.SkipWhile((string a) => a != "--resync").Skip(1).FirstOrDefault() ?? ".";
			try
			{
				return Resync(text);
			}
			catch (Exception ex2)
			{
				File.WriteAllText(Path.Combine(text, "resync.txt"), ex2.ToString());
				return 1;
			}
		}
		if (args2.Contains("--probe"))
		{
			return Probe(args2.SkipWhile((string a) => a != "--probe").Skip(1).FirstOrDefault() ?? ".");
		}
		if (args2.Contains("--bench"))
		{
			return Bench(args2.SkipWhile((string a) => a != "--bench").Skip(1).FirstOrDefault() ?? ".");
		}
		if (args2.Contains("--render-glyph"))
		{
			string[] array = args2.SkipWhile((string x) => x != "--render-glyph").Skip(1).ToArray();
			using InflatedGlyphs inflatedGlyphs = Inflated.Render(array[0], "Segoe UI Black", bold: true, float.Parse(array[1]), Imaging.Hex("#1a2140", Color.Black), array[2], (array.Length > 4) ? array[4].Replace("\\n", "\n") : null);
			using Bitmap bitmap = new Bitmap(inflatedGlyphs.Bitmap.Width, inflatedGlyphs.Bitmap.Height);
			using (Graphics graphics = Graphics.FromImage(bitmap))
			{
				graphics.Clear(Color.FromArgb(4, 5, 10));
				graphics.DrawImageUnscaled(inflatedGlyphs.Bitmap, 0, 0);
			}
			bitmap.Save(array[3], ImageFormat.Png);
			return 0;
		}
		if (args2.Contains("--render-layout"))
		{
			string[] a2 = args2.SkipWhile((string x) => x != "--render-layout").Skip(1).ToArray();
			using Engine engine3 = new Engine();
			engine3.StartSensorsOnly();
			Thread.Sleep(2500);
			ScreenLayout layout = Templates.All().First((ScreenLayout l) => l.Name == a2[0]);
			using ScreenRenderer screenRenderer = new ScreenRenderer(engine3.Sensors);
			Directory.CreateDirectory(a2[1]);
			// --pin cpu.load=95,gpu.temp=80 holds readings for the stills; --demo-history first fills two minutes of made-up history
			string? pins = args2.SkipWhile((string x) => x != "--pin").Skip(1).FirstOrDefault();
			var pinned = pins?.Split(',').Select((string p) => p.Split('=')).ToDictionary((string[] p) => p[0], (string[] p) => float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));
			void Pin()
			{
				if (pinned == null)
				{
					return;
				}
				foreach (var kv in pinned)
				{
					engine3.Sensors.Set(kv.Key, kv.Value);
				}
			}
			if (args2.Contains("--demo-history"))
			{
				foreach (string key in new[] { "cpu.load", "gpu.load", "ram.load", "gpu.temp", "gpu.hotspot", "net.down", "net.up" })
				{
					float now = pinned != null && pinned.TryGetValue(key, out float pv) ? pv : engine3.Sensors.Get(key) ?? 0f;
					bool temp = key.EndsWith(".temp") || key.EndsWith(".hotspot");
					for (int i = 0; i < 120; i++)
					{
						double wave = Math.Sin(i * 0.13 + key.Length) * 0.5 + Math.Sin(i * 0.41) * 0.25;
						float v = temp ? now - 9f * (1f - i / 119f) + (float)wave * 2f : Math.Max(0f, now * (float)(0.75 + wave * 0.45) + (float)(wave + 0.5) * 6f);
						engine3.Sensors.Set(key, i == 119 ? now : v);
					}
				}
			}
			Pin();
			for (int j = 0; j < 4; j++)
			{
				using Bitmap bitmap2 = screenRenderer.Render(layout, (double)j * 6.0);
				bitmap2.Save(Path.Combine(a2[1], $"{a2[0]}-{j}.png"), ImageFormat.Png);
			}
			using (Bitmap bitmap3 = screenRenderer.Render(layout, 30.5))
			{
				bitmap3.Save(Path.Combine(a2[1], a2[0] + "-4.png"), ImageFormat.Png);
			}
			// extra moments to capture: --times 28.8,29.4 saves Name-at-28.8.png and so on
			string times = args2.SkipWhile((string x) => x != "--times").Skip(1).FirstOrDefault();
			if (times != null)
			{
				foreach (string part in times.Split(','))
				{
					double at = double.Parse(part, System.Globalization.CultureInfo.InvariantCulture);
					Pin();
					using Bitmap still = screenRenderer.Render(layout, at);
					if (args2.Contains("--strip"))
					{
						// --strip: the light strip's 60 colours when it matches the screen, as a bar under the still
						var leds = Lighting.FromScreen(still, 60);
						using var withStrip = new Bitmap(still.Width, still.Height + 48);
						using (Graphics sg = Graphics.FromImage(withStrip))
						{
							sg.Clear(Color.Black);
							sg.DrawImageUnscaled(still, 0, 0);
							for (int i = 0; i < leds.Length; i++)
							{
								using var led = new SolidBrush(Color.FromArgb(leds[i].R, leds[i].G, leds[i].B));
								sg.FillRectangle(led, i * still.Width / leds.Length + 2, still.Height + 8, still.Width / leds.Length - 4, 32);
							}
						}
						withStrip.Save(Path.Combine(a2[1], $"{a2[0]}-at-{part}.png"), ImageFormat.Png);
						continue;
					}
					still.Save(Path.Combine(a2[1], $"{a2[0]}-at-{part}.png"), ImageFormat.Png);
				}
			}
			Stopwatch stopwatch = Stopwatch.StartNew();
			for (int k = 0; k < 60; k++)
			{
				screenRenderer.Render(layout, (double)k / 15.0).Dispose();
			}
			File.WriteAllText(Path.Combine(a2[1], "cost.txt"), $"{stopwatch.Elapsed.TotalMilliseconds / 60.0:0.0} ms per frame");
			return 0;
		}
		if (args2.Contains("--clock-test"))
		{
			// --clock-test <dir>: tries the screen's built-in clock. Each step first shows its number on the screen, then
			// turns the firmware clock on and stops sending frames for 15 s, so whoever is watching can see whether
			// the screen draws its own clock. Close LumaLi first.
			string dir = args2.SkipWhile((string x) => x != "--clock-test").Skip(1).FirstOrDefault() ?? ".";
			Directory.CreateDirectory(dir);
			string log = Path.Combine(dir, "clock-test.txt");
			File.WriteAllText(log, "");
			void Note(string s)
			{
				string line = $"{DateTime.Now:HH:mm:ss} {s}";
				File.AppendAllText(log, line + Environment.NewLine);
				Console.WriteLine(line);
			}
			byte[] Card(string big, string small)
			{
				using var bmp = new Bitmap(1920, 480);
				using (Graphics g = Graphics.FromImage(bmp))
				{
					g.Clear(Color.FromArgb(10, 12, 24));
					g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
					using var f1 = new Font("Segoe UI Semibold", 96f, GraphicsUnit.Pixel);
					using var f2 = new Font("Segoe UI", 34f, GraphicsUnit.Pixel);
					using var c = new StringFormat { Alignment = StringAlignment.Center };
					g.DrawString(big, f1, Brushes.White, new RectangleF(0, 110, 1920, 130), c);
					g.DrawString(small, f2, Brushes.LightSteelBlue, new RectangleF(0, 280, 1920, 60), c);
				}
				bmp.RotateFlip(RotateFlipType.Rotate90FlipNone);
				return Imaging.Jpeg(bmp, 90);
			}
			UsbDeviceInfo screenInfo = WinUsb.Enumerate().FirstOrDefault((UsbDeviceInfo d) => d.Kind == DeviceKind.Screen);
			if (screenInfo == null)
			{
				Note("no 8.8\" screen found");
				return 1;
			}
			using (LcdLink link = new LcdLink(screenInfo))
			{
				link.SendFrame(Card("CLOCK TEST", "watch this screen · 4 short steps · about a minute"), 80);
				Note("firmware " + link.Firmware);
				Thread.Sleep(4000);
				(string, bool, byte)[] steps = { ("1", true, 2), ("2", true, 1), ("3", true, 0), ("4", true, 3) };
				foreach (var (name, on, style) in steps)
				{
					link.SendFrame(Card("STEP " + name, "if a clock appears in the next 15 seconds, note the step number"), 80);
					Thread.Sleep(2500);
					Note($"step {name}: clock on, style {style}: " + link.Clock(on, style));
					Thread.Sleep(15000);
					Note($"step {name}: clock off: " + link.Clock(on: false, style));
				}
				link.SendFrame(Card("TEST DONE", "tell Claude which step showed a clock, if any"), 80);
				Note("done");
			}
			return 0;
		}
		if (args2.Contains("--render-face"))
		{
			// --render-face Lightbeam <dir> --sensor gpu.load --values 30,30,80 --times 1,1.05,1.1: one still per time,
			// the reading at each from the matching entry of --values (or the last one), plus the per-frame cost
			string[] a3 = args2.SkipWhile((string x) => x != "--render-face").Skip(1).ToArray();
			string Opt(string name, string fallback) => args2.SkipWhile((string x) => x != name).Skip(1).FirstOrDefault() ?? fallback;
			var inv = System.Globalization.CultureInfo.InvariantCulture;
			(string c1, string c2) = Faces.DefaultColors(a3[0]);
			var fan = new FanScreen { Serial = "preview", Face = a3[0], Sensor = Opt("--sensor", "gpu.load"), Color1 = c1, Color2 = c2 };
			float[] values = Opt("--values", "42").Split(',').Select((string v) => float.Parse(v, inv)).ToArray();
			string[] stamps = Opt("--times", "1").Split(',');
			Directory.CreateDirectory(a3[1]);
			using Faces faces2 = new Faces();
			for (int i = 0; i < stamps.Length; i++)
			{
				using Bitmap still = faces2.Render(fan, values[Math.Min(i, values.Length - 1)], double.Parse(stamps[i], inv));
				still.Save(Path.Combine(a3[1], $"{a3[0]}-at-{stamps[i]}.png"), ImageFormat.Png);
			}
			Stopwatch watch = Stopwatch.StartNew();
			for (int k = 0; k < 60; k++)
			{
				faces2.Render(fan, values[^1], 100.0 + k / 12.0).Dispose();
			}
			File.WriteAllText(Path.Combine(a3[1], "cost.txt"), $"{watch.Elapsed.TotalMilliseconds / 60.0:0.0} ms per frame");
			return 0;
		}
		if (args2.Contains("--refresh-copy"))
		{
			// --refresh-copy "Deer (my copy)" Deer: gives a user copy the template's current widgets while keeping
			// the copy's own background and colours (run while LumaLi is closed)
			string[] a3 = args2.SkipWhile((string x) => x != "--refresh-copy").Skip(1).ToArray();
			List<ScreenLayout> mine = Store.LoadLayouts();
			ScreenLayout copy = mine.FirstOrDefault((ScreenLayout l) => l.Name == a3[0]);
			ScreenLayout source = Templates.All().FirstOrDefault((ScreenLayout l) => l.Name == a3[1]);
			if (copy == null || source == null)
			{
				Log.Write("refresh-copy: layout not found");
				return 1;
			}
			copy.Widgets = source.Clone().Widgets;
			Store.SaveLayouts(mine);
			Log.Write($"refresh-copy: {a3[0]} now has the widgets of {a3[1]}");
			return 0;
		}
		if (args2.Contains("--make-icon"))
		{
			// writes the multi-size app icon (and a PNG preview of every size): --make-icon app.ico
			string target = args2.SkipWhile((string x) => x != "--make-icon").Skip(1).FirstOrDefault() ?? "app.ico";
			AppIcon.Save(target);
			int width = AppIcon.Sizes.Sum() + 10 * AppIcon.Sizes.Length;
			using Bitmap sheet = new Bitmap(width, 270);
			using (Graphics g = Graphics.FromImage(sheet))
			{
				g.Clear(Color.FromArgb(28, 28, 32));
				int x = 5;
				foreach (int size in AppIcon.Sizes)
				{
					using Bitmap one = AppIcon.Draw(size);
					g.DrawImageUnscaled(one, x, 262 - size);
					x += size + 10;
				}
			}
			sheet.Save(Path.ChangeExtension(target, ".png"), ImageFormat.Png);
			return 0;
		}
		if (args2.Contains("--make-frames"))
		{
			string[] array2 = args2.SkipWhile((string x) => x != "--make-frames").Skip(1).ToArray();
			try
			{
				File.WriteAllText(Path.Combine(array2[1], "..", "make-frames.txt"), VideoFrames.MakeFrames(array2[0], array2[1], args2.Contains("--sideways"), args2.Contains("--stretch")) + " frames");
				return 0;
			}
			catch (Exception ex3)
			{
				Log.Write("make-frames: " + ex3);
				return 1;
			}
		}
		if (args2.Contains("--wireless-dump"))
		{
			string path = args2.SkipWhile((string a) => a != "--wireless-dump").Skip(1).FirstOrDefault() ?? ".";
			List<string> list = new List<string>();
			try
			{
				List<UsbDeviceInfo> source = WinUsb.Enumerate();
				using WirelessHub wirelessHub = new WirelessHub(source.First((UsbDeviceInfo d) => d.Kind == DeviceKind.WirelessTx), source.First((UsbDeviceInfo d) => d.Kind == DeviceKind.WirelessRx));
				list.Add($"master {Convert.ToHexString(wirelessHub.Master)} channel {wirelessHub.Channel}");
				foreach (FanGroup group in wirelessHub.Groups)
				{
					list.Add($"{group.Id} slot={group.Slot} type={group.FanTypeByte} fans={group.FanCount} mbSync={group.MotherboardSync} status=0x{group.Record[28]:X2} cmdSeq={group.CmdSeq} effect={Convert.ToHexString(group.EffectIndex)} pwm={Convert.ToHexString(group.Pwm)}\n   {Convert.ToHexString(group.Record)}");
				}
			}
			catch (Exception ex4)
			{
				list.Add("ERROR " + ex4);
			}
			File.WriteAllLines(Path.Combine(path, "wireless-dump.txt"), list);
			return 0;
		}
		if (args2.Contains("--lighting-size"))
		{
			string path2 = args2.SkipWhile((string a) => a != "--lighting-size").Skip(1).FirstOrDefault() ?? ".";
			List<string> list2 = new List<string>();
			try
			{
				List<UsbDeviceInfo> source2 = WinUsb.Enumerate();
				using WirelessHub wirelessHub2 = new WirelessHub(source2.First((UsbDeviceInfo d) => d.Kind == DeviceKind.WirelessTx), source2.First((UsbDeviceInfo d) => d.Kind == DeviceKind.WirelessRx));
				FanGroup fanGroup = wirelessHub2.Groups.First((FanGroup x) => x.FanCount == 3);
				EffectSettings es = Lighting.DefaultsFor("Rainbow spin");
				int[] array3 = new int[5] { 24, 32, 34, 35, 36 };
				foreach (int i in array3)
				{
					List<(byte, byte, byte)[]> frames = (from f in Enumerable.Range(0, i)
						select Lighting.Frame("Rainbow spin", es, 3, 26, (double)f / (double)i)).ToList();
					bool flag = false;
					byte[] array4 = Array.Empty<byte>();
					for (int n = 1; n <= 2; n++)
					{
						if (flag)
						{
							break;
						}
						array4 = wirelessHub2.UploadLighting(fanGroup, frames, 200);
						for (int num = 0; num < 4; num++)
						{
							if (flag)
							{
								break;
							}
							Thread.Sleep(250);
							try
							{
								wirelessHub2.Refresh();
							}
							catch (IOException)
							{
							}
							flag = wirelessHub2.Applied(fanGroup, array4);
						}
					}
					list2.Add($"{fanGroup.Id} frames={i} raw={i * 78 * 3} encoded={TinyUz.Encode(new byte[i * 78 * 3]).Length} applied={flag}");
				}
			}
			catch (Exception ex6)
			{
				list2.Add("ERROR " + ex6);
			}
			File.WriteAllLines(Path.Combine(path2, "lighting-size.txt"), list2);
			return 0;
		}
		if (args2.Contains("--lighting-test"))
		{
			string path3 = args2.SkipWhile((string a) => a != "--lighting-test").Skip(1).FirstOrDefault() ?? ".";
			List<string> list3 = new List<string>();
			try
			{
				List<UsbDeviceInfo> source3 = WinUsb.Enumerate();
				using WirelessHub wirelessHub3 = new WirelessHub(source3.First((UsbDeviceInfo d) => d.Kind == DeviceKind.WirelessTx), source3.First((UsbDeviceInfo d) => d.Kind == DeviceKind.WirelessRx));
				string[] array5 = new string[2] { "Rainbow spin", "Tunnel spiral" };
				foreach (string text2 in array5)
				{
					EffectSettings s = Lighting.DefaultsFor(text2);
					foreach (FanGroup g in wirelessHub3.Groups.ToList())
					{
						string value = Convert.ToHexString(g.EffectIndex);
						(List<(byte R, byte G, byte B)[]> Frames, int Ticks) tuple = Lighting.FanLoop(text2, s, g.FanCount);
						List<(byte, byte, byte)[]> item = tuple.Frames;
						int item2 = tuple.Ticks;
						Stopwatch stopwatch2 = Stopwatch.StartNew();
						bool flag2 = false;
						int value2 = 0;
						byte[] array6 = Array.Empty<byte>();
						for (int num2 = 1; num2 <= 3; num2++)
						{
							if (flag2)
							{
								break;
							}
							value2 = num2;
							array6 = wirelessHub3.UploadLighting(g, item, item2);
							for (int num3 = 0; num3 < 4; num3++)
							{
								if (flag2)
								{
									break;
								}
								Thread.Sleep(250);
								try
								{
									wirelessHub3.Refresh();
								}
								catch (IOException)
								{
								}
								flag2 = wirelessHub3.Applied(g, array6);
							}
						}
						FanGroup fanGroup2 = wirelessHub3.Groups.First((FanGroup x) => x.Id == g.Id);
						list3.Add($"{text2,-14} {g.Id} fans={g.FanCount} frames={item.Count} ticks={item2} sent={Convert.ToHexString(array6)} before={value} after={Convert.ToHexString(fanGroup2.EffectIndex)} applied={flag2} attempts={value2} {stopwatch2.ElapsedMilliseconds} ms");
					}
					Thread.Sleep(4000);
				}
			}
			catch (Exception ex8)
			{
				list3.Add("ERROR " + ex8);
			}
			File.WriteAllLines(Path.Combine(path3, "lighting-test.txt"), list3);
			return 0;
		}
		if (args2.Contains("--sleep-test"))
		{
			return SleepTest(args2.SkipWhile((string a) => a != "--sleep-test").Skip(1).FirstOrDefault() ?? ".");
		}
		if (args2.Contains("--screen-test"))
		{
			return ScreenTest(args2.SkipWhile((string a) => a != "--screen-test").Skip(1).FirstOrDefault() ?? ".", 180);
		}
		if (args2.Contains("--hardware-test"))
		{
			return HardwareTest(args2.SkipWhile((string a) => a != "--hardware-test").Skip(1).FirstOrDefault() ?? ".");
		}
		bool createdNew;
		using Mutex mutex = new Mutex(initiallyOwned: true, "Local\\LumaLi", out createdNew);
		if (!createdNew && (!args2.Contains("--elevated") || !mutex.WaitOne(8000)))
		{
			MessageBox.Show("LumaLi is already running — look for it in the system tray.", "LumaLi");
			return 1;
		}
		Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		Application.ThreadException += delegate(object _, ThreadExceptionEventArgs e)
		{
			Log.Write("UI error: " + e.Exception);
		};
		AppDomain.CurrentDomain.UnhandledException += delegate(object _, UnhandledExceptionEventArgs e)
		{
			Log.Write("fatal: " + e.ExceptionObject);
		};
		using (Graphics graphics2 = Graphics.FromHwnd(IntPtr.Zero))
		{
			Theme.Scale = graphics2.DpiX / 96f;
		}
		Log.Write("LumaLi " + Application.ProductVersion + " starting");
		SettingsPage.MigrateStartup();
		Engine engine = new Engine();
		try
		{
			bool flag3 = args2.Contains("--snapshot");
			if (flag3)
			{
				engine.StartSensorsOnly();
			}
			else
			{
				engine.Start();
			}
			MainForm form = new MainForm(engine);
			try
			{
				if (flag3)
				{
					form.Shown += async delegate
					{
						string dir = args2.SkipWhile((string a) => a != "--snapshot").Skip(1).FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "snapshots");
						Directory.CreateDirectory(dir);
						await Task.Delay(2500);
						string[] pageNames = MainForm.PageNames;
						foreach (string page in pageNames)
						{
							form.Show(page);
							await Task.Delay(900);
							using Bitmap bitmap4 = new Bitmap(form.Width, form.Height);
							form.DrawToBitmap(bitmap4, new Rectangle(0, 0, form.Width, form.Height));
							bitmap4.Save(Path.Combine(dir, page.Replace(' ', '-').ToLowerInvariant() + ".png"), ImageFormat.Png);
							FlowLayoutPanel flowLayoutPanel = form.Controls.OfType<Panel>().First().Controls.OfType<Page>().First((Page c) => c.Visible).Controls.OfType<FlowLayoutPanel>().First();
							File.AppendAllLines(Path.Combine(dir, "layout.txt"), from Control c in flowLayoutPanel.Controls
								select $"{page}: {c.GetType().Name} {c.Bounds} visible={c.Visible}");
						}
						try
						{
							File.WriteAllText(Path.Combine(dir, "editor-test.txt"), EditorTest(form, engine));
						}
						catch (Exception ex9)
						{
							File.WriteAllText(Path.Combine(dir, "editor-test.txt"), "FAIL " + ex9);
						}
						form.ExitApp();
					};
				}
				Application.Run(form);
				Log.Write("LumaLi exiting");
				return 0;
			}
			finally
			{
				if (form != null)
				{
					((IDisposable)form).Dispose();
				}
			}
		}
		finally
		{
			if (engine != null)
			{
				((IDisposable)engine).Dispose();
			}
		}
	}

	[DllImport("user32.dll")]
	private static extern nint SendMessage(nint h, int msg, nint w, nint l);

	private static string EditorTest(MainForm form, Engine engine)
	{
		StringBuilder log = new StringBuilder();
		form.Show("Screen layout");
		Application.DoEvents();
		LayoutCanvas canvas = form.Get<LayoutPage>("Screen layout").Canvas;
		lock (engine.Settings)
		{
			engine.Settings.Layout = "Classic";
		}
		canvas.Tick(force: true);
		ScreenLayout currentLayout = engine.CurrentLayout;
		Widget widget = currentLayout.Widgets.First((Widget w) => w.Kind == WidgetKind.Panel);
		int x2 = widget.X;
		int y2 = widget.Y;
		float num = 4f;
		float num2 = (float)canvas.Width - 8f * Theme.Scale;
		float num3 = num2 / num;
		if (num3 > (float)canvas.Height - 8f * Theme.Scale)
		{
			num3 = (float)canvas.Height - 8f * Theme.Scale;
			num2 = num3 * num;
		}
		float num4 = ((float)canvas.Width - num2) / 2f;
		float num5 = ((float)canvas.Height - num3) / 2f;
		float num6 = num2 / 1920f;
		int num7 = (int)(num4 + (float)(widget.X + 8) * num6);
		int num8 = (int)(num5 + (float)(widget.Y + 8) * num6);
		SendMessage(canvas.Handle, 513, 1, P(num7, num8));
		Check("click selects the widget", canvas.Selected == widget);
		for (int i = 1; i <= 10; i++)
		{
			SendMessage(canvas.Handle, 512, 1, P(num7 + i * 6, num8 + i * 3));
		}
		SendMessage(canvas.Handle, 514, IntPtr.Zero, P(num7 + 60, num8 + 30));
		Application.DoEvents();
		ScreenLayout currentLayout2 = engine.CurrentLayout;
		Check("drag made a user copy (now \"" + currentLayout2.Name + "\")", !currentLayout2.BuiltIn && currentLayout2 != currentLayout && currentLayout2.Name.StartsWith("Classic"));
		Check("built-in template untouched", widget.X == x2 && widget.Y == y2);
		Widget selected = canvas.Selected;
		int index = engine.CurrentLayout.Widgets.IndexOf(selected);
		int x3 = selected.X;
		int y3 = selected.Y;
		Check($"widget moved ({x2},{y2}) -> ({selected.X},{selected.Y})", selected.X != x2 || selected.Y != y2);
		Check("snap keeps positions on the 10 px grid or a guide", selected.X % 10 == 0 || selected.Y % 10 == 0);
		canvas.Undo();
		Application.DoEvents();
		Widget widget2 = engine.CurrentLayout.Widgets[index];
		Check($"undo restores the position ({widget2.X},{widget2.Y})", widget2.X == x2 && widget2.Y == y2);
		canvas.Redo();
		Check("redo moves it again", engine.CurrentLayout.Widgets[index].X == x3 && engine.CurrentLayout.Widgets[index].Y == y3);
		int count = engine.CurrentLayout.Widgets.Count;
		canvas.Duplicate();
		Check("duplicate adds a widget", engine.CurrentLayout.Widgets.Count == count + 1);
		canvas.DeleteSelected();
		Check("delete removes it", engine.CurrentLayout.Widgets.Count == count);
		canvas.Selected = engine.CurrentLayout.Widgets[0];
		canvas.Align("right");
		Check("align right", engine.CurrentLayout.Widgets[0].X + engine.CurrentLayout.Widgets[0].W == 1920);
		return log.ToString();
		void Check(string what, bool ok)
		{
			log.AppendLine((ok ? "PASS " : "FAIL ") + what);
		}
		static nint P(int x, int y)
		{
			return (y << 16) | (x & 0xFFFF);
		}
	}

	private static int HardwareTest(string dir)
	{
		string dir2 = dir;
		Directory.CreateDirectory(dir2);
		StringBuilder log = new StringBuilder();
		Engine engine = new Engine();
		try
		{
			lock (engine.Settings)
			{
				engine.Settings.Layout = "Command Center";
				engine.Settings.ScreenFps = 20;
				engine.Settings.Cooling.Mode = "Hold";
			}
			foreach (UsbDeviceInfo item in WinUsb.Enumerate())
			{
				Line($"found {item.Kind} {item.Name} {item.Serial}");
			}
			engine.Start();
			Thread.Sleep(15000);
			Dump("after 15 s of output");
			lock (engine.Settings)
			{
				engine.Settings.Lighting.FanEffect = "Rainbow spin";
				engine.Settings.Lighting.FansOn = true;
			}
			engine.LightingChanged();
			Thread.Sleep(9000);
			Line("lighting: " + engine.LightingResult);
			Dump("after lighting upload");
			lock (engine.Settings)
			{
				engine.Settings.Cooling.Mode = "Performance";
			}
			engine.Poke();
			Thread.Sleep(10000);
			Line("cooling: " + engine.CoolingResult);
			Dump("after 10 s at Performance (75 %)");
			lock (engine.Settings)
			{
				engine.Settings.Cooling.Mode = "Hold";
			}
			engine.Poke();
			engine.Identify(TimeSpan.FromSeconds(3.0));
			Thread.Sleep(6000);
			Dump("after identify + back to Hold");
			Line("releasing");
			return 0;
		}
		finally
		{
			if (engine != null)
			{
				((IDisposable)engine).Dispose();
			}
		}
		void Dump(string when)
		{
			Status(when);
			foreach (OutputStatus item2 in engine.AllOutputs())
			{
				Line($"{item2.Name,-22} present={item2.Present} running={item2.Running} fps={item2.Fps:0.0} fw={item2.Firmware} state={item2.State}");
			}
			WirelessHub hub = engine.Hub;
			if (hub != null)
			{
				foreach (FanGroup group in hub.Groups)
				{
					Line($"group {group.Id} slot={group.Slot} ch={group.Channel} rx={group.RxType} type={group.FanTypeByte} fans={group.FanCount} rpm=[{group.RpmText}] duty={group.DutyPercent}% effect={Convert.ToHexString(group.EffectIndex)}");
				}
			}
		}
		void Line(string s)
		{
			StringBuilder stringBuilder = log;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(1, 2, stringBuilder);
			handler.AppendFormatted(DateTime.Now, "HH:mm:ss");
			handler.AppendLiteral(" ");
			handler.AppendFormatted(s);
			stringBuilder.AppendLine(ref handler);
			File.WriteAllText(Path.Combine(dir2, "hardware-test.txt"), log.ToString());
		}
		void Status(string when)
		{
			Line("--- " + when);
		}
	}

	private static int Probe(string dir)
	{
		StringBuilder stringBuilder = new StringBuilder();
		List<UsbDeviceInfo> source = WinUsb.Enumerate();
		bool[] array = args0Reset;
		foreach (bool flag in array)
		{
			foreach (UsbDeviceInfo item in source.Where(delegate(UsbDeviceInfo d)
			{
				DeviceKind kind = d.Kind;
				return (kind == DeviceKind.Screen || kind == DeviceKind.WirelessTx) ? true : false;
			}))
			{
				try
				{
					using WinUsb winUsb = new WinUsb(item, flag);
					StringBuilder stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder3 = stringBuilder2;
					StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(8, 3, stringBuilder2);
					handler.AppendFormatted(item.Name);
					handler.AppendLiteral(" reset=");
					handler.AppendFormatted(flag);
					handler.AppendLiteral(" ");
					handler.AppendFormatted(winUsb.Pipes);
					stringBuilder3.AppendLine(ref handler);
					if (item.Kind == DeviceKind.Screen)
					{
						PacketBuilder packetBuilder = new PacketBuilder(legacy: false);
						winUsb.Write(packetBuilder.Build(10));
						byte[] array2 = winUsb.Read(2000);
						stringBuilder2 = stringBuilder;
						StringBuilder stringBuilder4 = stringBuilder2;
						handler = new StringBuilder.AppendInterpolatedStringHandler(19, 2, stringBuilder2);
						handler.AppendLiteral("  GetVer -> ");
						handler.AppendFormatted(array2.Length);
						handler.AppendLiteral(" bytes ");
						handler.AppendFormatted(Convert.ToHexString(array2.AsSpan(0, Math.Min(32, array2.Length))));
						stringBuilder4.AppendLine(ref handler);
						continue;
					}
					byte[] array3 = new byte[4] { 8, 2, 4, 6 };
					foreach (byte b in array3)
					{
						byte[] array4 = new byte[64];
						array4[0] = 17;
						array4[1] = b;
						winUsb.Write(array4);
						byte[] array5 = winUsb.Read(200);
						stringBuilder2 = stringBuilder;
						StringBuilder stringBuilder5 = stringBuilder2;
						handler = new StringBuilder.AppendInterpolatedStringHandler(16, 3, stringBuilder2);
						handler.AppendLiteral("  ch ");
						handler.AppendFormatted(b);
						handler.AppendLiteral(" -> ");
						handler.AppendFormatted(array5.Length);
						handler.AppendLiteral(" bytes ");
						handler.AppendFormatted(Convert.ToHexString(array5.AsSpan(0, Math.Min(20, array5.Length))));
						stringBuilder5.AppendLine(ref handler);
						if (array5.Length != 0)
						{
							break;
						}
					}
				}
				catch (Exception ex)
				{
					StringBuilder stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder6 = stringBuilder2;
					StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(9, 3, stringBuilder2);
					handler.AppendFormatted(item.Name);
					handler.AppendLiteral(" reset=");
					handler.AppendFormatted(flag);
					handler.AppendLiteral(": ");
					handler.AppendFormatted(ex.Message);
					stringBuilder6.AppendLine(ref handler);
				}
			}
		}
		File.WriteAllText(Path.Combine(dir, "probe.txt"), stringBuilder.ToString());
		return 0;
	}

	private static int Resync(string dir)
	{
		StringBuilder stringBuilder = new StringBuilder();
		using WinUsb winUsb = new WinUsb(WinUsb.Enumerate().First((UsbDeviceInfo x) => x.Kind == DeviceKind.Screen), resetPipes: false);
		PacketBuilder packetBuilder = new PacketBuilder(legacy: false);
		byte[] data = new byte[65536];
		for (int i = 0; i <= 9; i++)
		{
			StringBuilder stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler;
			if (i > 0)
			{
				try
				{
					winUsb.Write(data);
				}
				catch (Exception ex)
				{
					stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder3 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(22, 1, stringBuilder2);
					handler.AppendLiteral("padding write failed: ");
					handler.AppendFormatted(ex.Message);
					stringBuilder3.AppendLine(ref handler);
					break;
				}
			}
			winUsb.Write(packetBuilder.Build(10));
			byte[] array = winUsb.Read(700);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(26, 3, stringBuilder2);
			handler.AppendLiteral("after ");
			handler.AppendFormatted(i * 64);
			handler.AppendLiteral(" KB padding: ");
			handler.AppendFormatted(array.Length);
			handler.AppendLiteral(" bytes ");
			handler.AppendFormatted(Convert.ToHexString(array.AsSpan(0, Math.Min(32, array.Length))));
			stringBuilder4.AppendLine(ref handler);
			if (array.Length != 0)
			{
				winUsb.Flush();
				break;
			}
		}
		File.WriteAllText(Path.Combine(dir, "resync.txt"), stringBuilder.ToString());
		return 0;
	}

	private static int ScreenTest(string dir, int seconds)
	{
		Directory.CreateDirectory(dir);
		string file = Path.Combine(dir, "screen-test.txt");
		File.WriteAllText(file, "");
		using (Engine engine = new Engine())
		{
			lock (engine.Settings)
			{
				engine.Settings.Layout = "Command Center";
				engine.Settings.ScreenFps = 20;
				engine.Settings.ScreenQuality = 90;
				engine.Settings.Cooling.Mode = "Hold";
				engine.Settings.Lighting.Applied = false;
			}
			engine.Start();
			int num = 0;
			string text = "";
			DateTime dateTime = DateTime.Now.AddSeconds(seconds);
			DateTime dateTime2 = DateTime.Now.AddSeconds(15.0);
			while (DateTime.Now < dateTime)
			{
				Thread.Sleep(250);
				string state = engine.Screen.State;
				if (state.StartsWith("error") && state != text)
				{
					num++;
					Line("SCREEN " + state);
				}
				text = state;
				if (DateTime.Now >= dateTime2)
				{
					dateTime2 = dateTime2.AddSeconds(15.0);
					string value = string.Join(" ", engine.FanOutputs.Select((OutputStatus f) => $"{f.Fps:0.0}"));
					Line($"screen running={engine.Screen.Running} fps={engine.Screen.Fps:0.0} fw={engine.Screen.Firmware} state={state} | fan fps {value}");
				}
			}
			Line($"done: {num} screen error transitions");
			return (num != 0) ? 1 : 0;
		}
		void Line(string s)
		{
			File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss} {s}{Environment.NewLine}");
		}
	}

	private static int Bench(string dir)
	{
		StringBuilder stringBuilder = new StringBuilder();
		Engine engine = new Engine();
		try
		{
			engine.StartSensorsOnly();
			Thread.Sleep(2500);
			foreach (ScreenLayout item3 in engine.Layouts.Where((ScreenLayout l) => l.BuiltIn))
			{
				using ScreenRenderer screenRenderer = new ScreenRenderer(engine.Sensors);
				List<double> list = new List<double>();
				List<double> list2 = new List<double>();
				List<double> list3 = new List<double>();
				for (int i = 0; i < 40; i++)
				{
					Stopwatch stopwatch = Stopwatch.StartNew();
					using Bitmap bitmap = screenRenderer.Render(item3.Clone(), engine.Time);
					list.Add(stopwatch.Elapsed.TotalMilliseconds);
					stopwatch.Restart();
					bitmap.RotateFlip(RotateFlipType.Rotate90FlipNone);
					list2.Add(stopwatch.Elapsed.TotalMilliseconds);
					stopwatch.Restart();
					Imaging.Jpeg(bitmap, 90);
					list3.Add(stopwatch.Elapsed.TotalMilliseconds);
				}
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder3 = stringBuilder2;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(40, 4, stringBuilder2);
				handler.AppendLiteral("screen ");
				handler.AppendFormatted<string>(item3.Name, -15);
				handler.AppendLiteral(" render ");
				handler.AppendFormatted(Avg(list.Skip(5).ToList()), 6, "0.0");
				handler.AppendLiteral(" ms  rotate ");
				handler.AppendFormatted(Avg(list2.Skip(5).ToList()), 5, "0.0");
				handler.AppendLiteral(" ms  jpeg ");
				handler.AppendFormatted(Avg(list3.Skip(5).ToList()), 5, "0.0");
				handler.AppendLiteral(" ms");
				stringBuilder3.AppendLine(ref handler);
			}
			ScreenRenderer r = new ScreenRenderer(engine.Sensors);
			try
			{
				ScreenLayout screenLayout = engine.Layouts.First((ScreenLayout l) => l.Name == "Command Center");
				ScreenLayout screenLayout2 = screenLayout.Clone();
				screenLayout2.Background = "solid";
				ScreenLayout screenLayout3 = screenLayout2.Clone();
				foreach (Widget widget in screenLayout3.Widgets)
				{
					widget.Glow = false;
				}
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder4 = stringBuilder2;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(37, 3, stringBuilder2);
				handler.AppendLiteral("CC full ");
				handler.AppendFormatted(Time(screenLayout), "0.0");
				handler.AppendLiteral(" | solid bg ");
				handler.AppendFormatted(Time(screenLayout2), "0.0");
				handler.AppendLiteral(" | solid+no glow ");
				handler.AppendFormatted(Time(screenLayout3), "0.0");
				stringBuilder4.AppendLine(ref handler);
				WidgetKind[] values = Enum.GetValues<WidgetKind>();
				foreach (WidgetKind kind in values)
				{
					ScreenLayout screenLayout4 = screenLayout3.Clone();
					screenLayout4.Widgets = screenLayout4.Widgets.Where((Widget w) => w.Kind == kind).ToList();
					if (screenLayout4.Widgets.Count > 0)
					{
						stringBuilder2 = stringBuilder;
						StringBuilder stringBuilder5 = stringBuilder2;
						handler = new StringBuilder.AppendInterpolatedStringHandler(15, 3, stringBuilder2);
						handler.AppendLiteral("   only ");
						handler.AppendFormatted(kind, -9);
						handler.AppendLiteral(" x");
						handler.AppendFormatted(screenLayout4.Widgets.Count, 2);
						handler.AppendLiteral(": ");
						handler.AppendFormatted(Time(screenLayout4), "0.0");
						handler.AppendLiteral(" ms");
						stringBuilder5.AppendLine(ref handler);
					}
				}
				ScreenLayout screenLayout5 = screenLayout3.Clone();
				screenLayout5.Widgets.Clear();
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder6 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
				handler.AppendLiteral("   empty: ");
				handler.AppendFormatted(Time(screenLayout5), "0.0");
				handler.AppendLiteral(" ms");
				stringBuilder6.AppendLine(ref handler);
			}
			finally
			{
				if (r != null)
				{
					((IDisposable)r).Dispose();
				}
			}
			using (Faces faces = new Faces())
			{
				string[] names = Faces.Names;
				foreach (string text in names)
				{
					(string, string) tuple = Faces.DefaultColors(text);
					string item = tuple.Item1;
					string item2 = tuple.Item2;
					FanScreen fan = new FanScreen
					{
						Face = text,
						Sensor = "cpu.load",
						Color1 = item,
						Color2 = item2
					};
					List<double> list4 = new List<double>();
					List<double> list5 = new List<double>();
					for (int k = 0; k < 30; k++)
					{
						Stopwatch stopwatch2 = Stopwatch.StartNew();
						using Bitmap bmp = faces.Render(fan, 17f, engine.Time);
						list4.Add(stopwatch2.Elapsed.TotalMilliseconds);
						stopwatch2.Restart();
						Imaging.Jpeg(bmp, 85);
						list5.Add(stopwatch2.Elapsed.TotalMilliseconds);
					}
					StringBuilder stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder7 = stringBuilder2;
					StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(28, 3, stringBuilder2);
					handler.AppendLiteral("face   ");
					handler.AppendFormatted<string>(text, -15);
					handler.AppendLiteral(" render ");
					handler.AppendFormatted(Avg(list4.Skip(5).ToList()), 6, "0.0");
					handler.AppendLiteral(" ms  jpeg ");
					handler.AppendFormatted(Avg(list5.Skip(5).ToList()), 5, "0.0");
					handler.AppendLiteral(" ms");
					stringBuilder7.AppendLine(ref handler);
				}
				File.WriteAllText(Path.Combine(dir, "bench.txt"), stringBuilder.ToString());
				return 0;
			}
			double Time(ScreenLayout l)
			{
				for (int m = 0; m < 5; m++)
				{
					r.Render(l, engine.Time).Dispose();
				}
				Stopwatch stopwatch3 = Stopwatch.StartNew();
				for (int n = 0; n < 30; n++)
				{
					r.Render(l, engine.Time).Dispose();
				}
				return stopwatch3.Elapsed.TotalMilliseconds / 30.0;
			}
		}
		finally
		{
			if (engine != null)
			{
				((IDisposable)engine).Dispose();
			}
		}
		static double Avg(List<double> l)
		{
			if (l.Count != 0)
			{
				return l.Average();
			}
			return 0.0;
		}
	}

	private static int RequestExit()
	{
		if (!EventWaitHandle.TryOpenExisting("Local\\LumaLi.Exit", out EventWaitHandle result))
		{
			return 0;
		}
		using (result)
		{
			result.Set();
		}
		for (int i = 0; i < 150; i++)
		{
			bool createdNew;
			using (new Mutex(initiallyOwned: false, "Local\\LumaLi", out createdNew))
			{
				if (createdNew)
				{
					return 0;
				}
				Thread.Sleep(100);
			}
		}
		return 1;
	}

	private static int SleepTest(string dir)
	{
		Directory.CreateDirectory(dir);
		string file = Path.Combine(dir, "sleep-test.txt");
		File.WriteAllText(file, "");
		Engine engine = new Engine();
		try
		{
			engine.Start();
			Thread.Sleep(15000);
			Dump("running");
			bool flag = engine.Screen.Running && engine.FanOutputs.All((OutputStatus f) => f.Running);
			for (int i = 1; i <= 2; i++)
			{
				engine.Suspend();
				Dump($"after suspend #{i}");
				flag &= engine.AllOutputs().All((OutputStatus o) => !o.Running);
				Thread.Sleep(5000);
				engine.Resume();
				Thread.Sleep(15000);
				Dump($"after resume #{i}");
				flag &= engine.Screen.Running && engine.FanOutputs.All((OutputStatus f) => f.Running) && engine.Wireless.Running;
			}
			Line(flag ? "RESULT PASS" : "RESULT FAIL");
			return (!flag) ? 1 : 0;
		}
		finally
		{
			if (engine != null)
			{
				((IDisposable)engine).Dispose();
			}
		}
		void Dump(string when)
		{
			Line("--- " + when);
			foreach (OutputStatus item in engine.AllOutputs())
			{
				Line($"  {item.Name,-20} running={item.Running} fps={item.Fps:0.0} state={item.State}");
			}
		}
		void Line(string s)
		{
			File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss} {s}{Environment.NewLine}");
		}
	}

	private static int ListDevices()
	{
		foreach (UsbDeviceInfo item in WinUsb.Enumerate())
		{
			Console.WriteLine($"{item.Kind,-11} {item.Name,-24} {item.Serial}");
		}
		return 0;
	}
}
