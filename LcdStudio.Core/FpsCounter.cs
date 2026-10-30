using System;
using System.Collections.Generic;

namespace LcdStudio.Core;

internal sealed class FpsCounter
{
	private readonly Queue<DateTime> _times = new Queue<DateTime>();

	public double Tick()
	{
		DateTime utcNow = DateTime.UtcNow;
		_times.Enqueue(utcNow);
		while (_times.Count > 0 && (utcNow - _times.Peek()).TotalSeconds > 3.0)
		{
			_times.Dequeue();
		}
		if (_times.Count >= 2)
		{
			return (double)(_times.Count - 1) / Math.Max(0.001, (utcNow - _times.Peek()).TotalSeconds);
		}
		return 0.0;
	}
}
