using System;

namespace HitEducation.App;

public sealed class Homework
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");

	public string SubjectId { get; set; } = "math";

	public string SubjectName { get; set; } = "数学";

	public string Content { get; set; } = "";

	public DateTime DueAt { get; set; } = DateTime.Now.AddHours(1);

	public bool NoSubmissionRequired { get; set; }

	public DateTime CreatedAt { get; set; } = DateTime.Now;

	public string Status { get; set; } = "active";

	public DateTime? CompletedAt { get; set; }

	public DateTime? RemindedAt { get; set; }

	public DateTime? DueRemindedAt { get; set; }

	public DateTime? DeferredReminderQueuedAt { get; set; }

	public DateTime? DeferredDueReminderQueuedAt { get; set; }

	public DateTime? ArchivedAt { get; set; }

	public string? ArchiveReason { get; set; }
}
