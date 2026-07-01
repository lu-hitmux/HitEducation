using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HitEducation.App;

public sealed class AppStorage
{
	private readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };

	private readonly SemaphoreSlim saveLock = new(1, 1);

	public string DataDirectory { get; }

	public string DataFile { get; }

	public string BackupDirectory { get; }

	public AppData Data { get; private set; } = new();

	public AppStorage()
	{
		DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HitEducation");
		DataFile = Path.Combine(DataDirectory, "data.json");
		BackupDirectory = Path.Combine(DataDirectory, "backups");
	}

	public async Task LoadAsync()
	{
		AppLogger.Info("Loading data");
		Directory.CreateDirectory(DataDirectory);
		Directory.CreateDirectory(BackupDirectory);
		if (!File.Exists(DataFile))
		{
			Data = new AppData();
			await SaveAsync(createBackup: false);
			AppLogger.Info("Created initial data file");
			return;
		}
		try
		{
			Data = (await ReadDataAsync(DataFile)) ?? new AppData();
			EnsureDefaults();
			AppLogger.Info("Data loaded");
		}
		catch (Exception exception)
		{
			AppLogger.Error("Data file read failed, trying latest backup", exception);
			if (!await RestoreLatestBackupAsync())
			{
				Data = new AppData();
				await SaveAsync(createBackup: false);
				AppLogger.Info("Backup restore failed; reset data file");
			}
		}
	}

	public async Task SaveAsync(bool createBackup = true)
	{
		await saveLock.WaitAsync();
		try
		{
			Directory.CreateDirectory(DataDirectory);
			Directory.CreateDirectory(BackupDirectory);
			if (createBackup && File.Exists(DataFile))
			{
				CreateBackup();
			}
			var tempFile = Path.Combine(DataDirectory, $"data.{Guid.NewGuid():N}.tmp");
			var json = JsonSerializer.Serialize(Data, jsonOptions);
			await File.WriteAllTextAsync(tempFile, json);
			File.Copy(tempFile, DataFile, overwrite: true);
			File.Delete(tempFile);
			CleanupBackups();
		}
		finally
		{
			saveLock.Release();
		}
	}

	public IEnumerable<Homework> GetActiveHomeworks()
	{
		var now = DateTime.Now;
		return Data.Homeworks
			.Where(h => h.Status == "active")
			.OrderBy(h => h.NoSubmissionRequired)
			.ThenByDescending(h => !h.NoSubmissionRequired && h.DueAt > now && h.DueAt <= now.AddMinutes(Data.Settings.RemindMinutesBefore))
			.ThenBy(h => h.DueAt);
	}

	public bool ArchiveExpired()
	{
		var changed = false;
		var archiveBefore = DateTime.Now.AddMinutes(-5);
		foreach (var homework in Data.Homeworks.Where(h => h.Status == "active" && !h.NoSubmissionRequired && h.DueAt <= archiveBefore))
		{
			homework.Status = "archived";
			homework.ArchivedAt = DateTime.Now;
			homework.ArchiveReason = "expired";
			changed = true;
		}
		return changed;
	}

	public void CompleteHomework(string id)
	{
		var homework = Data.Homeworks.FirstOrDefault(h => h.Id == id);
		if (homework is null) return;

		homework.Status = "archived";
		homework.CompletedAt = DateTime.Now;
		homework.ArchivedAt = DateTime.Now;
		homework.ArchiveReason = "completed";
	}

	public void DeleteHomework(string id) => Data.Homeworks.RemoveAll(h => h.Id == id);

	public bool IsDuplicate(string? editingId, string subjectId, string content, DateTime dueAt, bool noSubmissionRequired)
	{
		return Data.Homeworks.Any(h => h.Id != editingId
			&& h.Status == "active"
			&& h.SubjectId == subjectId
			&& string.Equals(h.Content.Trim(), content.Trim(), StringComparison.CurrentCultureIgnoreCase)
			&& h.NoSubmissionRequired == noSubmissionRequired
			&& h.DueAt == dueAt);
	}

	public async Task<bool> RestoreLatestBackupAsync()
	{
		var backup = Directory.Exists(BackupDirectory)
			? Directory.GetFiles(BackupDirectory, "data-*.json").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
			: null;
		if (backup is null) return false;

		try
		{
			Data = (await ReadDataAsync(backup)) ?? new AppData();
			File.Copy(backup, DataFile, overwrite: true);
			EnsureDefaults();
			AppLogger.Info($"Restored backup: {backup}");
			return true;
		}
		catch (Exception exception)
		{
			AppLogger.Error($"Backup restore failed: {backup}", exception);
			return false;
		}
	}

	public async Task ExportSettingsAsync(string path)
	{
		var export = new SettingsExport
		{
			Settings = Data.Settings,
			Subjects = Data.Subjects
		};
		var json = JsonSerializer.Serialize(export, jsonOptions);
		await File.WriteAllTextAsync(path, json);
		AppLogger.Info($"Settings exported: {path}");
	}

	public async Task ImportSettingsAsync(string path)
	{
		await using var stream = File.OpenRead(path);
		var export = await JsonSerializer.DeserializeAsync<SettingsExport>(stream, jsonOptions)
			?? throw new InvalidOperationException("设置文件无法读取。");
		if (export.Settings is null) throw new InvalidOperationException("设置文件缺少设置内容。");

		Data.Settings = export.Settings;
		if (export.Subjects is { Count: > 0 }) Data.Subjects = export.Subjects;
		EnsureDefaults();
		await SaveAsync(createBackup: true);
		AppLogger.Info($"Settings imported: {path}");
	}

	private async Task<AppData?> ReadDataAsync(string path)
	{
		await using var stream = File.OpenRead(path);
		return await JsonSerializer.DeserializeAsync<AppData>(stream, jsonOptions);
	}

	private void CreateBackup()
	{
		var name = $"data-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json";
		File.Copy(DataFile, Path.Combine(BackupDirectory, name), overwrite: false);
	}

	private void CleanupBackups()
	{
		var limit = Math.Max(1, Data.Settings.BackupLimit);
		var backups = Directory.GetFiles(BackupDirectory, "data-*.json").OrderByDescending(File.GetLastWriteTimeUtc).Skip(limit);
		foreach (var backup in backups) File.Delete(backup);
	}

	private void EnsureDefaults()
	{
		Data.Settings ??= new AppSettings();
		Data.Settings.Language = Localizer.Normalize(Data.Settings.Language);
		Data.Window ??= new WindowPlacement();
		if (Data.Subjects.Count == 0) Data.Subjects = AppData.DefaultSubjects();
		Data.Homeworks ??= [];
		Data.Settings.ClassPeriods ??= [];
		Data.Settings.FullscreenProcessNames ??= [];
		Data.Settings.FullscreenBlockedProcessNames ??= [];
		Data.Settings.BackgroundImagePath ??= string.Empty;
		if (Data.Settings.FontOpacity <= 0) Data.Settings.FontOpacity = 1;
		if (string.IsNullOrWhiteSpace(Data.Settings.UpcomingReminderTemplate)) Data.Settings.UpcomingReminderTemplate = "{学科}作业即将上交\n{时间} 前：{内容}";
		if (string.IsNullOrWhiteSpace(Data.Settings.DueReminderTemplate)) Data.Settings.DueReminderTemplate = "{学科}作业到时间了\n{时间} 前：{内容}";
	}
}
