using System;
using System.IO;

namespace HitEducation.App;

public static class AppLogger
{
	private static readonly object LockObject = new();

	public static string LogDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HitEducation", "logs");

	public static string LogFile { get; } = Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");

	public static void Info(string message) => Write("INFO", message, null);

	public static void Error(string message, Exception exception) => Write("ERROR", message, exception);

	private static void Write(string level, string message, Exception? exception)
	{
		try
		{
			Directory.CreateDirectory(LogDirectory);
			var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
			if (exception is not null)
			{
				line += Environment.NewLine + exception;
			}

			lock (LockObject)
			{
				File.AppendAllText(LogFile, line + Environment.NewLine);
			}
		}
		catch
		{
			// Logging must never crash the app.
		}
	}
}
