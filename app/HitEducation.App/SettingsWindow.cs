using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace HitEducation.App;

public partial class SettingsWindow : Window
{
	private readonly AppStorage storage;

	private readonly Action applied;

	private readonly DispatcherTimer liveSaveTimer = new DispatcherTimer();

	private ButtonBase? touchClickButton;

	private ComboBox? touchComboBox;

	private Point touchClickStart;

	private bool loading;

	private bool closingWithAnimation;

	public SettingsWindow(AppStorage storage, Action applied)
	{
		InitializeComponent();
		this.storage = storage;
		this.applied = applied;
		WindowOpenAnimation.Play(SettingsRoot);
		liveSaveTimer.Interval = TimeSpan.FromMilliseconds(500.0);
		liveSaveTimer.Tick += async (_, _) =>
		{
			liveSaveTimer.Stop();
			await storage.SaveAsync();
			StatusText.Text = Localizer.Text(storage, "SettingsAutoSaved");
		};
		EnableTouchControls();
		LoadSettings();
	}

	private void LoadSettings()
	{
		loading = true;
		AppSettings settings = storage.Data.Settings;
		settings.Language = Localizer.Normalize(settings.Language);
		LanguageBox.SelectedIndex = (Localizer.IsEnglish(settings.Language) ? 1 : 0);
		TopmostBox.IsChecked = settings.AlwaysOnTop;
		AutoStartBox.IsChecked = settings.AutoStart;
		AutoUpdateBox.IsChecked = settings.AutoUpdate;
		LockBox.IsChecked = settings.LockWindow;
		BackgroundImageBox.IsChecked = settings.BackgroundImageEnabled;
		BackgroundImagePathText.Text = DisplayBackgroundImagePath(settings.BackgroundImagePath);
		BackgroundOpacitySlider.Value = settings.Opacity;
		FontOpacitySlider.Value = settings.FontOpacity;
		FontSizeSlider.Value = settings.FontSize;
		AutoScrollSpeedSlider.Value = settings.AutoScrollSpeed;
		AutoScrollIdleSecondsBox.Text = settings.AutoScrollIdleSeconds.ToString();
		AutoScrollPauseSecondsBox.Text = settings.AutoScrollEdgePauseSeconds.ToString();
		RemindMinutesBox.Text = settings.RemindMinutesBefore.ToString();
		NotificationDurationBox.Text = settings.NotificationDurationSeconds.ToString();
		LoadReminderTextBoxes();
		ClassPeriodDaysBox.SelectedIndex = ((settings.ClassPeriodDays == "all") ? 1 : 0);
		FullscreenDetectionBox.IsChecked = settings.FullscreenDetectionEnabled;
		FullscreenProcessBox.Text = string.Join(Environment.NewLine, settings.FullscreenProcessNames);
		FullscreenBlockedProcessBox.Text = string.Join(Environment.NewLine, settings.FullscreenBlockedProcessNames);
		SubjectsBox.Text = string.Join(Environment.NewLine, from x in storage.Data.Subjects
			orderby x.SortOrder
			select $"{x.Name}|{x.Color}|{string.Join(',', x.Templates)}");
		ClassPeriodsBox.Text = string.Join(Environment.NewLine, settings.ClassPeriods.Select((ClassPeriod x) => x.Start + "-" + x.End));
		loading = false;
		ApplyLanguage();
		ApplyWindowOpacity();
	}

	private void ApplyLanguage()
	{
		base.Title = Localizer.Text(storage, "Settings");
		TitleText.Text = Localizer.Text(storage, "Settings");
		CheckUpdateButton.Content = Localizer.Text(storage, "CheckUpdate");
		ExportButton.Content = Localizer.Text(storage, "Export");
		ImportButton.Content = Localizer.Text(storage, "Import");
		RestoreBackupButton.Content = Localizer.Text(storage, "RestoreBackup");
		CancelButton.Content = Localizer.Text(storage, "Cancel");
		ApplyButton.Content = Localizer.Text(storage, "Apply");
		OkButton.Content = Localizer.Text(storage, "OK");
		LanguageLabel.Text = Localizer.Text(storage, "Language");
		TopmostBox.Content = Localizer.Text(storage, "KeepOnTop");
		AutoStartBox.Content = Localizer.Text(storage, "AutoStart");
		AutoUpdateBox.Content = Localizer.Text(storage, "AutoUpdate");
		LockBox.Content = Localizer.Text(storage, "LockWindowPositionSize");
		FullscreenDetectionBox.Content = Localizer.Text(storage, "EnableFullscreenDetection");
		BackgroundImageBox.Content = Localizer.Text(storage, "EnableBackgroundImage");
		ChooseBackgroundImageButton.Content = Localizer.Text(storage, "ChooseBackgroundImage");
		BackgroundImagePathText.Text = DisplayBackgroundImagePath(storage.Data.Settings.BackgroundImagePath);
		BackgroundOpacityLabel.Text = Localizer.Text(storage, "BackgroundOpacity");
		FontOpacityLabel.Text = Localizer.Text(storage, "FontOpacity");
		FontSizeLabel.Text = Localizer.Text(storage, "FontSize");
		AutoScrollSpeedLabel.Text = Localizer.Text(storage, "AutoScrollSpeed");
		AutoScrollIdleLabel.Text = Localizer.Text(storage, "IdleScrollSeconds");
		AutoScrollPauseLabel.Text = Localizer.Text(storage, "EdgePauseSeconds");
		ReminderHeader.Text = Localizer.Text(storage, "Reminder");
		RemindMinutesLabel.Text = Localizer.Text(storage, "MinutesBefore");
		NotificationDurationLabel.Text = Localizer.Text(storage, "NotificationSeconds");
		UpcomingTemplateLabel.Text = Localizer.Text(storage, "UpcomingTemplate");
		DueTemplateLabel.Text = Localizer.Text(storage, "DueTemplate");
		PlaceholdersHelpText.Text = Localizer.Text(storage, "PlaceholdersHelp");
		QuickTimeHeader.Text = Localizer.Text(storage, "QuickTime");
		QuickTimeHelpText.Text = Localizer.Text(storage, "QuickTimeHelp");
		SubjectsHeader.Text = Localizer.Text(storage, "SubjectsTemplates");
		TemplatesHelpText.Text = Localizer.Text(storage, "TemplatesHelp");
		ClassPeriodsHeader.Text = Localizer.Text(storage, "ClassPeriods");
		WeekdaysOnlyItem.Content = Localizer.Text(storage, "WeekdaysOnly");
		EveryDayItem.Content = Localizer.Text(storage, "EveryDay");
		FullscreenHeader.Text = Localizer.Text(storage, "FullscreenDetection");
		FullscreenHelpText.Text = Localizer.Text(storage, "FullscreenHelp");
		WhitelistLabel.Text = Localizer.Text(storage, "Whitelist");
		BlacklistLabel.Text = Localizer.Text(storage, "Blacklist");
	}

	private async void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!loading && LanguageBox.SelectedItem is ComboBoxItem { Tag: string tag })
		{
			string language = storage.Data.Settings.Language;
			storage.Data.Settings.Language = Localizer.Normalize(tag);
			UpdateDefaultReminderTemplatesForLanguage(language, storage.Data.Settings.Language);
			UpdateDefaultQuickTimeRulesForLanguage();
			LoadReminderTextBoxes();
			ApplyLanguage();
			applied();
			await storage.SaveAsync();
			StatusText.Text = Localizer.Text(storage, "SettingsSaved");
		}
	}

	private void LiveSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (!loading && base.IsLoaded)
		{
			storage.Data.Settings.Opacity = BackgroundOpacitySlider.Value;
			storage.Data.Settings.FontOpacity = FontOpacitySlider.Value;
			storage.Data.Settings.FontSize = FontSizeSlider.Value;
			storage.Data.Settings.AutoScrollSpeed = AutoScrollSpeedSlider.Value;
			ApplyWindowOpacity();
			applied();
			liveSaveTimer.Stop();
			liveSaveTimer.Start();
		}
	}

	private async void Apply_Click(object sender, RoutedEventArgs e)
	{
		MemoryOptimizer.AfterUserAction();
		await SaveSettingsAsync();
	}

	private async void Ok_Click(object sender, RoutedEventArgs e)
	{
		MemoryOptimizer.AfterUserAction();
		await SaveSettingsAsync();
		await CloseWithAnimationAsync();
	}

	private async Task SaveSettingsAsync()
	{
		AppSettings settings = storage.Data.Settings;
		if (LanguageBox.SelectedItem is ComboBoxItem { Tag: string tag })
		{
			settings.Language = Localizer.Normalize(tag);
		}
		settings.AlwaysOnTop = TopmostBox.IsChecked == true;
		settings.AutoStart = AutoStartBox.IsChecked == true;
		settings.AutoUpdate = AutoUpdateBox.IsChecked == true;
		settings.LockWindow = LockBox.IsChecked == true;
		settings.BackgroundImageEnabled = BackgroundImageBox.IsChecked == true;
		settings.Opacity = BackgroundOpacitySlider.Value;
		settings.FontOpacity = FontOpacitySlider.Value;
		settings.FontSize = FontSizeSlider.Value;
		settings.AutoScrollSpeed = AutoScrollSpeedSlider.Value;
		if (int.TryParse(AutoScrollIdleSecondsBox.Text.Trim(), out var idleSeconds))
		{
			settings.AutoScrollIdleSeconds = Math.Clamp(idleSeconds, 0, 600);
		}
		if (int.TryParse(AutoScrollPauseSecondsBox.Text.Trim(), out var pauseSeconds))
		{
			settings.AutoScrollEdgePauseSeconds = Math.Clamp(pauseSeconds, 0, 30);
		}
		if (int.TryParse(RemindMinutesBox.Text.Trim(), out var remindMinutes))
		{
			settings.RemindMinutesBefore = Math.Clamp(remindMinutes, 1, 120);
		}
		if (int.TryParse(NotificationDurationBox.Text.Trim(), out var durationSeconds))
		{
			settings.NotificationDurationSeconds = Math.Clamp(durationSeconds, 3, 60);
		}
		settings.UpcomingReminderTemplate = (string.IsNullOrWhiteSpace(UpcomingReminderTemplateBox.Text) ? Localizer.Text(storage, "UpcomingTemplateDefault") : UpcomingReminderTemplateBox.Text.Trim());
		settings.DueReminderTemplate = (string.IsNullOrWhiteSpace(DueReminderTemplateBox.Text) ? Localizer.Text(storage, "DueTemplateDefault") : DueReminderTemplateBox.Text.Trim());
		SaveQuickTimeRules(settings);
		settings.ClassPeriodDays = ((ClassPeriodDaysBox.SelectedIndex == 1) ? "all" : "weekdays");
		settings.FullscreenDetectionEnabled = FullscreenDetectionBox.IsChecked == true;
		settings.FullscreenProcessNames = FullscreenProcessBox.Text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToList();
		settings.FullscreenBlockedProcessNames = FullscreenBlockedProcessBox.Text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToList();
		AutoStartManager.Save(settings.AutoStart);
		SaveSubjects();
		SaveClassPeriods();
		await storage.SaveAsync();
		applied();
		ApplyLanguage();
		StatusText.Text = Localizer.Text(storage, "SettingsSaved");
	}

	private async void ChooseBackgroundImage_Click(object sender, RoutedEventArgs e)
	{
		OpenFileDialog dialog = new OpenFileDialog
		{
			Title = Localizer.Text(storage, "ChooseBackgroundImage"),
			Filter = Localizer.Text(storage, "ImageFileFilter")
		};
		if (dialog.ShowDialog(this) != true)
		{
			return;
		}

		try
		{
			string path = ImportBackgroundImage(dialog.FileName);
			storage.Data.Settings.BackgroundImagePath = path;
			storage.Data.Settings.BackgroundImageEnabled = true;
			BackgroundImageBox.IsChecked = true;
			BackgroundImagePathText.Text = DisplayBackgroundImagePath(path);
			await storage.SaveAsync();
			applied();
			StatusText.Text = Localizer.Text(storage, "BackgroundImageImported");
		}
		catch (Exception ex)
		{
			AppLogger.Error("Background image import failed: " + dialog.FileName, ex);
			StatusText.Text = Localizer.Format(storage, "BackgroundImageImportFailed", ex.Message);
		}
	}

	private string ImportBackgroundImage(string sourcePath)
	{
		string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
		string[] supportedExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp"];
		if (!supportedExtensions.Contains(extension))
		{
			throw new InvalidOperationException(Localizer.Text(storage, "UnsupportedImageFile"));
		}

		string directory = Path.Combine(storage.DataDirectory, "backgrounds");
		Directory.CreateDirectory(directory);
		string destination = Path.Combine(directory, "main-background" + extension);
		File.Copy(sourcePath, destination, overwrite: true);
		return destination;
	}

	private string DisplayBackgroundImagePath(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return Localizer.Text(storage, "NoBackgroundImageSelected");
		}
		return Path.GetFileName(path);
	}

	private void EnableTouchControls()
	{
		SettingsRoot.AddHandler(UIElement.PreviewTouchDownEvent, new EventHandler<TouchEventArgs>(Control_PreviewTouchDown), handledEventsToo: true);
		SettingsRoot.AddHandler(UIElement.PreviewTouchMoveEvent, new EventHandler<TouchEventArgs>(Control_PreviewTouchMove), handledEventsToo: true);
		SettingsRoot.AddHandler(UIElement.PreviewTouchUpEvent, new EventHandler<TouchEventArgs>(Control_PreviewTouchUp), handledEventsToo: true);
	}

	private void LoadReminderTextBoxes()
	{
		AppSettings settings = storage.Data.Settings;
		UpcomingReminderTemplateBox.Text = settings.UpcomingReminderTemplate;
		DueReminderTemplateBox.Text = settings.DueReminderTemplate;
		QuickTimeRulesBox.Text = string.Join(Environment.NewLine, from x in GetQuickTimeRules(settings)
			select x.Name + "|" + x.Rule);
	}

	private void UpdateDefaultReminderTemplatesForLanguage(string previousLanguage, string nextLanguage)
	{
		AppSettings settings = storage.Data.Settings;
		string previousUpcoming = Localizer.Text(previousLanguage, "UpcomingTemplateDefault");
		string previousDue = Localizer.Text(previousLanguage, "DueTemplateDefault");
		if (string.IsNullOrWhiteSpace(settings.UpcomingReminderTemplate) || settings.UpcomingReminderTemplate == previousUpcoming)
		{
			settings.UpcomingReminderTemplate = Localizer.Text(nextLanguage, "UpcomingTemplateDefault");
		}
		if (string.IsNullOrWhiteSpace(settings.DueReminderTemplate) || settings.DueReminderTemplate == previousDue)
		{
			settings.DueReminderTemplate = Localizer.Text(nextLanguage, "DueTemplateDefault");
		}
	}

	private void UpdateDefaultQuickTimeRulesForLanguage()
	{
		List<QuickTimeRule> quickTimeRules = storage.Data.Settings.QuickTimeRules;
		List<(string Key, string Rule)> defaults = DefaultQuickTimeRuleKeys(storage.Data.Settings).ToList();
		if (quickTimeRules.Count != 0 && quickTimeRules.Select(x => x.Rule).SequenceEqual(defaults.Select(x => x.Rule)))
		{
			for (int i = 0; i < quickTimeRules.Count && i < defaults.Count; i++)
			{
				quickTimeRules[i].Name = Localizer.Text(storage, defaults[i].Key);
			}
		}
	}

	private void Control_PreviewTouchDown(object? sender, TouchEventArgs e)
	{
		ComboBox comboBox = FindComboBox(e.OriginalSource);
		if (comboBox != null && comboBox.IsEnabled)
		{
			touchComboBox = comboBox;
			touchClickButton = null;
			touchClickStart = e.GetTouchPoint(this).Position;
			e.TouchDevice.Capture(comboBox);
			e.Handled = true;
			return;
		}
		ButtonBase buttonBase = FindButton(e.OriginalSource);
		if (buttonBase != null && buttonBase.IsEnabled)
		{
			touchClickButton = buttonBase;
			touchComboBox = null;
			touchClickStart = e.GetTouchPoint(this).Position;
			e.TouchDevice.Capture(buttonBase);
			e.Handled = true;
		}
	}

	private void Control_PreviewTouchMove(object? sender, TouchEventArgs e)
	{
		if (touchComboBox != null || touchClickButton != null)
		{
			Point position = e.GetTouchPoint(this).Position;
			if (Math.Abs(position.X - touchClickStart.X) > 12.0 || Math.Abs(position.Y - touchClickStart.Y) > 12.0)
			{
				touchComboBox = null;
				touchClickButton = null;
				e.TouchDevice.Capture(null);
			}
			e.Handled = true;
		}
	}

	private void Control_PreviewTouchUp(object? sender, TouchEventArgs e)
	{
		if (touchComboBox != null)
		{
			ComboBox comboBox = touchComboBox;
			touchComboBox = null;
			e.TouchDevice.Capture(null);
			if (IsTouchInside(e, comboBox))
			{
				comboBox.Focus();
				comboBox.IsDropDownOpen = true;
			}
			e.Handled = true;
		}
		else if (touchClickButton != null)
		{
			ButtonBase buttonBase = touchClickButton;
			touchClickButton = null;
			e.TouchDevice.Capture(null);
			if (IsTouchInside(e, buttonBase))
			{
				MemoryOptimizer.AfterUserAction();
				buttonBase.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, buttonBase));
			}
			e.Handled = true;
		}
	}

	private static ButtonBase? FindButton(object source)
	{
		for (DependencyObject? current = source as DependencyObject; current != null; current = VisualTreeHelper.GetParent(current))
		{
			if (current is ButtonBase result)
			{
				return result;
			}
		}
		return null;
	}

	private static ComboBox? FindComboBox(object source)
	{
		for (DependencyObject? current = source as DependencyObject; current != null; current = VisualTreeHelper.GetParent(current))
		{
			if (current is ComboBox result)
			{
				return result;
			}
		}
		return null;
	}

	private static bool IsTouchInside(TouchEventArgs e, FrameworkElement element)
	{
		if (!element.IsVisible || !element.IsEnabled)
		{
			return false;
		}
		Point position = e.GetTouchPoint(element).Position;
		if (position.X >= 0.0 && position.Y >= 0.0 && position.X <= element.ActualWidth)
		{
			return position.Y <= element.ActualHeight;
		}
		return false;
	}

	private void ApplyWindowOpacity()
	{
		double backgroundOpacity = Math.Clamp(BackgroundOpacitySlider.Value + 0.08, 0.35, 1.0);
		if (SettingsRoot.Background is SolidColorBrush rootBrush)
		{
			SolidColorBrush brush = rootBrush.Clone();
			brush.Opacity = backgroundOpacity;
			SettingsRoot.Background = brush;
		}
		if (TitleBar.Background is SolidColorBrush titleBrush)
		{
			SolidColorBrush brush = titleBrush.Clone();
			brush.Opacity = Math.Clamp(backgroundOpacity + 0.12, 0.35, 1.0);
			TitleBar.Background = brush;
		}
		ApplyControlBackgroundOpacity(SettingsRoot, Math.Clamp(backgroundOpacity + 0.08, 0.35, 1.0));
	}

	private static void ApplyControlBackgroundOpacity(DependencyObject root, double opacity)
	{
		int childrenCount = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < childrenCount; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is TextBox textBox)
			{
				textBox.Background = new SolidColorBrush(Color.FromRgb(byte.MaxValue, byte.MaxValue, byte.MaxValue))
				{
					Opacity = opacity
				};
			}
			else if (child is ComboBox comboBox)
			{
				comboBox.Background = new SolidColorBrush(Color.FromRgb(byte.MaxValue, byte.MaxValue, byte.MaxValue))
				{
					Opacity = opacity
				};
			}
			else if (child is Button button)
			{
				button.Background = new SolidColorBrush(Color.FromRgb(245, 245, 245))
				{
					Opacity = opacity
				};
			}
			ApplyControlBackgroundOpacity(child, opacity);
		}
	}

	private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ButtonState == MouseButtonState.Pressed)
		{
			DragMove();
		}
	}

	private async void Cancel_Click(object sender, RoutedEventArgs e)
	{
		MemoryOptimizer.AfterUserAction();
		await CloseWithAnimationAsync();
	}

	public async Task CloseWithAnimationAsync()
	{
		if (!closingWithAnimation)
		{
			closingWithAnimation = true;
			await WindowOpenAnimation.PlayCloseAsync(SettingsRoot);
			ReleaseHeavyControls();
			Close();
			MemoryOptimizer.TrimSoon();
		}
	}

	private void ReleaseHeavyControls()
	{
		UpcomingReminderTemplateBox.Clear();
		DueReminderTemplateBox.Clear();
		SubjectsBox.Clear();
		ClassPeriodsBox.Clear();
		QuickTimeRulesBox.Clear();
		FullscreenProcessBox.Clear();
		FullscreenBlockedProcessBox.Clear();
	}

	private void SaveSubjects()
	{
		string[] lines = SubjectsBox.Text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		List<Subject> subjects = new List<Subject>();
		int sortOrder = 1;
		foreach (string line in lines)
		{
			string[] parts = line.Split('|', 3, StringSplitOptions.TrimEntries);
			if (parts.Length != 0 && !string.IsNullOrWhiteSpace(parts[0]))
			{
				Subject subject = storage.Data.Subjects.FirstOrDefault((Subject s) => s.Name == parts[0] || Localizer.SubjectName(storage, s) == parts[0]);
				string color = subject?.Color ?? "#64748B";
				string templateText = (parts.Length > 1) ? parts[1] : "课时作业";
				if (parts.Length > 2)
				{
					color = (string.IsNullOrWhiteSpace(parts[1]) ? color : parts[1]);
					templateText = parts[2];
				}
				subjects.Add(new Subject
				{
					Id = (subject?.Id ?? Guid.NewGuid().ToString("N")),
					Name = ((subject != null && Localizer.IsDefaultSubject(subject)) ? subject.Name : parts[0]),
					Color = color,
					SortOrder = sortOrder++,
					Templates = templateText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
					TemplateCounters = (subject?.TemplateCounters ?? new Dictionary<string, int> { ["课时作业"] = 1 })
				});
			}
		}
		if (subjects.Count > 0)
		{
			storage.Data.Subjects = subjects;
		}
	}

	private void SaveQuickTimeRules(AppSettings settings)
	{
		List<QuickTimeRule> list = (from line in QuickTimeRulesBox.Text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			select line.Split('|', 2, StringSplitOptions.TrimEntries) into parts
			where parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1])
			select new QuickTimeRule
			{
				Name = parts[0],
				Rule = parts[1]
			}).ToList();
		if (list.Count > 0)
		{
			settings.QuickTimeRules = list;
		}
	}

	private static List<QuickTimeRule> GetQuickTimeRules(AppSettings settings)
	{
		if (settings.QuickTimeRules.Count > 0)
		{
			return settings.QuickTimeRules;
		}
		return (from x in DefaultQuickTimeRuleKeys(settings)
			select new QuickTimeRule
			{
				Name = Localizer.Text(settings.Language, x.Key),
				Rule = x.Rule
			}).ToList();
	}

	private static IEnumerable<(string Key, string Rule)> DefaultQuickTimeRuleKeys(AppSettings settings)
	{
		yield return (Key: "NoonToday", Rule: "today " + settings.LunchDismissalTime);
		yield return (Key: "Tonight", Rule: "today " + settings.EveningDismissalTime);
		yield return (Key: "TomorrowMorning", Rule: "tomorrow " + settings.MorningSubmitTime);
		yield return (Key: "TomorrowNoon", Rule: "tomorrow " + settings.LunchDismissalTime);
		yield return (Key: "TomorrowNight", Rule: "tomorrow " + settings.EveningDismissalTime);
		yield return (Key: "OneHourLater", Rule: "+1hour");
		yield return (Key: "ThirtyMinutesLater", Rule: "+30min");
	}

	private void SaveClassPeriods()
	{
		storage.Data.Settings.ClassPeriods.Clear();
		string[] lines = ClassPeriodsBox.Text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (string line in lines)
		{
			string[] parts = line.Split('-', 2, StringSplitOptions.TrimEntries);
			if (parts.Length == 2)
			{
				storage.Data.Settings.ClassPeriods.Add(new ClassPeriod
				{
					Start = parts[0],
					End = parts[1]
				});
			}
		}
	}

	private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
	{
		if (await storage.RestoreLatestBackupAsync())
		{
			LoadSettings();
			applied();
			StatusText.Text = Localizer.Text(storage, "RestoreBackupSuccess");
		}
		else
		{
			StatusText.Text = Localizer.Text(storage, "RestoreBackupFailed");
		}
	}

	private async void ExportSettings_Click(object sender, RoutedEventArgs e)
	{
		SaveFileDialog dialog = new SaveFileDialog
		{
			Title = Localizer.Text(storage, "ExportSettings"),
			Filter = Localizer.Text(storage, "SettingsFileFilter"),
			FileName = Localizer.Format(storage, "SettingsFileName", DateTime.Now.ToString("yyyyMMdd-HHmmss"))
		};
		if (dialog.ShowDialog(this) == true)
		{
			await SaveSettingsAsync();
			await storage.ExportSettingsAsync(dialog.FileName);
			StatusText.Text = Localizer.Text(storage, "SettingsExported");
		}
	}

	private async void ImportSettings_Click(object sender, RoutedEventArgs e)
	{
		OpenFileDialog dialog = new OpenFileDialog
		{
			Title = Localizer.Text(storage, "ImportSettings"),
			Filter = Localizer.Text(storage, "SettingsFileFilter")
		};
		if (dialog.ShowDialog(this) != true)
		{
			return;
		}
		try
		{
			await storage.ImportSettingsAsync(dialog.FileName);
			AutoStartManager.Save(storage.Data.Settings.AutoStart);
			LoadSettings();
			applied();
			StatusText.Text = Localizer.Text(storage, "SettingsImported");
		}
		catch (Exception ex)
		{
			AppLogger.Error("Settings import failed: " + dialog.FileName, ex);
			StatusText.Text = Localizer.Format(storage, "ImportFailed", ex.Message);
		}
	}

	private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
	{
		CheckUpdateButton.IsEnabled = false;
		StatusText.Text = Localizer.Text(storage, "CheckingUpdate");
		try
		{
			UpdateCheckResult result = await UpdateChecker.CheckAsync();
			if (!result.HasUpdate)
			{
				StatusText.Text = Localizer.Format(storage, "UpdateAlreadyLatest", result.CurrentVersion);
				return;
			}
			string notes = result.Manifest.Notes.Count == 0 ? Localizer.Text(storage, "UpdateNoNotes") : string.Join(Environment.NewLine, result.Manifest.Notes.Select((string x) => "- " + x));
			string message = Localizer.Format(storage, "UpdateAvailableMessage", result.CurrentVersion, result.LatestVersion, notes);
			StatusText.Text = Localizer.Format(storage, "UpdateAvailableStatus", result.LatestVersion);
			if (!string.IsNullOrWhiteSpace(result.Manifest.DownloadUrl) && MessageBox.Show(message, Localizer.Text(storage, "UpdateAvailableTitle"), MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
			{
				StatusText.Text = Localizer.Text(storage, "UpdateInstalling");
				if (!UpdateChecker.TryStartUpdater(auto: false))
				{
					StatusText.Text = Localizer.Text(storage, "UpdateStartFailed");
				}
			}
		}
		catch (Exception ex)
		{
			AppLogger.Error("Update check failed.", ex);
			StatusText.Text = Localizer.Format(storage, "UpdateCheckFailed", ex.Message);
		}
		finally
		{
			CheckUpdateButton.IsEnabled = true;
		}
	}

}
