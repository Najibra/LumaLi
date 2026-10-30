using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LcdStudio.Core;

public static class Store
{
	private static readonly JsonSerializerOptions Json;

	public static string Folder { get; }

	public static string Uploads => Path.Combine(Folder, "uploads");

	private static string SettingsPath => Path.Combine(Folder, "settings.json");

	private static string LayoutsPath => Path.Combine(Folder, "layouts.json");

	public static Settings LoadSettings()
	{
		try
		{
			if (File.Exists(SettingsPath))
			{
				return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath), Json) ?? new Settings();
			}
		}
		catch (Exception ex)
		{
			Log.Write("settings unreadable, using defaults: " + ex.Message);
			BackupBad(SettingsPath);
		}
		return new Settings();
	}

	public static List<ScreenLayout> LoadLayouts()
	{
		try
		{
			if (File.Exists(LayoutsPath))
			{
				return JsonSerializer.Deserialize<List<ScreenLayout>>(File.ReadAllText(LayoutsPath), Json) ?? new List<ScreenLayout>();
			}
		}
		catch (Exception ex)
		{
			Log.Write("layouts unreadable: " + ex.Message);
			BackupBad(LayoutsPath);
		}
		return new List<ScreenLayout>();
	}

	private static void BackupBad(string path)
	{
		try
		{
			File.Copy(path, path + ".bad", overwrite: true);
		}
		catch
		{
		}
	}

	public static void Save(Settings s)
	{
		WriteAtomic(SettingsPath, JsonSerializer.Serialize(s, Json));
	}

	public static void SaveLayouts(IEnumerable<ScreenLayout> user)
	{
		WriteAtomic(LayoutsPath, JsonSerializer.Serialize(user.Where((ScreenLayout l) => !l.BuiltIn).ToList(), Json));
	}

	public static string ToJson(ScreenLayout l)
	{
		return JsonSerializer.Serialize(l, Json);
	}

	public static ScreenLayout? LayoutFromJson(string json)
	{
		return JsonSerializer.Deserialize<ScreenLayout>(json, Json);
	}

	private static void WriteAtomic(string path, string text)
	{
		Directory.CreateDirectory(Folder);
		string text2 = path + ".tmp";
		File.WriteAllText(text2, text);
		if (File.Exists(path))
		{
			File.Replace(text2, path, path + ".previous");
		}
		else
		{
			File.Move(text2, path);
		}
	}

	public static string Import(string file)
	{
		Directory.CreateDirectory(Uploads);
		if (Path.GetDirectoryName(Path.GetFullPath(file)).Equals(Uploads, StringComparison.OrdinalIgnoreCase))
		{
			return file;
		}
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file);
		string extension = Path.GetExtension(file);
		string text = Path.Combine(Uploads, fileNameWithoutExtension + extension);
		int num = 2;
		while (File.Exists(text))
		{
			text = Path.Combine(Uploads, $"{fileNameWithoutExtension}-{num}{extension}");
			num++;
		}
		File.Copy(file, text);
		return text;
	}

	static Store()
	{
		string environmentVariable = Environment.GetEnvironmentVariable("LUMALI_DATA") ?? Environment.GetEnvironmentVariable("LCDSTUDIO_DATA");
		string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		string folder = Path.Combine(appData, "LumaLi"), before = Path.Combine(appData, "LCD Studio");
		// settings, layouts and uploads from before the rename move across on first run
		if (string.IsNullOrEmpty(environmentVariable) && !Directory.Exists(folder) && Directory.Exists(before))
		{
			try
			{
				Directory.Move(before, folder);
			}
			catch
			{
				folder = before;
			}
		}
		Folder = ((environmentVariable != null && environmentVariable.Length > 0) ? environmentVariable : folder);
		Json = new JsonSerializerOptions
		{
			WriteIndented = true,
			Converters = { (JsonConverter)new JsonStringEnumConverter() },
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
		};
	}
}
