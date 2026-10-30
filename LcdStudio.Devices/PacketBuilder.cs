using System;
using System.Security.Cryptography;
using System.Text;

namespace LcdStudio.Devices;

public sealed class PacketBuilder
{
	private static readonly byte[] Key = Encoding.ASCII.GetBytes("slv3tuzx");

	private readonly DES _des;

	private uint _last;

	public bool Legacy { get; }

	public PacketBuilder(bool legacy)
	{
		Legacy = legacy;
		_des = DES.Create();
		_des.Key = Key;
		_des.IV = Key;
		_des.Mode = CipherMode.CBC;
		_des.Padding = PaddingMode.PKCS7;
	}

	public byte[] Build(byte command, params byte[] parameters)
	{
		byte[] array = new byte[Legacy ? 504 : 500];
		array[0] = command;
		array[2] = 26;
		array[3] = 109;
		uint num = (uint)Environment.TickCount64;
		if (num <= _last)
		{
			num = _last + 1;
		}
		_last = num;
		BitConverter.GetBytes(num).CopyTo(array, 4);
		Array.Copy(parameters, 0, array, 8, Math.Min(parameters.Length, 492));
		byte[] array2;
		lock (_des)
		{
			array2 = _des.EncryptCbc(array, Key);
		}
		byte[] array3 = new byte[512];
		Array.Copy(array2, array3, Math.Min(array2.Length, 512));
		if (!Legacy)
		{
			array3[510] = 161;
			array3[511] = 26;
		}
		return array3;
	}

	public static byte[] BigEndian(int n)
	{
		return new byte[4]
		{
			(byte)(n >> 24),
			(byte)(n >> 16),
			(byte)(n >> 8),
			(byte)n
		};
	}
}
