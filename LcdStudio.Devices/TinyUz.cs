using System.Collections.Generic;

namespace LcdStudio.Devices;

public sealed class TinyUz
{
	private readonly List<byte> _code = new List<byte>();

	private int _bits;

	private int _index;

	private void Bit(int b)
	{
		if (_bits == 0)
		{
			_index = _code.Count;
			_code.Add(0);
		}
		_code[_index] |= (byte)(b << _bits);
		_bits = (_bits + 1) % 8;
	}

	private void Len(int v, int pack)
	{
		int i;
		for (i = 1; v >= 1 << i * pack; i++)
		{
			v -= 1 << i * pack;
		}
		while (i-- > 0)
		{
			for (int j = 0; j < pack; j++)
			{
				Bit((v >> i * pack + j) & 1);
			}
			Bit((i > 0) ? 1 : 0);
		}
	}

	public static byte[] Encode(byte[] raw)
	{
		TinyUz tinyUz = new TinyUz();
		tinyUz._code.AddRange(new byte[4] { 0, 16, 0, 0 });
		foreach (byte item in raw)
		{
			tinyUz.Bit(1);
			tinyUz._code.Add(item);
		}
		tinyUz.Bit(0);
		tinyUz.Len(3, 1);
		if (raw.Length != 0)
		{
			tinyUz.Bit(0);
		}
		tinyUz._code.Add(0);
		return tinyUz._code.ToArray();
	}
}
