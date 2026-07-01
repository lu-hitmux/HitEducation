using System;

namespace HitEducation.App;

public sealed class AddHomeworkDraft
{
	public string? SubjectId { get; set; }

	public string? Content { get; set; }

	public bool NoSubmissionRequired { get; set; }

	public DateTime? DueDate { get; set; }

	public string? DueTime { get; set; }

	public string? AppliedTemplate { get; set; }
}
