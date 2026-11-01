using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace LcdStudio.Core;

/// <summary>
/// Fonts bundled in the exe (Montserrat ExtraLight and Light, SIL Open Font License), so layouts can use them on a PC
/// that doesn't have them installed. Loaded once into a private collection whose memory is kept for the app's life.
/// </summary>
public static class BundledFonts
{
	private static readonly object Gate = new object();

	private static PrivateFontCollection? _fonts;

	private static readonly string[] Files = { "Montserrat-ExtraLight.ttf", "Montserrat-Light.ttf" };

	private static PrivateFontCollection Fonts()
	{
		lock (Gate)
		{
			if (_fonts != null)
			{
				return _fonts;
			}
			var pfc = new PrivateFontCollection();
			foreach (string name in Files)
			{
				using Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
				if (s == null)
				{
					continue;
				}
				var bytes = new byte[s.Length];
				s.ReadExactly(bytes);
				// the collection reads from this memory for as long as it is used, so it is never freed
				IntPtr mem = Marshal.AllocCoTaskMem(bytes.Length);
				Marshal.Copy(bytes, 0, mem, bytes.Length);
				pfc.AddMemoryFont(mem, bytes.Length);
			}
			return _fonts = pfc;
		}
	}

	/// <summary>A new family for a bundled font by its family name ("Montserrat ExtraLight", "Montserrat Light"), or null.</summary>
	public static FontFamily? Family(string name)
	{
		if (!name.StartsWith("Montserrat", StringComparison.Ordinal))
		{
			return null;
		}
		try
		{
			PrivateFontCollection pfc = Fonts();
			foreach (FontFamily f in pfc.Families)
			{
				if (f.Name == name)
				{
					return new FontFamily(name, pfc);
				}
			}
		}
		catch
		{
		}
		return null;
	}

	/// <summary>The family names in the bundle, for checks.</summary>
	public static string[] Names()
	{
		try
		{
			return Array.ConvertAll(Fonts().Families, f => f.Name);
		}
		catch
		{
			return Array.Empty<string>();
		}
	}
}
