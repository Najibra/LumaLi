using System;
using System.Drawing;

namespace LcdStudio.Core;

public sealed record InflatedGlyphs(Bitmap Bitmap, RectangleF Glyphs) : IDisposable
{
	public void Dispose()
	{
		Bitmap.Dispose();
	}
}
