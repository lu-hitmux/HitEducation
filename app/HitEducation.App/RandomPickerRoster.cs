using System;
using System.Collections.Generic;

namespace HitEducation.App;

public sealed class RandomPickerRoster
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");

	public string Name { get; set; } = string.Empty;

	public string SourcePath { get; set; } = string.Empty;

	public DateTime ImportedAt { get; set; } = DateTime.Now;

	public List<RandomPickerMember> Members { get; set; } = [];
}
