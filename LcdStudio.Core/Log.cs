using System;
using System.IO;

namespace LcdStudio.Core;

public static class Log
{
	private static readonly object Gate = new object();

	public static string Path => System.IO.Path.Combine(Store.Folder, "log.txt");

	public static void Write(string message)
	{
		lock (Gate)
		{
			try
			{
				Directory.CreateDirectory(Store.Folder);
				FileInfo fileInfo = new FileInfo(Path);
				if (fileInfo.Exists && fileInfo.Length > 1000000)
				{
					File.Move(Path, Path + ".old", overwrite: true);
				}
				File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
			}
			catch
			{
			}
		}
	}
}
