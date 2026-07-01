using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HitEducation.App;

public static class MemoryOptimizer
{
	private static DateTime lastTrim = DateTime.MinValue;

	private static int scheduledVersion;

	public static void TrimSoon(bool force = false)
	{
		if (!force && (DateTime.Now - lastTrim).TotalSeconds < 3) return;
		lastTrim = DateTime.Now;
		_ = Task.Run(Trim);
	}

	public static async void TrimAfterDelay(int milliseconds, bool force = false)
	{
		var version = Interlocked.Increment(ref scheduledVersion);
		await Task.Delay(milliseconds);
		if (version != scheduledVersion) return;
		TrimSoon(force);
	}

	public static async void TrimOnceAfterDelay(int milliseconds, bool force = false)
	{
		await Task.Delay(milliseconds);
		TrimSoon(force);
	}

	public static void AfterUserAction() => TrimAfterDelay(1600, force: true);

	private static void Trim()
	{
		try
		{
			GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
			GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
			GC.WaitForPendingFinalizers();
			GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
			using var process = Process.GetCurrentProcess();
			EmptyWorkingSet(process.Handle);
		}
		catch (Exception exception)
		{
			AppLogger.Error("Memory trim failed", exception);
		}
	}

	[DllImport("psapi.dll")]
	private static extern bool EmptyWorkingSet(IntPtr processHandle);
}
