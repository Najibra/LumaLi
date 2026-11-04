using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LcdStudio.Core;

/// <summary>The weather card's settings: the place (found by name with Open-Meteo's free geocoding) and the units.</summary>
public sealed class WeatherSettings
{
	/// <summary>The place as typed, e.g. "Dubai". Empty = weather off.</summary>
	public string City { get; set; } = "";

	/// <summary>The place as found ("Dubai, United Arab Emirates"), and where it is.</summary>
	public string Place { get; set; } = "";

	public double Latitude { get; set; }

	public double Longitude { get; set; }

	public bool Fahrenheit { get; set; }

	public bool On => City.Length > 0 && (Latitude != 0.0 || Longitude != 0.0);
}

/// <summary>The weather now and today's range, in °C (the screen converts when Fahrenheit is chosen).</summary>
public sealed record WeatherNow(double Temp, double Feels, double Humidity, double Wind, int Code, bool IsDay, double High, double Low, DateTime Sunrise, DateTime Sunset, string Place, DateTime At)
{
	/// <summary>What WMO weather code this is, in words.</summary>
	public string Text => Weather.Describe(Code);

	/// <summary>Clear, Cloudy, Fog, Rain, Snow or Storm: what the animated icons draw.</summary>
	public string Kind => Weather.KindOf(Code);
}

/// <summary>
/// The weather for one place from Open-Meteo (free, no account or key): fetched every 15 minutes by the full app and
/// kept in <see cref="Current"/>. Only the place's coordinates are sent. Previews use <see cref="Demo"/>.
/// </summary>
public static class Weather
{
	private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

	private static WeatherNow? _current;

	private static Thread? _thread;

	private static readonly AutoResetEvent Wake = new AutoResetEvent(false);

	private static Func<WeatherSettings>? _settings;

	private static volatile bool _demo;

	public static WeatherNow? Current => Volatile.Read(ref _current);

	/// <summary>The temperatures are shown in Fahrenheit.</summary>
	public static bool Fahrenheit { get; set; }

	static Weather()
	{
		Http.DefaultRequestHeaders.UserAgent.ParseAdd("LumaLi");
	}

	public static string Degrees(double celsius)
	{
		double v = Fahrenheit ? celsius * 9.0 / 5.0 + 32.0 : celsius;
		return Math.Round(v).ToString("0", CultureInfo.InvariantCulture) + "°";
	}

	public static void Start(Func<WeatherSettings> settings)
	{
		_settings = settings;
		if (_thread != null || _demo)
		{
			return;
		}
		_thread = new Thread(Loop) { IsBackground = true, Name = "LumaLi weather" };
		_thread.Start();
	}

	/// <summary>Fetch again now (the place changed).</summary>
	public static void Refresh()
	{
		Wake.Set();
	}

	/// <summary>Made-up weather for previews: kind is Clear, Cloudy, Fog, Rain, Snow or Storm.</summary>
	public static void Demo(string kind = "Clear", bool day = true)
	{
		_demo = true;
		int code = kind switch { "Cloudy" => 3, "Fog" => 45, "Rain" => 63, "Snow" => 73, "Storm" => 95, "Partly" => 2, _ => 0 };
		DateTime today = DateTime.Today;
		Volatile.Write(ref _current, new WeatherNow(31.4, 34.0, 48, 14, code, day, 36, 27, today.AddHours(6.2), today.AddHours(17.6), "Dubai", DateTime.UtcNow));
	}

	private static void Loop()
	{
		while (true)
		{
			WeatherSettings? s = null;
			try
			{
				s = _settings?.Invoke();
			}
			catch
			{
			}
			bool ok = false;
			if (s != null && s.On)
			{
				Fahrenheit = s.Fahrenheit;
				try
				{
					WeatherNow? w = FetchAsync(s.Latitude, s.Longitude, s.Place.Length > 0 ? s.Place : s.City).GetAwaiter().GetResult();
					if (w != null)
					{
						Volatile.Write(ref _current, w);
						ok = true;
					}
				}
				catch (Exception ex)
				{
					Log.Write("weather: " + ex.Message);
				}
			}
			else
			{
				Volatile.Write(ref _current, null);
			}
			// every 15 minutes, or a minute after a failed try; sooner when the place changes
			Wake.WaitOne(ok || s == null || !s.On ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(1));
		}
	}

	private static async Task<WeatherNow?> FetchAsync(double lat, double lon, string place)
	{
		string url = string.Format(CultureInfo.InvariantCulture,
			"https://api.open-meteo.com/v1/forecast?latitude={0:0.####}&longitude={1:0.####}&current=temperature_2m,relative_humidity_2m,apparent_temperature,is_day,weather_code,wind_speed_10m&daily=temperature_2m_max,temperature_2m_min,sunrise,sunset&timezone=auto&forecast_days=1",
			lat, lon);
		using JsonDocument doc = JsonDocument.Parse(await Http.GetStringAsync(url));
		JsonElement cur = doc.RootElement.GetProperty("current");
		JsonElement daily = doc.RootElement.GetProperty("daily");
		double D(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0.0;
		double First(string name) => daily.TryGetProperty(name, out JsonElement a) && a.GetArrayLength() > 0 && a[0].ValueKind == JsonValueKind.Number ? a[0].GetDouble() : double.NaN;
		DateTime Time(string name) => daily.TryGetProperty(name, out JsonElement a) && a.GetArrayLength() > 0 && DateTime.TryParse(a[0].GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d) ? d : DateTime.MinValue;
		double temp = D(cur, "temperature_2m");
		double high = First("temperature_2m_max"), low = First("temperature_2m_min");
		return new WeatherNow(temp, D(cur, "apparent_temperature"), D(cur, "relative_humidity_2m"), D(cur, "wind_speed_10m"), (int)D(cur, "weather_code"), D(cur, "is_day") > 0.5,
			double.IsNaN(high) ? temp : high, double.IsNaN(low) ? temp : low, Time("sunrise"), Time("sunset"), place, DateTime.UtcNow);
	}

	/// <summary>Finds a place by name: (display name, latitude, longitude), or null.</summary>
	public static async Task<(string Place, double Lat, double Lon)?> FindAsync(string name)
	{
		string url = "https://geocoding-api.open-meteo.com/v1/search?count=1&format=json&name=" + Uri.EscapeDataString(name.Trim());
		using JsonDocument doc = JsonDocument.Parse(await Http.GetStringAsync(url));
		if (!doc.RootElement.TryGetProperty("results", out JsonElement results) || results.GetArrayLength() == 0)
		{
			return null;
		}
		JsonElement r = results[0];
		string city = r.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? name : name;
		string country = r.TryGetProperty("country", out JsonElement c) ? c.GetString() ?? "" : "";
		return (country.Length > 0 ? city + ", " + country : city, r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble());
	}

	public static string Describe(int code) => code switch
	{
		0 => "Clear",
		1 => "Mostly clear",
		2 => "Partly cloudy",
		3 => "Overcast",
		45 or 48 => "Fog",
		51 or 53 or 55 => "Drizzle",
		56 or 57 => "Freezing drizzle",
		61 => "Light rain",
		63 => "Rain",
		65 => "Heavy rain",
		66 or 67 => "Freezing rain",
		71 => "Light snow",
		73 => "Snow",
		75 => "Heavy snow",
		77 => "Snow grains",
		80 or 81 => "Showers",
		82 => "Heavy showers",
		85 or 86 => "Snow showers",
		95 => "Thunderstorm",
		96 or 99 => "Storm with hail",
		_ => "—"
	};

	public static string KindOf(int code) => code switch
	{
		0 or 1 => "Clear",
		2 => "Partly",
		3 => "Cloudy",
		45 or 48 => "Fog",
		>= 51 and <= 67 or >= 80 and <= 82 => "Rain",
		>= 71 and <= 77 or 85 or 86 => "Snow",
		>= 95 => "Storm",
		_ => "Cloudy"
	};
}
