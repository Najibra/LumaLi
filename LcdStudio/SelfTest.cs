using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LcdStudio.Core;
using LcdStudio.Devices;

namespace LcdStudio;

internal static class SelfTest
{
	private static int _pass;

	private static int _fail;

	private static readonly StringBuilder Report = new StringBuilder();

	private static void Check(string name, bool ok, string detail = "")
	{
		if (ok)
		{
			_pass++;
		}
		else
		{
			_fail++;
		}
		StringBuilder report = Report;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(2, 3, report);
		handler.AppendFormatted(ok ? "PASS" : "FAIL");
		handler.AppendLiteral("  ");
		handler.AppendFormatted(name);
		handler.AppendFormatted((detail.Length > 0) ? ("  — " + detail) : "");
		report.AppendLine(ref handler);
	}

	public static int Run(string[] args)
	{
		string text = args.SkipWhile((string a) => a != "--test").Skip(1).FirstOrDefault((string a) => !a.StartsWith("--")) ?? Path.Combine(AppContext.BaseDirectory, "test-output");
		Directory.CreateDirectory(text);
		try
		{
			Protocol();
		}
		catch (Exception ex)
		{
			Check("protocol", ok: false, ex.ToString());
		}
		try
		{
			Rendering(text);
		}
		catch (Exception ex2)
		{
			Check("rendering", ok: false, ex2.ToString());
		}
		try
		{
			LightingLoops();
		}
		catch (Exception ex3)
		{
			Check("lighting", ok: false, ex3.ToString());
		}
		try
		{
			Model();
		}
		catch (Exception ex4)
		{
			Check("model", ok: false, ex4.ToString());
		}
		StringBuilder report = Report;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(17, 2, report);
		handler.AppendLiteral("\n");
		handler.AppendFormatted(_pass);
		handler.AppendLiteral(" passed, ");
		handler.AppendFormatted(_fail);
		handler.AppendLiteral(" failed");
		report.AppendLine(ref handler);
		File.WriteAllText(Path.Combine(text, "test-results.txt"), Report.ToString());
		Console.WriteLine(Report.ToString());
		return (_fail != 0) ? 1 : 0;
	}

	private static byte[] Decrypt(byte[] data)
	{
		using DES dES = DES.Create();
		byte[] iv = (dES.Key = Encoding.ASCII.GetBytes("slv3tuzx"));
		byte[] array = dES.DecryptCbc(data, iv, PaddingMode.None);
		int pad = array[^1];
		if (pad < 1 || pad > 8 || array.Skip(array.Length - pad).Any((byte x) => x != pad))
		{
			throw new CryptographicException("bad padding, tail " + Convert.ToHexString(array[^8..]));
		}
		return array[..^pad];
	}

	private static void Protocol()
	{
		byte[] array = new PacketBuilder(legacy: false).Build(101, PacketBuilder.BigEndian(123456));
		Check("WinUSB header is 512 bytes with A1 1A trailer", array.Length == 512 && array[510] == 161 && array[511] == 26);
		byte[] array2 = Decrypt(array[..504]);
		Check("WinUSB header decrypts to 500 bytes", array2.Length == 500, array2.Length.ToString());
		Check("WinUSB header fields", array2[0] == 101 && array2[2] == 26 && array2[3] == 109 && array2[8] == 0 && array2[9] == 1 && array2[10] == 226 && array2[11] == 64);
		byte[] array3 = new PacketBuilder(legacy: true).Build(14, 40);
		byte[] array4 = Decrypt(array3);
		Check("Fan LCD header fills 512 bytes and decrypts to 504", array3.Length == 512 && array4.Length == 504 && array4[0] == 14 && array4[8] == 40);
		PacketBuilder packetBuilder = new PacketBuilder(legacy: false);
		uint num = BitConverter.ToUInt32(Decrypt(packetBuilder.Build(1)[..504]), 4);
		uint num2 = BitConverter.ToUInt32(Decrypt(packetBuilder.Build(1)[..504]), 4);
		Check("Timestamps strictly increase", num2 > num);
		Check("Fan brightness curve", LcdLink.FanBrightness(0) == 0 && LcdLink.FanBrightness(25) == 10 && LcdLink.FanBrightness(50) == 30 && LcdLink.FanBrightness(75) == 40 && LcdLink.FanBrightness(100) == 100);
		byte[] array5 = (from i in Enumerable.Range(0, 1000)
			select (byte)(i * 7)).ToArray();
		Check("tinyuz literal stream round-trips", TinyUzDecode(TinyUz.Encode(array5)).SequenceEqual(array5));
		FanGroup fanGroup = new FanGroup();
		fanGroup.Mac = new byte[6] { 1, 2, 3, 4, 5, 6 };
		fanGroup.Channel = 8;
		fanGroup.RxType = 2;
		fanGroup.Slot = 3;
		fanGroup.FanCount = 3;
		FanGroup g = fanGroup;
		byte[] array6 = WirelessHub.PwmFrame(g, new byte[6] { 9, 9, 9, 9, 9, 9 }, 8, 100);
		Check("PWM frame matches reference layout", array6.Length == 240 && array6.Take(21).SequenceEqual(new byte[21]
		{
			18, 16, 1, 2, 3, 4, 5, 6, 9, 9,
			9, 9, 9, 9, 2, 8, 3, 255, 255, 255,
			0
		}));
		byte[] array7 = WirelessHub.PwmFrame(g, new byte[6], 8, 5);
		Check("PWM floor (14 %) and stop", array7[17] == 35 && WirelessHub.PwmFrame(g, new byte[6], 8, 0)[17] == 0);
	}

	private static byte[] TinyUzDecode(byte[] code)
	{
		byte[] code2 = code;
		List<byte> list = new List<byte>();
		int pos = 4;
		int bitIndex = -1;
		int bits = 0;
		while (pos < code2.Length && Bit() == 1)
		{
			list.Add(code2[pos++]);
		}
		return list.ToArray();
		int Bit()
		{
			if (bits == 0)
			{
				bitIndex = pos++;
				bits = 8;
			}
			int result = (code2[bitIndex] >> 8 - bits) & 1;
			bits--;
			return result;
		}
	}

	private static void Rendering(string dir)
	{
		using Sensors sensors = new Sensors();
		(string, float)[] array = new(string, float)[17]
		{
			("cpu.load", 17f),
			("gpu.load", 1f),
			("gpu.power", 36f),
			("gpu.temp", 29f),
			("ram.load", 18f),
			("cpu.temp", 61f),
			("ram.used", 17.4f),
			("ram.total", 93.7f),
			("net.down", 3.2f),
			("net.up", 0.4f),
			("cpu.clock", 5025f),
			("gpu.clock", 2655f),
			("gpu.vram", 2.1f),
			("cpu.busiest", 40f),
			("group1.rpm", 1030f),
			("group1.duty", 50f),
			("group1.fans", 3f)
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, float) tuple = array[i];
			string item = tuple.Item1;
			float item2 = tuple.Item2;
			for (int j = 0; j < 30; j++)
			{
				sensors.Set(item, item2 + (float)Math.Sin((double)j / 3.0) * item2 * 0.2f);
			}
		}
		for (int k = 1; k <= 16; k++)
		{
			sensors.Set($"cpu.core{k}", k * 37 % 100);
		}
		using Faces faces = new Faces();
		string[] names = Faces.Names;
		foreach (string text in names)
		{
			(string, string) tuple2 = Faces.DefaultColors(text);
			string item3 = tuple2.Item1;
			string item4 = tuple2.Item2;
			FanScreen fan = new FanScreen
			{
				Face = text,
				Sensor = "cpu.load",
				Color1 = item3,
				Color2 = item4
			};
			using Bitmap bitmap = faces.Render(fan, 17f, 1.0);
			using Bitmap b = faces.Render(fan, 17f, 1.7);
			byte[] array2 = Imaging.Jpeg(bitmap, 85);
			bitmap.Save(Path.Combine(dir, "face-" + text.Replace(' ', '-').Replace('/', '-') + ".png"), ImageFormat.Png);
			Check($"Face {text}: 400×400, JPEG {array2.Length / 1024} KB fits fan packet", bitmap.Width == 400 && bitmap.Height == 400 && array2.Length <= 101888);
			bool flag;
			switch (text)
			{
			case "Clock":
			case "Image / GIF":
			case "Lottie Gauge":
			case "Carved":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (!flag)
			{
				Check("Face " + text + " animates", !SamePixels(bitmap, b));
			}
		}
		{
			// following a reading: calm leaves the effect as it is; fully hot takes the hot colours and a quicker pace;
			// halfway the colour is still vivid, turned round the colour wheel rather than mixed toward grey
			var l = new LightingSettings();
			EffectSettings aurora = Lighting.DefaultsFor("Aurora");
			EffectSettings calm = Lighting.Warmed(aurora, l, 0), hotSet = Lighting.Warmed(aurora, l, Lighting.ReactSteps);
			Color half = Lighting.HueMix(Imaging.Hex("#3affc8", Color.Cyan), Imaging.Hex(l.ReactHot1, Color.Orange), 0.5);
			int hi = Math.Max((int)half.R, Math.Max((int)half.G, (int)half.B)), lo = Math.Min((int)half.R, Math.Min((int)half.G, (int)half.B));
			double sat = (hi - lo) / (double)Math.Max(1, hi);
			Check($"Reactive fan lights: calm unchanged, hot {hotSet.Color1}/{hotSet.Color2} at speed {hotSet.Speed}, halfway {Imaging.Hex(half)}",
				calm.Color1 == aurora.Color1 && calm.Speed == aurora.Speed && hotSet.Color1 == l.ReactHot1 && hotSet.Color2 == l.ReactHot2 && hotSet.Speed > aurora.Speed && sat > 0.7);
		}
		{
			// past its Warn at level the gauge turns the warning colour; a fan with no level stays in its own colours
			(string g1, string g2) = Faces.DefaultColors("Lottie Gauge");
			FanScreen plain = new FanScreen { Serial = "warn-off", Face = "Lottie Gauge", Sensor = "cpu.load", Color1 = g1, Color2 = g2 };
			FanScreen warned = new FanScreen { Serial = "warn-on", Face = "Lottie Gauge", Sensor = "cpu.load", Color1 = g1, Color2 = g2, WarnAt = 80f, WarnColor = "#ff5a3c" };
			using Bitmap calm = faces.Render(plain, 90f, 1.0);
			using Bitmap hot = faces.Render(warned, 90f, 1.0);
			hot.Save(Path.Combine(dir, "face-Lottie-Gauge-warning.png"), ImageFormat.Png);
			// the face as a whole: cyan to pink normally, warm (more red, less blue) once warning
			static (double R, double B) Mean(Bitmap m)
			{
				double r = 0, bl = 0;
				for (int py = 0; py < m.Height; py += 4)
				{
					for (int px = 0; px < m.Width; px += 4)
					{
						Color p = m.GetPixel(px, py);
						r += p.R;
						bl += p.B;
					}
				}
				double n = (m.Width / 4) * (m.Height / 4);
				return (r / n, bl / n);
			}
			var (ar, ab) = Mean(calm);
			var (br, bb) = Mean(hot);
			Check($"Gauge turns its warning colour past Warn at  — red {ar:0}→{br:0}, blue {ab:0}→{bb:0}", br >= ar && bb < ab * 0.8);
		}
		ScreenRenderer renderer = new ScreenRenderer(sensors);
		try
		{
			foreach (ScreenLayout item5 in Templates.All())
			{
				using Bitmap bitmap2 = renderer.Render(item5, 2.0);
				using Bitmap bitmap3 = Imaging.Rotated(bitmap2, 90);
				byte[] array3 = Imaging.Jpeg(bitmap3, 90);
				bitmap2.Save(Path.Combine(dir, "screen-" + item5.Name.Replace(' ', '-') + ".png"), ImageFormat.Png);
				Check($"Template {item5.Name}: native 480×1920, JPEG {array3.Length / 1024} KB < 500 KB", bitmap3.Width == 480 && bitmap3.Height == 1920 && array3.Length < 512000);
				string text2 = string.Join(" ", from w in item5.Widgets
					where w.Kind == WidgetKind.Text
					select renderer.Resolve(w.Text));
				Check("Template " + item5.Name + ": tokens resolved", !text2.Contains('{'), (text2.Length > 80) ? text2.Substring(0, 80) : text2);
			}
			using Bitmap bitmap4 = new Bitmap(20, 10);
			bitmap4.SetPixel(0, 0, Color.Red);
			using Bitmap bitmap5 = Imaging.Rotated(bitmap4, 90);
			Check("Rotation 90° clockwise puts top-left at top-right", bitmap5.Width == 10 && bitmap5.Height == 20 && bitmap5.GetPixel(9, 0).R > 200);
		}
		finally
		{
			if (renderer != null)
			{
				((IDisposable)renderer).Dispose();
			}
		}
	}

	private static bool SamePixels(Bitmap a, Bitmap b)
	{
		for (int i = 0; i < a.Height; i += 7)
		{
			for (int j = 0; j < a.Width; j += 7)
			{
				if (a.GetPixel(j, i) != b.GetPixel(j, i))
				{
					return false;
				}
			}
		}
		return true;
	}

	private static void LightingLoops()
	{
		string[] effects = Lighting.Effects;
		foreach (string text in effects)
		{
			EffectSettings s = Lighting.DefaultsFor(text);
			int[] array = new int[3] { 2, 3, 4 };
			foreach (int num in array)
			{
				(List<(byte R, byte G, byte B)[]> Frames, int Ticks) tuple = Lighting.FanLoop(text, s, num);
				List<(byte, byte, byte)[]> item = tuple.Frames;
				int item2 = tuple.Ticks;
				int num2 = item.Count * num * 26 * 3;
				if (num2 > 8192)
				{
					Check($"Effect {text} with {num} fans stays within the fans' 8 KB buffer", ok: false, num2.ToString());
				}
				int num3 = TinyUz.Encode(new byte[num2]).Length;
				if (num == 3)
				{
					Check($"Effect {text}: {item.Count} frames, {num3} bytes ≤ 12288, {item2} ticks", num3 <= 12288 && item2 >= 16);
				}
				else if (num3 > 12288)
				{
					Check($"Effect {text} with {num} fans fits", ok: false, num3.ToString());
				}
			}
		}
	}

	private static void Model()
	{
		ScreenLayout screenLayout = Templates.All()[0];
		ScreenLayout screenLayout2 = Store.LayoutFromJson(Store.ToJson(screenLayout));
		Check("Layout JSON round-trip", screenLayout2 != null && screenLayout2.Widgets.Count == screenLayout.Widgets.Count && screenLayout2.Widgets[3].Sensor == screenLayout.Widgets[3].Sensor);
		FanScreen o = new FanScreen
		{
			Face = "Molten Glass",
			Sensor = "gpu.load",
			Rotation = 180,
			Color1 = "#111111"
		};
		FanScreen fanScreen = new FanScreen
		{
			Face = "Glass Slab",
			Sensor = "gpu.power",
			Rotation = 90,
			Label = "keep"
		};
		fanScreen.CopyLookFrom(o);
		Check("Use this look on all fans keeps sensor, label and rotation", fanScreen.Face == "Molten Glass" && fanScreen.Color1 == "#111111" && fanScreen.Sensor == "gpu.power" && fanScreen.Rotation == 90 && fanScreen.Label == "keep");
		using (Engine engine = new Engine())
		{
			CoolingSettings cooling = engine.Settings.Cooling;
			cooling.Mode = "Auto";
			cooling.AutoSensor = "gpu.temp";
			cooling.MinPercent = 25;
			cooling.StartAt = 40;
			cooling.FullAt = 85;
			Check("Automatic with no reading ever goes to 100 %", engine.TargetPercent(out string why) == 100.0);
			engine.Sensors.Set("gpu.temp", 40f);
			Check("Automatic follows the curve (40 °C → lowest speed)", Math.Abs(engine.TargetPercent(out why).Value - 25.0) < 0.01);
			engine.Sensors.Set("gpu.temp", 62.5f);
			Check("Automatic halfway (62.5 °C → 62.5 %)", Math.Abs(engine.TargetPercent(out why).Value - 62.5) < 0.01);
			engine.Sensors.Backdate("gpu.temp", 4.0);
			Check("A 4 s old reading still counts", engine.TargetPercent(out why).Value < 100.0);
			engine.Sensors.Backdate("gpu.temp", 6.0);
			Check("A reading lost for over 5 s gives 100 %", engine.TargetPercent(out why) == 100.0);
			cooling.Mode = "Hold";
			Check("Hold never controls the fans", !engine.TargetPercent(out why).HasValue);
		}
		Check("Preset speeds", CoolingSettings.PresetPercent("Full", 0) == 100 && CoolingSettings.PresetPercent("Custom", 63) == 63 && CoolingSettings.PresetPercent("Hold", 0) == -1);
	}
}
