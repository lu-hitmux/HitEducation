using System;
using Microsoft.Win32;

namespace HitEducation.App;

public static class AutoStartManager
{
	private const string AppName = "HitEducation";

	private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

	private const string StartupApprovedRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

	private static readonly byte[] EnabledStartupApprovedValue = [0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

	public static void Save(bool enabled)
	{
		using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
		if (runKey is null) return;

		if (enabled)
		{
			runKey.SetValue(AppName, GetStartupCommand(), RegistryValueKind.String);
			using var approvedKey = Registry.CurrentUser.CreateSubKey(StartupApprovedRunKeyPath, writable: true);
			approvedKey?.SetValue(AppName, EnabledStartupApprovedValue, RegistryValueKind.Binary);
		}
		else
		{
			runKey.DeleteValue(AppName, throwOnMissingValue: false);
			using var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedRunKeyPath, writable: true);
			approvedKey?.DeleteValue(AppName, throwOnMissingValue: false);
		}
	}

	private static string GetStartupCommand()
	{
		var path = Environment.ProcessPath;
		if (string.IsNullOrWhiteSpace(path)) path = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
		return string.IsNullOrWhiteSpace(path) ? "" : $"\"{path}\"";
	}
}
