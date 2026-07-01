using System;
using System.Collections.Generic;
using System.Linq;
using System.Media;
using System.Threading.Tasks;
using System.Windows;

namespace HitEducation.App;

public partial class MainWindow
{
	private async Task ArchiveExpiredAsync()
	{
		var expiredIds = ExpiredHomeworkIds().ToList();
		if (expiredIds.Count == 0)
		{
			return;
		}

		var previousCardPositions = await AnimateHomeworkRemovalAsync(expiredIds);
		if (storage.ArchiveExpired())
		{
			await storage.SaveAsync();
			RemoveHomeworkCardsFromView(expiredIds, previousCardPositions, refreshAfterReflow: true);
		}
	}

	private IEnumerable<string> ExpiredHomeworkIds()
	{
		var archiveBefore = DateTime.Now.AddMinutes(-5.0);
		return storage.Data.Homeworks
			.Where(homework => homework.Status == "active" && !homework.NoSubmissionRequired && homework.DueAt <= archiveBefore)
			.Select(homework => homework.Id);
	}

	private async Task UpdateHomeworkTimeStateAsync()
	{
		var changed = false;
		var now = DateTime.Now;
		Dictionary<string, Point>? previousCardPositions = null;
		UpdateClassPeriodState(now);

		foreach (var homework in storage.Data.Homeworks.Where(homework => homework.Status == "active" && !homework.NoSubmissionRequired))
		{
			var remindAt = homework.DueAt.AddMinutes(-storage.Data.Settings.RemindMinutesBefore);
			if ((homework.RemindedAt is null || homework.RemindedAt < remindAt) && now >= remindAt && now < homework.DueAt)
			{
				homework.RemindedAt = now;
				changed = true;
				changed |= QueueOrShowReminder(homework, isDue: false, remindAt);
			}

			if (homework.DueRemindedAt is null && now >= homework.DueAt)
			{
				homework.DueRemindedAt = now;
				changed = true;
				changed |= QueueOrShowReminder(homework, isDue: true, homework.DueAt);
			}
		}

		var expiredIds = ExpiredHomeworkIds().ToList();
		if (expiredIds.Count > 0)
		{
			previousCardPositions = await AnimateHomeworkRemovalAsync(expiredIds);
			if (storage.ArchiveExpired())
			{
				changed = true;
			}
		}

		var anyOverdue = storage.Data.Homeworks.Any(homework => homework.Status == "active" && !homework.NoSubmissionRequired && homework.DueAt <= now);
		if (anyOverdue != wasAnyHomeworkOverdue)
		{
			wasAnyHomeworkOverdue = anyOverdue;
			RenderHomeworks();
		}

		if (changed)
		{
			await storage.SaveAsync();
			if (previousCardPositions != null)
			{
				RemoveHomeworkCardsFromView(expiredIds, previousCardPositions, refreshAfterReflow: true);
			}
			else
			{
				RenderHomeworks();
			}
		}
	}

	private bool QueueOrShowReminder(Homework homework, bool isDue, DateTime triggeredAt)
	{
		if (IsInClassPeriod(DateTime.Now))
		{
			if (isDue)
			{
				homework.DeferredDueReminderQueuedAt ??= triggeredAt;
			}
			else
			{
				homework.DeferredReminderQueuedAt = triggeredAt;
			}

			AppLogger.Info($"Reminder deferred during class period: {homework.Id}, due={isDue}");
			return true;
		}

		ShowReminder(homework, isDue);
		return false;
	}

	private void ShowReminder(Homework homework, bool isDue)
	{
		SystemSounds.Exclamation.Play();
		ShowNotification(homework, isDue);
	}

	private void UpdateClassPeriodState(DateTime now)
	{
		if (IsInClassPeriod(now))
		{
			classPeriodEndedAt = null;
			return;
		}

		classPeriodEndedAt ??= now;
		if ((now - classPeriodEndedAt.Value).TotalMinutes < 1.0)
		{
			return;
		}

		EnqueueDeferredReminders();
	}

	private void EnqueueDeferredReminders()
	{
		if (deferredReminders.Count > 0)
		{
			return;
		}

		var reminders = storage.Data.Homeworks
			.Where(homework => homework.Status == "active")
			.SelectMany(homework => DeferredReminder.FromHomework(homework))
			.OrderBy(reminder => reminder.QueuedAt)
			.ThenBy(reminder => reminder.Homework.DueAt)
			.ToList();

		foreach (var reminder in reminders)
		{
			deferredReminders.Enqueue(reminder);
		}
	}

	private async void ShowNextDeferredReminder()
	{
		var now = DateTime.Now;
		UpdateClassPeriodState(now);
		if (IsInClassPeriod(now) || classPeriodEndedAt is null)
		{
			return;
		}

		if ((now - classPeriodEndedAt.Value).TotalMinutes < 1.0 || deferredReminders.Count == 0)
		{
			return;
		}

		var changed = false;
		while (deferredReminders.Count > 0)
		{
			var reminder = deferredReminders.Dequeue();
			if (reminder.Homework.Status != "active")
			{
				continue;
			}

			if (reminder.IsDue)
			{
				if (reminder.Homework.DeferredDueReminderQueuedAt is null)
				{
					continue;
				}

				reminder.Homework.DeferredDueReminderQueuedAt = null;
			}
			else
			{
				if (reminder.Homework.DeferredReminderQueuedAt is null)
				{
					continue;
				}

				reminder.Homework.DeferredReminderQueuedAt = null;
			}

			changed = true;
			ShowReminder(reminder.Homework, reminder.IsDue);
			break;
		}

		if (changed)
		{
			await storage.SaveAsync();
		}
	}

	private void ShowNotification(Homework homework, bool isDue)
	{
		var message = BuildReminderMessage(homework, isDue);
		notificationWindows.RemoveAll(window => !window.IsVisible);
		var notificationWindow = new NotificationWindow(
			message.Title,
			message.Detail,
			Localizer.Text(storage, "NotificationLabel"),
			storage.Data.Settings.NotificationDurationSeconds,
			notificationWindows.Count);
		notificationWindows.Add(notificationWindow);
		notificationWindow.Closed += (_, _) =>
		{
			notificationWindows.Remove(notificationWindow);
			RepositionNotificationWindows();
		};
		notificationWindow.Show();
		AppLogger.Info($"Notification shown for homework: {homework.Id}");
	}

	private void RepositionNotificationWindows()
	{
		notificationWindows.RemoveAll(window => !window.IsVisible);
		for (var i = 0; i < notificationWindows.Count; i++)
		{
			notificationWindows[i].MoveToStackIndex(i);
		}
	}

	private (string Title, string Detail) BuildReminderMessage(Homework homework, bool isDue)
	{
		var template = isDue ? storage.Data.Settings.DueReminderTemplate : storage.Data.Settings.UpcomingReminderTemplate;
		if (string.IsNullOrWhiteSpace(template))
		{
			template = Localizer.Text(storage, isDue ? "DueTemplateDefault" : "UpcomingTemplateDefault");
		}

		var subjectName = Localizer.SubjectName(storage, homework.SubjectId, homework.SubjectName);
		var text = template
			.Replace("{Subject}", subjectName)
			.Replace("{Content}", homework.Content)
			.Replace("{Time}", homework.DueAt.ToString("HH:mm"))
			.Replace("{Date}", homework.DueAt.ToString("MM-dd"))
			.Replace("{MinutesBefore}", storage.Data.Settings.RemindMinutesBefore.ToString())
			.Replace("{学科}", subjectName)
			.Replace("{内容}", homework.Content)
			.Replace("{时间}", homework.DueAt.ToString("HH:mm"))
			.Replace("{日期}", homework.DueAt.ToString("MM-dd"))
			.Replace("{提前分钟}", storage.Data.Settings.RemindMinutesBefore.ToString());

		var lines = text.Replace("\r\n", "\n").Split('\n', 2);
		var title = string.IsNullOrWhiteSpace(lines[0])
			? Localizer.Text(storage, isDue ? "ReminderDueDefault" : "ReminderUpcomingDefault")
			: lines[0].Trim();
		var detail = lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1])
			? lines[1].Trim()
			: Localizer.Format(storage, "ReminderDetailDefault", homework.DueAt.ToString("HH:mm"), homework.Content);

		return (title, detail);
	}

	private bool IsInClassPeriod(DateTime now)
	{
		if (storage.Data.Settings.ClassPeriodDays == "weekdays"
			&& now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
		{
			return false;
		}

		var time = now.TimeOfDay;
		foreach (var period in storage.Data.Settings.ClassPeriods)
		{
			if (!TimeSpan.TryParse(period.Start, out var start) || !TimeSpan.TryParse(period.End, out var end))
			{
				continue;
			}

			if (end >= start && time >= start && time <= end)
			{
				return true;
			}

			if (end < start && (time >= start || time <= end))
			{
				return true;
			}
		}

		return false;
	}
}
