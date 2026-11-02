using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;

namespace LcdStudio.UI;

/// <summary>
/// Starting LumaLi as administrator when you sign in, so CPU temperature and the game frame rate work from the start.
/// A Run-key entry can't start a program elevated, so this is a Task Scheduler task ("LumaLi") that runs at logon with
/// the highest privileges, without a prompt. Creating or removing it needs LumaLi to be running as administrator.
/// </summary>
public static class ElevatedStartup
{
	public const string TaskName = "LumaLi";

	public static bool Enabled()
	{
		try
		{
			return Run("/Query /TN \"" + TaskName + "\"", out _) == 0;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>Creates or removes the task. Returns null when it worked, else what went wrong.</summary>
	public static string? Set(bool on, string exe)
	{
		try
		{
			if (!on)
			{
				return Run("/Delete /TN \"" + TaskName + "\" /F", out string o) == 0 || !Enabled() ? null : o.Trim();
			}
			string user = Environment.UserDomainName + "\\" + Environment.UserName;
			string file = Path.Combine(Path.GetTempPath(), "LumaLi-task.xml");
			File.WriteAllText(file, TaskXml(exe, user), Encoding.Unicode);
			try
			{
				return Run("/Create /TN \"" + TaskName + "\" /XML \"" + file + "\" /F", out string output) == 0 ? null : output.Trim();
			}
			finally
			{
				File.Delete(file);
			}
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
	}

	/// <summary>
	/// The task: at this user's logon, run LumaLi in the tray with the highest privileges, interactively, with no time
	/// limit (the default would end it after three days) and not stopped on battery.
	/// </summary>
	public static string TaskXml(string exe, string user)
	{
		string u = SecurityElement.Escape(user), e = SecurityElement.Escape(exe);
		return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n"
			+ "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n"
			+ "  <RegistrationInfo><Description>Starts LumaLi as administrator when you sign in (CPU temperature, game frame rate).</Description></RegistrationInfo>\r\n"
			+ "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + u + "</UserId><Delay>PT5S</Delay></LogonTrigger></Triggers>\r\n"
			+ "  <Principals><Principal id=\"Author\"><UserId>" + u + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n"
			+ "  <Settings>\r\n"
			+ "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n"
			+ "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n"
			+ "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n"
			+ "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n"
			+ "    <Priority>7</Priority>\r\n"
			+ "  </Settings>\r\n"
			+ "  <Actions Context=\"Author\"><Exec><Command>" + e + "</Command><Arguments>--tray</Arguments></Exec></Actions>\r\n"
			+ "</Task>\r\n";
	}

	private static int Run(string args, out string output)
	{
		using Process p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		})!;
		output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
		p.WaitForExit(10000);
		return p.ExitCode;
	}
}
