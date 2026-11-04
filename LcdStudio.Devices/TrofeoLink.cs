using System;
using System.Drawing;
using System.IO;
using LcdStudio.Native;

namespace LcdStudio.Devices;

/// <summary>
/// Thermalright Trofeo Vision 9.16" (0416:5408 "LY", 0416:5409 "LY1") over the vendor's chunked bulk protocol: a
/// 2048-byte handshake whose 512-byte reply carries the panel's PM/SUB fingerprint, then every picture as a JPEG
/// sliced into 512-byte chunks (16-byte header + 496 data) written in 4096-byte USB writes and answered with a
/// 512-byte ACK. The panel reverts to its own logo about 2-3 s after the last frame, so a static picture has to be
/// re-sent every few hundred milliseconds; the screen loop is told that separately.
/// </summary>
public sealed class TrofeoLink : IScreenLink
{
	/// <summary>The C#'s own ceiling: its JPEG encoder never sends this many bytes or more.</summary>
	public const int MaxFrame = 450000;

	private const int ChunkSize = 512;

	private const int ChunkHeader = 16;

	private const int ChunkData = 496;

	private const int WriteSize = 4096;

	private static readonly byte[] Greeting = BuildGreeting();

	private readonly UsbDeviceInfo _info;

	private readonly bool _ly1;

	private readonly byte[] _scratch = new byte[WriteSize];

	private readonly int _base;

	private WinUsb _usb;

	/// <summary>The panel's own canvas, as it takes a frame (upright, before the wire's turn).</summary>
	public Size PanelSize { get; }

	/// <summary>The handshake's PM byte, for diagnostics.</summary>
	public int Pm { get; }

	/// <summary>The handshake's SUB byte, for diagnostics.</summary>
	public int Sub { get; }

	public string? Firmware { get; }

	public string LastAck { get; private set; } = "";

	public TrofeoLink(UsbDeviceInfo info)
	{
		_info = info;
		_ly1 = info.Path.Contains("pid_5409", StringComparison.OrdinalIgnoreCase);
		_usb = new WinUsb(info);
		try
		{
			_usb.Flush();
			_usb.Write(Greeting);
			byte[] reply = _usb.Read(1000);
			if (reply.Length < 37 || reply[0] != 3 || reply[1] != 0xFF || reply[8] != 1)
			{
				throw new IOException($"{info.Name}: handshake rejected ({reply.Length} bytes, [{First(reply)}])");
			}
			// LY1 takes PM as 49 + [20] and the SUB byte as-is; LY takes PM as 64 + [20] (anything under 4 reads as 1)
			// and the vendor's own pmSub, which is [22] + 1 (USBLCDNEW ThreadSendDeviceDataLY packs 1 + array[22]).
			if (_ly1)
			{
				Pm = 49 + reply[20];
				Sub = reply[22];
			}
			else
			{
				int raw = reply[20];
				if (raw <= 3)
				{
					raw = 1;
				}
				Pm = 64 + raw;
				Sub = reply[22] + 1;
			}
			PanelSize = PanelFor(Pm);
			_base = BaseAngle(PanelSize, Sub);
			Firmware = $"PM {Pm} · SUB {Sub} · {PanelSize.Width}×{PanelSize.Height}";
			_usb.Flush();
		}
		catch
		{
			_usb.Dispose();
			throw;
		}
	}

	/// <summary>The turn the wire frame takes before encoding: the panel's base minus the user's 180° flip.</summary>
	public int EncodeAngle(bool flip)
	{
		return ((_base - (flip ? 180 : 0)) % 360 + 360) % 360;
	}

	public void SendFrame(byte[] jpeg, int brightness)
	{
		// brightness is baked into the picture before it is encoded: the LY wire has no brightness command
		if (jpeg == null || jpeg.Length == 0)
		{
			return;
		}
		if (jpeg.Length >= MaxFrame)
		{
			throw new InvalidOperationException($"Frame is {jpeg.Length / 1024} KB; limit {MaxFrame / 1024} KB. Lower picture quality.");
		}
		byte[] frame = Chunk(jpeg);
		int pos = 0;
		while (pos < frame.Length)
		{
			int remaining = frame.Length - pos;
			int take = ((remaining >= WriteSize) ? WriteSize : ((!_ly1) ? Math.Min(2048, remaining) : remaining));
			Array.Copy(frame, pos, _scratch, 0, take);
			_usb.Write(_scratch, take);
			pos += WriteSize;
		}
		// the panel answers each frame with a 512-byte ACK; its content is never read, but the pipe has to be drained
		byte[] ack = _usb.Read(1000);
		LastAck = ((ack.Length == 0) ? "none" : BitConverter.ToString(ack, 0, Math.Min(12, ack.Length)));
	}

	public string Clock(bool on, byte style = 2)
	{
		// no firmware clock on this family
		return "";
	}

	/// <summary>Lets go without a stop command: the panel keeps the last frame, then falls back to its own logo.</summary>
	public void Leave()
	{
		_usb.Dispose();
	}

	public void Dispose()
	{
		_usb.Dispose();
	}

	private static byte[] BuildGreeting()
	{
		byte[] payload = new byte[2048];
		byte[] header = new byte[16]
		{
			2, 255, 0, 0, 0, 0, 0, 0,
			1, 0, 0, 0, 0, 0, 0, 0
		};
		Array.Copy(header, payload, header.Length);
		return payload;
	}

	/// <summary>The canvas the panel's PM byte names; an unnamed PM reads as the 9.16" strip it is.</summary>
	private static Size PanelFor(int pm)
	{
		return pm switch
		{
			68 => new Size(1280, 480),
			69 => new Size(1920, 440),
			_ => new Size(1920, 462)
		};
	}

	/// <summary>
	/// The C#'s encode base for this panel, from its ImageToJpg switch: 1920 × 462 and 1920 × 440 are 180° except at
	/// SUB 2-4 (portrait mount), and 1280 × 480 is 0° except at SUB 2 (90°). The sign is negative for all of them:
	/// the wire turns (base − orientation). A panel whose fingerprint is not in the table gets the 9.16" default.
	/// </summary>
	private static int BaseAngle(Size panel, int sub)
	{
		if (panel.Width == 1280)
		{
			return (sub == 2) ? 90 : 0;
		}
		return ((sub >= 2 && sub <= 4) ? 0 : 180);
	}

	private byte[] Chunk(byte[] payload)
	{
		int total = payload.Length;
		int chunks = total / ChunkData + 1;
		int last = total % ChunkData;
		// LY pads the chunk count to a multiple of four with zeros; LY1 sends it as it is
		int padded = (_ly1 ? chunks : ((chunks + 3) / 4 * 4));
		byte[] frame = new byte[padded * ChunkSize];
		for (int i = 0; i < chunks; i++)
		{
			int at = i * ChunkSize;
			int len = ((i == chunks - 1) ? last : ChunkData);
			frame[at] = 1;
			frame[at + 1] = 255;
			frame[at + 2] = (byte)total;
			frame[at + 3] = (byte)(total >> 8);
			frame[at + 4] = (byte)(total >> 16);
			frame[at + 5] = (byte)(total >> 24);
			frame[at + 6] = (byte)len;
			frame[at + 7] = (byte)(len >> 8);
			frame[at + 8] = (byte)(_ly1 ? 2 : 1);
			frame[at + 9] = (byte)chunks;
			frame[at + 10] = (byte)(chunks >> 8);
			frame[at + 11] = (byte)i;
			frame[at + 12] = (byte)(i >> 8);
			if (len > 0)
			{
				Buffer.BlockCopy(payload, i * ChunkData, frame, at + ChunkHeader, len);
			}
		}
		return frame;
	}

	private static string First(byte[] data)
	{
		if (data.Length == 0)
		{
			return "no answer";
		}
		return BitConverter.ToString(data, 0, Math.Min(8, data.Length));
	}
}
