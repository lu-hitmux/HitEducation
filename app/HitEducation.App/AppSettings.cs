using System.Collections.Generic;

namespace HitEducation.App;

public sealed class AppSettings
{
	public string Language { get; set; } = "zh-CN";

	public bool AlwaysOnTop { get; set; } = true;

	public bool AutoStart { get; set; } = true;

	public bool AutoUpdate { get; set; } = true;

	public double Opacity { get; set; } = 0.75;

	public double FontOpacity { get; set; } = 1;

	public bool BackgroundImageEnabled { get; set; }

	public string BackgroundImagePath { get; set; } = string.Empty;

	public double FontSize { get; set; } = 24;

	public double AutoScrollSpeed { get; set; } = 0.85;

	public int AutoScrollIdleSeconds { get; set; } = 20;

	public int AutoScrollEdgePauseSeconds { get; set; } = 3;

	public bool LockWindow { get; set; }

	public int RemindMinutesBefore { get; set; } = 5;

	public string LunchDismissalTime { get; set; } = "12:10";

	public string EveningDismissalTime { get; set; } = "22:00";

	public string MorningSubmitTime { get; set; } = "07:45";

	public List<QuickTimeRule> QuickTimeRules { get; set; } =
	[
		new() { Name = "今天中午", Rule = "today 12:10" },
		new() { Name = "今天晚上", Rule = "today 22:00" },
		new() { Name = "明早上交", Rule = "tomorrow 07:45" },
		new() { Name = "明天中午", Rule = "tomorrow 12:10" },
		new() { Name = "明天晚上", Rule = "tomorrow 22:00" },
		new() { Name = "1 小时后", Rule = "+1hour" },
		new() { Name = "30 分钟后", Rule = "+30min" }
	];

	public string NotificationPosition { get; set; } = "top";

	public int NotificationDurationSeconds { get; set; } = 10;

	public string UpcomingReminderTemplate { get; set; } = "{学科}作业即将上交\n{时间} 前：{内容}";

	public string DueReminderTemplate { get; set; } = "{学科}作业到时间了\n{时间} 前：{内容}";

	public int BackupLimit { get; set; } = 30;

	public string EmptyState { get; set; } = "collapsed-toolbar";

	public string ClassPeriodDays { get; set; } = "weekdays";

	public List<ClassPeriod> ClassPeriods { get; set; } = [];

	public bool FullscreenDetectionEnabled { get; set; } = true;

	public List<string> FullscreenProcessNames { get; set; } = [];

	public List<string> FullscreenBlockedProcessNames { get; set; } = ["explorer"];
}
