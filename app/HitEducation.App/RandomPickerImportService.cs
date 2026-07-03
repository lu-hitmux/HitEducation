using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HitEducation.App;

public sealed class RandomPickerImportResult
{
	public required RandomPickerRoster Roster { get; init; }

	public required List<RandomPickerMember> Members { get; init; }
}

public static class RandomPickerImportService
{
	private static readonly object ActiveImportLock = new();
	private static readonly HashSet<string> ActiveImportPaths = new(StringComparer.OrdinalIgnoreCase);

	public static bool TryBeginImport(string path, out string normalizedPath)
	{
		normalizedPath = Path.GetFullPath(path);
		lock (ActiveImportLock)
		{
			return ActiveImportPaths.Add(normalizedPath);
		}
	}

	public static void EndImport(string path)
	{
		string normalizedPath = Path.GetFullPath(path);
		lock (ActiveImportLock)
		{
			ActiveImportPaths.Remove(normalizedPath);
		}
	}

	public static Task<RandomPickerImportResult> ImportAsync(AppStorage storage, string path, IProgress<RandomPickerImportProgress>? progress = null)
	{
		var completion = new TaskCompletionSource<RandomPickerImportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		string normalizedPath = Path.GetFullPath(path);
		string dataDirectory = storage.DataDirectory;

		var thread = new Thread(() =>
		{
			try
			{
				completion.SetResult(ImportCore(dataDirectory, normalizedPath, progress));
			}
			catch (Exception ex)
			{
				completion.SetException(ex);
			}
		});
		thread.IsBackground = true;
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();

		return completion.Task;
	}

	private static RandomPickerImportResult ImportCore(string dataDirectory, string path, IProgress<RandomPickerImportProgress>? progress)
	{
		Report(progress, 5);
		string rosterId = Guid.NewGuid().ToString("N");
		string rosterDirectory = Path.Combine(dataDirectory, "rosters", rosterId);
		Directory.CreateDirectory(rosterDirectory);
		try
		{
			string savedPath = Path.Combine(rosterDirectory, Path.GetFileName(path));
			File.Copy(path, savedPath, overwrite: true);
			Report(progress, 15);

			var members = RandomPickerImporter.Import(savedPath, Path.Combine(rosterDirectory, "thumbnails"), progress);
			Report(progress, 95);
			if (members.Count == 0)
			{
				throw new InvalidOperationException(Localizer.Text((string?)null, "RosterImportEmpty"));
			}

			return new RandomPickerImportResult
			{
				Roster = new RandomPickerRoster
				{
					Id = rosterId,
					Name = Path.GetFileNameWithoutExtension(path),
					SourcePath = savedPath,
					ImportedAt = DateTime.Now,
					Members = members
				},
				Members = members
			};
		}
		catch
		{
			try
			{
				if (Directory.Exists(rosterDirectory))
				{
					Directory.Delete(rosterDirectory, recursive: true);
				}
			}
			catch
			{
			}

			throw;
		}
	}

	private static void Report(IProgress<RandomPickerImportProgress>? progress, int percent)
	{
		progress?.Report(new RandomPickerImportProgress { Percent = Math.Clamp(percent, 0, 100) });
	}
}
