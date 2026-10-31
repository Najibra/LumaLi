using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace LcdStudio.Core;

/// <summary>
/// A pause accurate to about a millisecond. Ordinary waits on Windows end only on the system's 15.6 ms tick, so asking
/// for 13 ms between screen frames could sleep 31 and hold the screen near 20 fps; Windows' high-resolution timer ends
/// when asked, without raising the timer rate for the whole machine.
/// </summary>
internal sealed class PreciseWait : WaitHandle
{
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period, IntPtr completion, IntPtr arg, [MarshalAs(UnmanagedType.Bool)] bool resume);

	private const uint HighResolution = 0x2, AllAccess = 0x1F0003;

	private PreciseWait(IntPtr handle)
	{
		SafeWaitHandle = new SafeWaitHandle(handle, ownsHandle: true);
	}

	/// <summary>The timer, or null where Windows has no high-resolution one (before Windows 10 1803).</summary>
	public static PreciseWait? Create()
	{
		IntPtr h = CreateWaitableTimerExW(IntPtr.Zero, null, HighResolution, AllAccess);
		return h == IntPtr.Zero ? null : new PreciseWait(h);
	}

	/// <summary>Waits the given time, or less if cancelled or woken.</summary>
	public void Wait(CancellationToken ct, double ms, WaitHandle? wake = null)
	{
		if (ms <= 0.0)
		{
			ct.ThrowIfCancellationRequested();
			return;
		}
		// due time in 100 ns steps, negative for "from now"
		long due = -(long)(ms * 10000.0);
		if (!SetWaitableTimer(SafeWaitHandle, ref due, 0, IntPtr.Zero, IntPtr.Zero, resume: false))
		{
			ct.WaitHandle.WaitOne((int)Math.Ceiling(ms));
			return;
		}
		WaitHandle.WaitAny(wake == null ? new WaitHandle[] { ct.WaitHandle, this } : new WaitHandle[] { ct.WaitHandle, wake, this });
	}
}
