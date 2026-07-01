using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace HitEducation.App;

public sealed class Subject
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");

	public string Name { get; set; } = "学科";

	public string Color { get; set; } = "#64748B";

	public List<string> Templates { get; set; } = ["导学单", "课时作业", "练习册", "背诵默写", "预习", "复习"];

	public Dictionary<string, int> TemplateCounters { get; set; } = new() { ["课时作业"] = 1 };

	public int SortOrder { get; set; }

	[JsonIgnore]
	public string DisplayName
	{
		get
		{
			try
			{
				return Localizer.SubjectName(App.Storage, this);
			}
			catch
			{
				return Name;
			}
		}
	}

	public Color GetColor()
	{
		try
		{
			return (Color)ColorConverter.ConvertFromString(Color)!;
		}
		catch
		{
			return System.Windows.Media.Color.FromRgb(100, 116, 139);
		}
	}
}
