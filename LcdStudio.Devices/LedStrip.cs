using System;
using LcdStudio.Native;

namespace LcdStudio.Devices;

public sealed class LedStrip : IDisposable
{
	public const int Count = 60;

	private readonly WinUsb _usb;

	public LedStrip(UsbDeviceInfo info)
	{
		_usb = new WinUsb(info);
	}

	public void Send(ReadOnlySpan<(byte R, byte G, byte B)> colors)
	{
		for (int i = 0; i < 60; i += 20)
		{
			byte[] array = new byte[64];
			array[0] = 17;
			array[1] = (byte)i;
			for (int j = 0; j < 20 && i + j < colors.Length; j++)
			{
				(byte, byte, byte) tuple = colors[i + j];
				array[4 + j * 3] = tuple.Item1;
				array[5 + j * 3] = tuple.Item2;
				array[6 + j * 3] = tuple.Item3;
			}
			_usb.Write(array);
		}
	}

	public void Dispose()
	{
		try
		{
			Send(new(byte, byte, byte)[60]);
		}
		catch
		{
		}
		_usb.Dispose();
	}
}
