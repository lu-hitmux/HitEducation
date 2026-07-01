using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace HitEducation.App;

public static class UpdateChecker
{
	public const string VersionUrl = "https://pack.hitmc.net/HitPrograms/HitEducation/version.json";

	private static readonly HttpClient Client = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(10.0)
	};

	public static async Task<UpdateCheckResult> CheckAsync()
	{
		using HttpResponseMessage response = await Client.GetAsync(VersionUrl);
		response.EnsureSuccessStatusCode();
		await using Stream stream = await response.Content.ReadAsStreamAsync();
		UpdateManifest? manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		});
		if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version))
		{
			throw new InvalidOperationException("Version file is missing the version field.");
		}
		Version current = CurrentVersion();
		Version latest = ParseVersion(manifest.Version);
		return new UpdateCheckResult(current, latest, latest > current, manifest);
	}

	public static Version CurrentVersion()
	{
		string? value = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
		if (string.IsNullOrWhiteSpace(value))
		{
			value = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
		}
		return ParseVersion(value ?? "0.0.0");
	}

	public static void OpenDownload(UpdateManifest manifest)
	{
		if (string.IsNullOrWhiteSpace(manifest.DownloadUrl))
		{
			return;
		}
		Process.Start(new ProcessStartInfo(manifest.DownloadUrl)
		{
			UseShellExecute = true
		});
	}

	public static bool TryStartUpdater(bool auto)
	{
		try
		{
			string? executablePath = Environment.ProcessPath;
			if (string.IsNullOrWhiteSpace(executablePath))
			{
				throw new InvalidOperationException("Cannot locate current executable.");
			}
			string targetDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string updaterPath = Path.Combine(targetDirectory, "HitEducation.Updater.exe");
			if (!File.Exists(updaterPath))
			{
				throw new FileNotFoundException("Updater executable was not found.", updaterPath);
			}

			ProcessStartInfo startInfo = new ProcessStartInfo(updaterPath)
			{
				UseShellExecute = true,
				WorkingDirectory = targetDirectory
			};
			startInfo.ArgumentList.Add("--target");
			startInfo.ArgumentList.Add(targetDirectory);
			startInfo.ArgumentList.Add("--app");
			startInfo.ArgumentList.Add(executablePath);
			startInfo.ArgumentList.Add("--pid");
			startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
			startInfo.ArgumentList.Add("--current-version");
			startInfo.ArgumentList.Add(CurrentVersion().ToString());
			startInfo.ArgumentList.Add("--version-url");
			startInfo.ArgumentList.Add(VersionUrl);
			if (auto)
			{
				startInfo.ArgumentList.Add("--auto");
			}
			Process.Start(startInfo);
			AppLogger.Info("Updater started in " + (auto ? "auto" : "manual") + " mode.");
			return true;
		}
		catch (Exception ex)
		{
			AppLogger.Error("Failed to start updater.", ex);
			return false;
		}
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
}

public sealed class UpdateCheckResult
{
	public UpdateCheckResult(Version currentVersion, Version latestVersion, bool hasUpdate, UpdateManifest manifest)
	{
		CurrentVersion = currentVersion;
		LatestVersion = latestVersion;
		HasUpdate = hasUpdate;
		Manifest = manifest;
	}

	public Version CurrentVersion { get; }

	public Version LatestVersion { get; }

	public bool HasUpdate { get; }

	public UpdateManifest Manifest { get; }
}

public sealed class UpdateManifest
{
	[JsonPropertyName("version")]
	public string Version { get; set; } = string.Empty;

	[JsonPropertyName("title")]
	public string Title { get; set; } = string.Empty;

	[JsonPropertyName("downloadUrl")]
	public string DownloadUrl { get; set; } = string.Empty;

	[JsonPropertyName("sha256")]
	public string Sha256 { get; set; } = string.Empty;

	[JsonPropertyName("publishedAt")]
	public string PublishedAt { get; set; } = string.Empty;

	[JsonPropertyName("updateUpdater")]
	public bool UpdateUpdater { get; set; }

	[JsonPropertyName("updaterDownloadUrl")]
	public string UpdaterDownloadUrl { get; set; } = string.Empty;

	[JsonPropertyName("updaterSha256")]
	public string UpdaterSha256 { get; set; } = string.Empty;

	[JsonPropertyName("required")]
	public bool Required { get; set; }

	[JsonPropertyName("notes")]
	public List<string> Notes { get; set; } = new List<string>();
}
