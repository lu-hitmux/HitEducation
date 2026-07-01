using System.Collections.Generic;

namespace HitEducation.App;

public sealed class SettingsExport
{
	public AppSettings Settings { get; set; } = new AppSettings();

	public List<Subject> Subjects { get; set; } = new List<Subject>();
}
