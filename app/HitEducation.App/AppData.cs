using System;
using System.Collections.Generic;

namespace HitEducation.App;

public sealed class AppData
{
	public AppSettings Settings { get; set; } = new();

	public WindowPlacement Window { get; set; } = new();

	public List<Subject> Subjects { get; set; } = DefaultSubjects();

	public List<Homework> Homeworks { get; set; } = [];

	public RandomPickerState RandomPicker { get; set; } = new();

	public static List<Subject> DefaultSubjects() =>
	[
		new() { Id = "chinese", Name = "语文", Color = "#BA5A31", SortOrder = 1 },
		new() { Id = "math", Name = "数学", Color = "#3B82F6", SortOrder = 2 },
		new() { Id = "english", Name = "英语", Color = "#16A34A", SortOrder = 3 },
		new() { Id = "physics", Name = "物理", Color = "#7C3AED", SortOrder = 4 },
		new() { Id = "chemistry", Name = "化学", Color = "#0891B2", SortOrder = 5 },
		new() { Id = "biology", Name = "生物", Color = "#65A30D", SortOrder = 6 }
	];
}
