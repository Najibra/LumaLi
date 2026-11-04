using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LcdStudio.Core;

/// <summary>
/// Looks for a newer LumaLi on GitHub (the latest release's tag) a minute after start and then once a day. Nothing is
/// downloaded: Home shows a note with a button that opens the release page.
/// </summary>
public static class Updates
{
	public const string ReleasesPage = "https://github.com/Najibra/LumaLi/releases/latest";

	private const string Api = "https://api.github.com/repos/Najibra/LumaLi/releases/latest";

	private static Thread? _thread;

	/// <summary>The newer version found ("1.4.0"), or null.</summary>
	public static string? Available { get; private set; }

	/// <summary>The page of that release.</summary>
	public static string Page { get; private set; } = ReleasesPage;

	public static Version Running
	{
		get
		{
			Version? v = Assembly.GetEntryAssembly()?.GetName().Version;
			return v == null ? new Version(0, 0, 0) : new Version(v.Major, v.Minor, Math.Max(0, v.Build));
		}
	}

	public static void Start(Func<bool> enabled)
	{
		if (_thread != null)
		{
			return;
		}
		_thread = new Thread(() =>
		{
			Thread.Sleep(TimeSpan.FromMinutes(1));
			while (true)
			{
				if (enabled())
				{
					try
					{
						CheckAsync().GetAwaiter().GetResult();
					}
					catch (Exception ex)
					{
						Log.Write("update check: " + ex.Message);
					}
				}
				else
				{
					Available = null;
				}
				Thread.Sleep(TimeSpan.FromHours(24));
			}
		}) { IsBackground = true, Name = "LumaLi updates" };
		_thread.Start();
	}

	private static async Task CheckAsync()
	{
		using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
		http.DefaultRequestHeaders.UserAgent.ParseAdd("LumaLi/" + Running.ToString(3));
		http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
		using JsonDocument doc = JsonDocument.Parse(await http.GetStringAsync(Api));
		string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
		string url = doc.RootElement.TryGetProperty("html_url", out JsonElement u) ? u.GetString() ?? ReleasesPage : ReleasesPage;
		Version? latest = Parse(tag);
		if (latest != null && latest > Running)
		{
			if (Available != latest.ToString(3))
			{
				Log.Write($"update check: {latest.ToString(3)} is available (running {Running.ToString(3)})");
			}
			Available = latest.ToString(3);
			Page = url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase) ? url : ReleasesPage;
		}
		else
		{
			Available = null;
		}
	}

	/// <summary>"v1.3.2" → 1.3.2; anything else → null.</summary>
	public static Version? Parse(string tag)
	{
		string s = tag.Trim().TrimStart('v', 'V');
		int dash = s.IndexOfAny(new[] { '-', '+', ' ' });
		if (dash >= 0)
		{
			s = s[..dash];
		}
		if (!Version.TryParse(s, out Version? v))
		{
			return null;
		}
		return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
	}
}
