using System;
using System.Text;
using LcdStudio.Native;

namespace LcdStudio.Devices;

/// <summary>What the fan-screen loop needs from a fan's screen, whichever way it is connected.</summary>
public interface IFanLink : IDisposable
{
	string? Firmware { get; }

	void SendFrame(byte[] jpeg, int brightness);
}

/// <summary>
/// A wired TL LCD fan (04FC:7393), 400 × 400, over HID. Experimental: written from sgtaziz/lian-li-linux (MIT) and not
/// yet tried on real hardware. Every message is a 512-byte output report: report ID 2, the command, the whole
/// message's size (4 bytes), the packet's number (3 bytes) and its payload length (2 bytes), then up to 501 bytes of
/// payload. Frames stream as JPEGs with the sync command, which the fan doesn't answer. Unlike the Linux driver this
/// never writes a serial number to the fan; fans are told apart by where they are plugged in.
/// </summary>
public sealed class TlLcdLink : IFanLink
{
	private const byte ReportId = 2;

	private const int Packet = 512, Header = 11, Chunk = Packet - Header;

	private const byte CmdHandshake = 60, CmdProductInfo = 61, CmdLcdControl = 64, CmdSyncJpeg = 0x46;

	private const byte ModeSetting = 5;

	private readonly UsbDeviceInfo _info;

	private HidPort? _port;

	private int _brightness = -1;

	public string? Firmware { get; private set; }

	public TlLcdLink(UsbDeviceInfo info)
	{
		_info = info;
	}

	public static byte[] Build(byte cmd, int total, int number, ReadOnlySpan<byte> payload)
	{
		byte[] p = new byte[Packet];
		int len = Math.Min(payload.Length, Chunk);
		p[0] = ReportId;
		p[1] = cmd;
		p[2] = (byte)(total >> 24);
		p[3] = (byte)(total >> 16);
		p[4] = (byte)(total >> 8);
		p[5] = (byte)total;
		p[6] = (byte)(number >> 16);
		p[7] = (byte)(number >> 8);
		p[8] = (byte)number;
		p[9] = (byte)(len >> 8);
		p[10] = (byte)len;
		payload[..len].CopyTo(p.AsSpan(Header));
		return p;
	}

	private byte[] Ask(byte cmd, byte[] payload, int timeoutMs)
	{
		_port!.Write(Build(cmd, payload.Length, 0, payload));
		return _port.Read(timeoutMs);
	}

	private void Open()
	{
		_port = new HidPort(_info.Path);
		_port.Flush();
		try
		{
			Ask(CmdHandshake, Array.Empty<byte>(), 3000);
			// the product info comes back as two reports: the version, then a build date
			byte[] r = Ask(CmdProductInfo, Array.Empty<byte>(), 3000);
			if (r.Length > Header)
			{
				int len = Math.Min((r[9] << 8) | r[10], r.Length - Header);
				string v = Encoding.ASCII.GetString(r, Header, Math.Max(0, len)).TrimEnd('\0').Trim();
				Firmware = v.Length > 0 ? v : null;
			}
			_port.Read(500);
		}
		catch (TimeoutException)
		{
		}
		_brightness = -1;
	}

	private void SetBrightness(int percent)
	{
		percent = Math.Clamp(percent, 0, 100);
		if (percent == _brightness)
		{
			return;
		}
		// mode 5 is the settings: brightness at byte 4, 30 at byte 5 (as the fan's own software sends), rotation 0
		byte[] payload = new byte[11];
		payload[0] = ModeSetting;
		payload[4] = (byte)percent;
		payload[5] = 30;
		Ask(CmdLcdControl, payload, 300);
		_brightness = percent;
	}

	public void SendFrame(byte[] jpeg, int brightness)
	{
		try
		{
			if (_port == null)
			{
				Open();
			}
			SetBrightness(brightness);
			for (int at = 0, n = 0; at < jpeg.Length; at += Chunk, n++)
			{
				_port!.Write(Build(CmdSyncJpeg, jpeg.Length, n, jpeg.AsSpan(at, Math.Min(Chunk, jpeg.Length - at))));
			}
		}
		catch
		{
			_port?.Dispose();
			_port = null;
			throw;
		}
	}

	public void Dispose()
	{
		_port?.Dispose();
		_port = null;
	}
}
