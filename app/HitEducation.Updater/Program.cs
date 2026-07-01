using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

return await UpdaterProgram.RunAsync(args);

internal static class UpdaterProgram
{
	private const string AppExeName = "HitEducation.App.exe";
	private const string AppDllName = "HitEducation.App.dll";
	private static readonly HttpClient Client = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(30.0)
	};

	private static string LogFile => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"HitEducation",
		"logs",
		"updater-" + DateTime.Now.ToString("yyyyMMdd") + ".log");

	public static async Task<int> RunAsync(string[] args)
	{
		Mutex? mutex = null;
		try
		{
			Log("Updater started. Args: " + string.Join(" ", args));
			Arguments options = Arguments.Parse(args);
			if (options.CompleteUpdaterSelfUpdate)
			{
				CompleteUpdaterSelfUpdate(options);
			}
			mutex = AcquireTargetMutex(options.TargetDirectory);
			if (mutex == null)
			{
				Log("Another updater instance is already running for this target. Exiting.");
				return 0;
			}
			if (options.LegacyPackagePath != null)
			{
				InstallLegacyPackage(options);
				return 0;
			}
			ValidateRequired(options);

			UpdateManifest manifest = await LoadManifestAsync(options.VersionUrl!);
			Version currentVersion = ParseVersion(options.CurrentVersion!);
			Version latestVersion = ParseVersion(manifest.Version);
			Log($"Current version: {currentVersion}; server version: {latestVersion}");

			if (latestVersion <= currentVersion)
			{
				Log("No update available. Exiting.");
				return 0;
			}

			if (manifest.UpdateUpdater && !options.SkipUpdaterSelfUpdate)
			{
				await StartUpdaterSelfUpdateAsync(manifest, options, args);
				return 0;
			}

			await InstallAppUpdateAsync(manifest, options, currentVersion);
			Log("Update finished.");
			return 0;
		}
		catch (Exception ex)
		{
			Log("Updater failed: " + ex);
			return 1;
		}
		finally
		{
			mutex?.ReleaseMutex();
			mutex?.Dispose();
		}
	}

	private static Mutex? AcquireTargetMutex(string? targetDirectory)
	{
		string key = string.IsNullOrWhiteSpace(targetDirectory) ? "default" : Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(targetDirectory))));
		Mutex mutex = new Mutex(initiallyOwned: false, "Local\\HitEducation.Updater." + key);
		if (!mutex.WaitOne(0))
		{
			mutex.Dispose();
			return null;
		}
		return mutex;
	}

	private static void ValidateRequired(Arguments options)
	{
		if (string.IsNullOrWhiteSpace(options.TargetDirectory)
			|| string.IsNullOrWhiteSpace(options.AppPath)
			|| string.IsNullOrWhiteSpace(options.CurrentVersion)
			|| string.IsNullOrWhiteSpace(options.VersionUrl))
		{
			throw new InvalidOperationException("Missing required updater arguments.");
		}
	}

	private static void InstallLegacyPackage(Arguments options)
	{
		if (string.IsNullOrWhiteSpace(options.LegacyPackagePath)
			|| string.IsNullOrWhiteSpace(options.TargetDirectory)
			|| string.IsNullOrWhiteSpace(options.LegacyRestartPath))
		{
			throw new InvalidOperationException("Missing required legacy updater arguments.");
		}

		Log("Running legacy package install mode.");
		if (options.ProcessId.HasValue)
		{
			StopAppProcess(options.ProcessId.Value);
		}

		string extractDirectory = Path.Combine(Path.GetTempPath(), "HitEducation", "legacy-update-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(extractDirectory);
		Log("Extracting legacy app package to: " + extractDirectory);
		ZipFile.ExtractToDirectory(options.LegacyPackagePath, extractDirectory, overwriteFiles: true);
		ValidateLegacyAppPackage(extractDirectory);
		CopyAppFiles(extractDirectory, options.TargetDirectory);

		TryDelete(extractDirectory, recursive: true);
		TryDelete(options.LegacyPackagePath, recursive: false);
		RestartApp(options.LegacyRestartPath, options.TargetDirectory);
	}

	private static async Task<UpdateManifest> LoadManifestAsync(string versionUrl)
	{
		Log("Fetching manifest: " + versionUrl);
		if (File.Exists(versionUrl))
		{
			await using FileStream fileStream = File.OpenRead(versionUrl);
			return await DeserializeManifestAsync(fileStream);
		}
		using HttpResponseMessage response = await Client.GetAsync(versionUrl);
		response.EnsureSuccessStatusCode();
		await using Stream stream = await response.Content.ReadAsStreamAsync();
		return await DeserializeManifestAsync(stream);
	}

	private static async Task<UpdateManifest> DeserializeManifestAsync(Stream stream)
	{
		UpdateManifest? manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		});
		if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version))
		{
			throw new InvalidOperationException("Version file is missing the version field.");
		}
		return manifest;
	}

	private static async Task InstallAppUpdateAsync(UpdateManifest manifest, Arguments options, Version currentVersion)
	{
		if (string.IsNullOrWhiteSpace(manifest.DownloadUrl))
		{
			throw new InvalidOperationException("Version file is missing downloadUrl.");
		}
		if (string.IsNullOrWhiteSpace(manifest.Sha256))
		{
			throw new InvalidOperationException("Version file is missing sha256.");
		}

		string packageDirectory = Path.Combine(Path.GetTempPath(), "HitEducation", "updates");
		Directory.CreateDirectory(packageDirectory);
		string packagePath = Path.Combine(packageDirectory, "HitEducation.App-" + manifest.Version + ".zip");
		await DownloadFileAsync(manifest.DownloadUrl, packagePath);
		VerifySha256(packagePath, manifest.Sha256, "app package");

		string extractDirectory = Path.Combine(Path.GetTempPath(), "HitEducation", "update-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(extractDirectory);
		Log("Extracting app package to: " + extractDirectory);
		ZipFile.ExtractToDirectory(packagePath, extractDirectory, overwriteFiles: true);
		ValidateAppPackage(extractDirectory, currentVersion);

		if (options.ProcessId.HasValue)
		{
			StopAppProcess(options.ProcessId.Value);
		}

		CopyAppFiles(extractDirectory, options.TargetDirectory!);
		TryDelete(extractDirectory, recursive: true);
		TryDelete(packagePath, recursive: false);
		RestartApp(options.AppPath!, options.TargetDirectory!);
	}

	private static async Task StartUpdaterSelfUpdateAsync(UpdateManifest manifest, Arguments options, string[] originalArgs)
	{
		if (string.IsNullOrWhiteSpace(manifest.UpdaterDownloadUrl) || string.IsNullOrWhiteSpace(manifest.UpdaterSha256))
		{
			throw new InvalidOperationException("Version file requires updater update but is missing updaterDownloadUrl or updaterSha256.");
		}

		string packageDirectory = Path.Combine(Path.GetTempPath(), "HitEducation", "updates");
		Directory.CreateDirectory(packageDirectory);
		string packagePath = Path.Combine(packageDirectory, "HitEducation.Updater-" + manifest.Version + ".zip");
		await DownloadFileAsync(manifest.UpdaterDownloadUrl, packagePath);
		VerifySha256(packagePath, manifest.UpdaterSha256, "updater package");

		string extractDirectory = Path.Combine(Path.GetTempPath(), "HitEducation", "updater-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(extractDirectory);
		Log("Extracting updater package to: " + extractDirectory);
		ZipFile.ExtractToDirectory(packagePath, extractDirectory, overwriteFiles: true);
		ValidateUpdaterPackage(extractDirectory);

		string tempUpdaterPath = Path.Combine(extractDirectory, "HitEducation.Updater.exe");
		string arguments = BuildArgumentString(originalArgs,
			"--skip-updater-self-update",
			"--complete-updater-self-update",
			"--old-updater-pid", Environment.ProcessId.ToString(),
			"--updater-source", extractDirectory,
			"--updater-package", packagePath);

		Log("Starting temporary updater for self replacement: " + tempUpdaterPath);
		Process.Start(new ProcessStartInfo(tempUpdaterPath)
		{
			UseShellExecute = true,
			WorkingDirectory = extractDirectory,
			Arguments = arguments
		});
	}

	private static void CompleteUpdaterSelfUpdate(Arguments options)
	{
		if (string.IsNullOrWhiteSpace(options.TargetDirectory) || string.IsNullOrWhiteSpace(options.UpdaterSourceDirectory))
		{
			throw new InvalidOperationException("Missing updater self-update completion arguments.");
		}

		if (options.OldUpdaterProcessId.HasValue)
		{
			WaitForProcessExit(options.OldUpdaterProcessId.Value, "old updater");
		}

		Log("Copying updater files from temporary updater: " + options.UpdaterSourceDirectory);
		foreach (string file in Directory.EnumerateFiles(options.UpdaterSourceDirectory, "HitEducation.Updater*", SearchOption.TopDirectoryOnly))
		{
			string destination = Path.Combine(options.TargetDirectory, Path.GetFileName(file));
			File.Copy(file, destination, overwrite: true);
		}
		Log("Updater files copied to target directory.");

		if (!string.IsNullOrWhiteSpace(options.LegacyUpdaterPackagePath))
		{
			TryDelete(options.LegacyUpdaterPackagePath, recursive: false);
		}

	}

	private static async Task DownloadFileAsync(string url, string destinationPath)
	{
		Log("Downloading: " + url);
		if (File.Exists(url))
		{
			File.Copy(url, destinationPath, overwrite: true);
			Log("Local file copied: " + destinationPath);
			return;
		}
		using HttpResponseMessage response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
		response.EnsureSuccessStatusCode();
		await using Stream source = await response.Content.ReadAsStreamAsync();
		await using FileStream destination = File.Create(destinationPath);
		await source.CopyToAsync(destination);
		Log("Download completed: " + destinationPath);
	}

	private static void VerifySha256(string filePath, string expectedHash, string label)
	{
		using FileStream stream = File.OpenRead(filePath);
		string actualHash = Convert.ToHexString(SHA256.HashData(stream));
		Log($"SHA256 {label}: expected={expectedHash}; actual={actualHash}");
		if (!string.Equals(actualHash, expectedHash.Trim(), StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("SHA256 verification failed for " + label + ".");
		}
	}

	private static void ValidateAppPackage(string extractDirectory, Version currentVersion)
	{
		string appExe = Path.Combine(extractDirectory, AppExeName);
		string appDll = Path.Combine(extractDirectory, AppDllName);
		if (!File.Exists(appExe) || !File.Exists(appDll))
		{
			throw new InvalidOperationException("App package is missing required files.");
		}

		Version packageVersion = ReadFileVersion(appDll);
		Log($"Extracted app version: {packageVersion}");
		if (packageVersion <= currentVersion)
		{
			throw new InvalidOperationException("Extracted app version is not newer than current version.");
		}
	}

	private static void ValidateUpdaterPackage(string extractDirectory)
	{
		if (!File.Exists(Path.Combine(extractDirectory, "HitEducation.Updater.exe"))
			|| !File.Exists(Path.Combine(extractDirectory, "HitEducation.Updater.dll")))
		{
			throw new InvalidOperationException("Updater package is missing required files.");
		}
	}

	private static void ValidateLegacyAppPackage(string extractDirectory)
	{
		if (!File.Exists(Path.Combine(extractDirectory, AppExeName))
			|| !File.Exists(Path.Combine(extractDirectory, AppDllName)))
		{
			throw new InvalidOperationException("Legacy app package is missing required files.");
		}
	}

	private static Version ReadFileVersion(string path)
	{
		FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
		string value = info.ProductVersion ?? info.FileVersion ?? "0.0.0";
		return ParseVersion(value);
	}

	private static void StopAppProcess(int processId)
	{
		try
		{
			using Process process = Process.GetProcessById(processId);
			Log("Waiting for app process to exit: " + processId);
			if (!process.WaitForExit(5000))
			{
				Log("Stopping app process: " + processId);
				process.Kill(entireProcessTree: true);
				process.WaitForExit(10000);
			}
			Log("App process stopped or exited: " + processId);
		}
		catch (ArgumentException)
		{
			Log("App process already exited: " + processId);
		}
		Thread.Sleep(500);
	}

	private static void WaitForProcessExit(int processId, string label)
	{
		try
		{
			using Process process = Process.GetProcessById(processId);
			Log("Waiting for " + label + " process to exit: " + processId);
			process.WaitForExit(30000);
		}
		catch (ArgumentException)
		{
			Log(label + " process already exited: " + processId);
		}
		Thread.Sleep(500);
	}

	private static void CopyAppFiles(string sourceDirectory, string targetDirectory)
	{
		Log("Copying app files to: " + targetDirectory);
		Directory.CreateDirectory(targetDirectory);
		foreach (string directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
		{
			Directory.CreateDirectory(Path.Combine(targetDirectory, Path.GetRelativePath(sourceDirectory, directory)));
		}
		foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
		{
			string name = Path.GetFileName(file);
			if (name.StartsWith("HitEducation.Updater", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			string destination = Path.Combine(targetDirectory, Path.GetRelativePath(sourceDirectory, file));
			Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
			File.Copy(file, destination, overwrite: true);
		}
		Log("App files copied.");
	}

	private static void RestartApp(string appPath, string targetDirectory)
	{
		Log("Restarting app: " + appPath);
		Process.Start(new ProcessStartInfo(appPath)
		{
			UseShellExecute = true,
			WorkingDirectory = targetDirectory,
			Arguments = "--skip-auto-update-once"
		});
	}

	private static Version ParseVersion(string value)
	{
		string version = value.Split('+', 2)[0].Trim();
		if (Version.TryParse(version, out Version? parsed))
		{
			return parsed;
		}
		throw new FormatException("Invalid version: " + value);
	}

	private static string BuildArgumentString(string[] originalArgs, params string[] extraArgs)
	{
		List<string> values = new List<string>(originalArgs);
		foreach (string extraArg in extraArgs)
		{
			values.Add(extraArg);
		}
		return string.Join(" ", values.ConvertAll(QuoteArgument));
	}

	private static string QuoteArgument(string value)
	{
		if (value.Length == 0 || value.Contains(' ') || value.Contains('"'))
		{
			return "\"" + value.Replace("\"", "\\\"") + "\"";
		}
		return value;
	}

	private static string QuotePs(string value)
	{
		return "'" + value.Replace("'", "''") + "'";
	}

	private static void TryDelete(string path, bool recursive)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive);
			}
			else if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception ex)
		{
			Log("Delete failed: " + path + "; " + ex.Message);
		}
	}

	private static void Log(string message)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
			File.AppendAllText(LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine);
		}
		catch
		{
		}
	}
}

internal sealed class Arguments
{
	public string? TargetDirectory { get; private set; }

	public string? AppPath { get; private set; }

	public int? ProcessId { get; private set; }

	public string? CurrentVersion { get; private set; }

	public string? VersionUrl { get; private set; }

	public bool Auto { get; private set; }

	public bool SkipUpdaterSelfUpdate { get; private set; }

	public string? LegacyPackagePath { get; private set; }

	public string? LegacyRestartPath { get; private set; }

	public string? LegacyUpdaterPackagePath { get; private set; }

	public bool CompleteUpdaterSelfUpdate { get; private set; }

	public int? OldUpdaterProcessId { get; private set; }

	public string? UpdaterSourceDirectory { get; private set; }

	public static Arguments Parse(string[] args)
	{
		Arguments result = new Arguments();
		for (int i = 0; i < args.Length; i++)
		{
			switch (args[i].ToLowerInvariant())
			{
				case "--target" when i + 1 < args.Length:
					result.TargetDirectory = args[++i];
					break;
				case "--package" when i + 1 < args.Length:
					result.LegacyPackagePath = args[++i];
					break;
				case "--restart" when i + 1 < args.Length:
					result.LegacyRestartPath = args[++i];
					break;
				case "--updater-package" when i + 1 < args.Length:
					result.LegacyUpdaterPackagePath = args[++i];
					break;
				case "--complete-updater-self-update":
					result.CompleteUpdaterSelfUpdate = true;
					break;
				case "--old-updater-pid" when i + 1 < args.Length:
					if (int.TryParse(args[++i], out int oldUpdaterProcessId))
					{
						result.OldUpdaterProcessId = oldUpdaterProcessId;
					}
					break;
				case "--updater-source" when i + 1 < args.Length:
					result.UpdaterSourceDirectory = args[++i];
					break;
				case "--app" when i + 1 < args.Length:
					result.AppPath = args[++i];
					break;
				case "--pid" when i + 1 < args.Length:
					if (int.TryParse(args[++i], out int processId))
					{
						result.ProcessId = processId;
					}
					break;
				case "--current-version" when i + 1 < args.Length:
					result.CurrentVersion = args[++i];
					break;
				case "--version-url" when i + 1 < args.Length:
					result.VersionUrl = args[++i];
					break;
				case "--auto":
					result.Auto = true;
					break;
				case "--skip-updater-self-update":
					result.SkipUpdaterSelfUpdate = true;
					break;
			}
		}
		return result;
	}
}

internal sealed class UpdateManifest
{
	[JsonPropertyName("version")]
	public string Version { get; set; } = string.Empty;

	[JsonPropertyName("downloadUrl")]
	public string DownloadUrl { get; set; } = string.Empty;

	[JsonPropertyName("sha256")]
	public string Sha256 { get; set; } = string.Empty;

	[JsonPropertyName("updateUpdater")]
	public bool UpdateUpdater { get; set; }

	[JsonPropertyName("updaterDownloadUrl")]
	public string UpdaterDownloadUrl { get; set; } = string.Empty;

	[JsonPropertyName("updaterSha256")]
	public string UpdaterSha256 { get; set; } = string.Empty;
}
