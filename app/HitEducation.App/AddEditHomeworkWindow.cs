using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HitEducation.App;

public partial class AddEditHomeworkWindow : Window
{
	private const double TimeWheelStepPixels = 12.0;

	private readonly AppStorage storage;

	private readonly Action? saved;

	private string? editingId;

	private string? appliedTemplate;

	private ButtonBase? touchClickButton;

	private ComboBox? touchComboBox;

	private ComboBoxItem? touchComboBoxItem;

	private Point touchClickStart;

	private Point timeWheelDragStart;

	private bool closingDialog;

	private bool saving;

	private bool timeWheelDragging;

	private bool timeWheelDraggingHour;

	private bool syncingDueDate;

	private int selectedHour;

	private int selectedMinute;

	public bool IsEditing => editingId is not null;

	public static string DraftFile(AppStorage storage) => Path.Combine(storage.DataDirectory, "add-draft.json");

	public AddEditHomeworkWindow(AppStorage storage, string? editingId, Action? saved = null)
	{
		InitializeComponent();
		this.storage = storage;
		this.editingId = editingId;
		this.saved = saved;
		WindowOpenAnimation.Play(WindowRoot);
		SubjectBox.ItemsSource = storage.Data.Subjects.OrderBy((Subject s) => s.SortOrder).ToList();
		SubjectBox.SelectedIndex = 0;
		ApplyLanguage();
		ApplyDisplaySettings();
		PlaceErrorTextBesideCancelButton();
		EnableTouchButtonClicks();
		BuildTimeWheels();
		BuildQuickTimes();
		LoadEditingHomework();
		LoadDraftIfAdding();
	}

	public bool HasDraftContent()
	{
		return editingId is null
			&& (!string.IsNullOrWhiteSpace(ContentBox.Text)
				|| SubjectBox.SelectedIndex > 0
				|| NoSubmissionRequiredBox.IsChecked == true
				|| DueDatePicker.SelectedDate != DateTime.Today
				|| GetDueTimeText() != DefaultDueTimeText());
	}

	public async Task SaveDraftAsync()
	{
		if (editingId is not null)
		{
			return;
		}

		Directory.CreateDirectory(storage.DataDirectory);
		if (!HasDraftContent())
		{
			DeleteDraft(storage);
			return;
		}

		var draft = new AddHomeworkDraft
		{
			SubjectId = (SubjectBox.SelectedItem as Subject)?.Id,
			Content = ContentBox.Text,
			NoSubmissionRequired = NoSubmissionRequiredBox.IsChecked == true,
			DueDate = DueDatePicker.SelectedDate,
			DueTime = GetDueTimeText(),
			AppliedTemplate = appliedTemplate
		};
		string contents = JsonSerializer.Serialize(draft, new JsonSerializerOptions { WriteIndented = true });
		await File.WriteAllTextAsync(DraftFile(storage), contents);
	}

	public async Task CloseWithAnimationAsync()
	{
		if (closingDialog)
		{
			return;
		}

		closingDialog = true;
		await WindowOpenAnimation.PlayCloseAsync(WindowRoot);
		ReleaseHeavyControls();
		Close();
		MemoryOptimizer.TrimSoon();
	}

	public async Task HideWithAnimationAsync()
	{
		await WindowOpenAnimation.PlayCloseAsync(WindowRoot);
		ReleaseHeavyControls();
		Hide();
		MemoryOptimizer.TrimSoon();
	}

	public static void DeleteDraft(AppStorage storage)
	{
		var path = DraftFile(storage);
		if (File.Exists(path))
		{
			File.Delete(path);
		}
	}

	private void LoadDraftIfAdding()
	{
		if (editingId is not null)
		{
			return;
		}
		var path = DraftFile(storage);
		if (!File.Exists(path))
		{
			return;
		}
		try
		{
			var draft = JsonSerializer.Deserialize<AddHomeworkDraft>(File.ReadAllText(path));
			if (draft is null)
			{
				return;
			}
			if (!string.IsNullOrWhiteSpace(draft.SubjectId))
			{
				SubjectBox.SelectedItem = storage.Data.Subjects.FirstOrDefault((Subject s) => s.Id == draft.SubjectId) ?? SubjectBox.SelectedItem;
			}
			ContentBox.Text = draft.Content ?? "";
			NoSubmissionRequiredBox.IsChecked = draft.NoSubmissionRequired;
			SetDueDate(DateTime.Today);
			SetDueTime(DefaultDueTime());
			appliedTemplate = draft.AppliedTemplate;
			UpdateDueControlsEnabled();
		}
		catch (Exception exception)
		{
			AppLogger.Error("Add homework draft load failed", exception);
		}
	}

	private void EnableTouchButtonClicks()
	{
		WindowRoot.AddHandler(UIElement.PreviewTouchDownEvent, new EventHandler<TouchEventArgs>(Button_PreviewTouchDown), handledEventsToo: true);
		WindowRoot.AddHandler(UIElement.PreviewTouchMoveEvent, new EventHandler<TouchEventArgs>(Button_PreviewTouchMove), handledEventsToo: true);
		WindowRoot.AddHandler(UIElement.PreviewTouchUpEvent, new EventHandler<TouchEventArgs>(Button_PreviewTouchUp), handledEventsToo: true);
		SubjectBox.ItemContainerStyle = CreateSubjectBoxItemStyle(SubjectBox.ItemContainerStyle);
		SubjectBox.AddHandler(UIElement.PreviewTouchDownEvent, new EventHandler<TouchEventArgs>(SubjectBox_PreviewTouchDown), handledEventsToo: true);
		SubjectBox.AddHandler(UIElement.PreviewTouchMoveEvent, new EventHandler<TouchEventArgs>(SubjectBox_PreviewTouchMove), handledEventsToo: true);
		SubjectBox.AddHandler(UIElement.PreviewTouchUpEvent, new EventHandler<TouchEventArgs>(SubjectBox_PreviewTouchUp), handledEventsToo: true);
	}

	private Style CreateSubjectBoxItemStyle(Style? baseStyle)
	{
		var style = new Style(typeof(ComboBoxItem), baseStyle);
		style.Setters.Add(new EventSetter(UIElement.PreviewTouchDownEvent, new EventHandler<TouchEventArgs>(SubjectBoxItem_PreviewTouchDown)));
		style.Setters.Add(new EventSetter(UIElement.PreviewTouchMoveEvent, new EventHandler<TouchEventArgs>(SubjectBox_PreviewTouchMove)));
		style.Setters.Add(new EventSetter(UIElement.PreviewTouchUpEvent, new EventHandler<TouchEventArgs>(SubjectBox_PreviewTouchUp)));
		return style;
	}

	private void SubjectBoxItem_PreviewTouchDown(object? sender, TouchEventArgs e)
	{
		if (sender is ComboBoxItem { IsEnabled: not false } comboBoxItem)
		{
			SelectSubjectItemByTouch(comboBoxItem, e);
		}
	}

	private void SubjectBox_PreviewTouchDown(object? sender, TouchEventArgs e)
	{
		if (SubjectBox.IsDropDownOpen)
		{
			ComboBoxItem comboBoxItem = FindComboBoxItem(e.OriginalSource);
			if (comboBoxItem != null && comboBoxItem.IsEnabled)
			{
				SelectSubjectItemByTouch(comboBoxItem, e);
			}
		}
	}

	private void SelectSubjectItemByTouch(ComboBoxItem item, TouchEventArgs e)
	{
		touchComboBoxItem = null;
		touchComboBox = null;
		touchClickButton = null;
		e.TouchDevice.Capture(null);
		SubjectBox.SelectedItem = item.DataContext;
		SubjectBox.IsDropDownOpen = false;
		e.Handled = true;
	}

	private void BeginSubjectItemTouch(ComboBoxItem item, TouchEventArgs e)
	{
		touchComboBoxItem = item;
		touchClickStart = e.GetTouchPoint(this).Position;
		e.TouchDevice.Capture(item);
		e.Handled = true;
	}

	private void SubjectBox_PreviewTouchMove(object? sender, TouchEventArgs e)
	{
		if (touchComboBoxItem is null)
		{
			return;
		}

		Point position = e.GetTouchPoint(this).Position;
		if (Math.Abs(position.X - touchClickStart.X) > 12.0 || Math.Abs(position.Y - touchClickStart.Y) > 12.0)
		{
			touchComboBoxItem = null;
			SubjectBox.IsDropDownOpen = false;
			e.TouchDevice.Capture(null);
		}
		e.Handled = true;
	}

	private void SubjectBox_PreviewTouchUp(object? sender, TouchEventArgs e)
	{
		if (touchComboBoxItem is null)
		{
			return;
		}

		ComboBoxItem comboBoxItem = touchComboBoxItem;
		touchComboBoxItem = null;
		e.TouchDevice.Capture(null);
		if (IsTouchInside(e, comboBoxItem))
		{
			SubjectBox.SelectedItem = comboBoxItem.DataContext;
		}
		SubjectBox.IsDropDownOpen = false;
		e.Handled = true;
	}

	private void Button_PreviewTouchDown(object? sender, TouchEventArgs e)
	{
		ComboBox comboBox = FindComboBox(e.OriginalSource);
		if (comboBox == SubjectBox && comboBox.IsEnabled)
		{
			touchComboBox = comboBox;
			touchClickStart = e.GetTouchPoint(this).Position;
			e.TouchDevice.Capture(comboBox);
			e.Handled = true;
			return;
		}
		if (SubjectBox.IsDropDownOpen)
		{
			SubjectBox.IsDropDownOpen = false;
		}
		if (FindDatePicker(e.OriginalSource) != null)
		{
			return;
		}
		ButtonBase buttonBase = FindButton(e.OriginalSource);
		if (buttonBase != null && buttonBase.IsEnabled)
		{
			touchClickButton = buttonBase;
			touchClickStart = e.GetTouchPoint(this).Position;
			e.TouchDevice.Capture(buttonBase);
			e.Handled = true;
		}
	}

	private void Button_PreviewTouchMove(object? sender, TouchEventArgs e)
	{
		if (touchComboBoxItem is not null || (touchComboBox is null && touchClickButton is null))
		{
			return;
		}

		Point position = e.GetTouchPoint(this).Position;
		if (Math.Abs(position.X - touchClickStart.X) > 12.0 || Math.Abs(position.Y - touchClickStart.Y) > 12.0)
		{
			touchComboBox = null;
			touchClickButton = null;
			e.TouchDevice.Capture(null);
		}
		e.Handled = true;
	}

	private void Button_PreviewTouchUp(object? sender, TouchEventArgs e)
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
				buttonBase.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, buttonBase));
			}
			e.Handled = true;
		}
	}

	private static ButtonBase? FindButton(object source)
	{
		for (DependencyObject? current = source as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
		{
			if (current is ButtonBase button)
			{
				return button;
			}
		}
		return null;
	}

	private static ComboBox? FindComboBox(object source)
	{
		for (DependencyObject? current = source as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
		{
			if (current is ComboBox comboBox)
			{
				return comboBox;
			}
		}
		return null;
	}

	private static DatePicker? FindDatePicker(object source)
	{
		for (DependencyObject? current = source as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
		{
			if (current is DatePicker datePicker)
			{
				return datePicker;
			}
		}
		return null;
	}

	private static ComboBoxItem? FindComboBoxItem(object source)
	{
		for (DependencyObject? current = source as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
		{
			if (current is ComboBoxItem comboBoxItem)
			{
				return comboBoxItem;
			}
		}
		return null;
	}

	private static bool IsTouchInside(TouchEventArgs e, FrameworkElement element)
	{
		Point position = e.GetTouchPoint(element).Position;
		return position.X >= 0.0 && position.Y >= 0.0 && position.X <= element.ActualWidth && position.Y <= element.ActualHeight;
	}

	private void ApplyDisplaySettings()
	{
		ApplyBrushOpacity(WindowRoot, Border.BackgroundProperty, storage.Data.Settings.Opacity);
		ApplyTextOpacity(WindowContent, storage.Data.Settings.FontOpacity);
		ApplyButtonBackgroundOpacity(WindowContent, storage.Data.Settings.Opacity);
	}

	private void ApplyLanguage()
	{
		string key = editingId is null ? "AddHomework" : "EditHomework";
		Title = Localizer.Text(storage, key);
		TitleText.Text = Localizer.Text(storage, key);
		CancelButton.Content = Localizer.Text(storage, "Cancel");
		SaveContinueButton.Content = Localizer.Text(storage, "SaveContinue");
		SaveButton.Content = Localizer.Text(storage, "Save");
		SubjectLabel.Text = Localizer.Text(storage, "Subject");
		KeywordTemplatesLabel.Text = Localizer.Text(storage, "KeywordTemplates");
		HomeworkContentLabel.Text = Localizer.Text(storage, "HomeworkContent");
		DueTimeLabel.Text = Localizer.Text(storage, "DueTime");
		NoSubmissionRequiredBox.Content = Localizer.Text(storage, "NoSubmissionRequired");
	}

	private void BuildTimeWheels()
	{
		SetDueDate(DateTime.Today);
		SetDueTime(DefaultDueTime());
	}

	private static TimeSpan DefaultDueTime()
	{
		return DateTime.Now.TimeOfDay;
	}

	private static string DefaultDueTimeText()
	{
		return DateTime.Now.ToString("HH:mm");
	}

	private void SetDueDate(DateTime date)
	{
		DateTime day = date.Date;
		syncingDueDate = true;
		DueDatePicker.SelectedDate = day;
		DueDateCalendar.SelectedDate = day;
		DueDateCalendar.DisplayDate = day;
		syncingDueDate = false;
	}

	private void DueDateCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!syncingDueDate && DueDateCalendar.SelectedDate.HasValue)
		{
			DueDatePicker.SelectedDate = DueDateCalendar.SelectedDate.Value.Date;
		}
	}

	private void PlaceErrorTextBesideCancelButton()
	{
		if (ErrorText.Parent == CancelButton.Parent)
		{
			return;
		}
		RemoveErrorTextFromParent();
		ErrorText.Margin = new Thickness(0.0, 0.0, 12.0, 0.0);
		ErrorText.VerticalAlignment = VerticalAlignment.Center;
		ErrorText.TextAlignment = TextAlignment.Right;
		ErrorText.TextWrapping = TextWrapping.NoWrap;
		ErrorText.TextTrimming = TextTrimming.CharacterEllipsis;
		ErrorText.MinWidth = 180.0;
		ErrorText.MaxWidth = 360.0;
		if (CancelButton.Parent is Panel panel)
		{
			int index = panel.Children.IndexOf(CancelButton);
			if (index >= 0)
			{
				panel.Children.Insert(index, ErrorText);
			}
			else
			{
				panel.Children.Add(ErrorText);
			}
		}
	}

	private void RemoveErrorTextFromParent()
	{
		if (ErrorText.Parent is Panel panel)
		{
			panel.Children.Remove(ErrorText);
		}
		else if (ErrorText.Parent is Decorator decorator && decorator.Child == ErrorText)
		{
			decorator.Child = null;
		}
		else if (ErrorText.Parent is ContentControl contentControl && contentControl.Content == ErrorText)
		{
			contentControl.Content = null;
		}
	}

	private void SetDueTimeFromText(string? value)
	{
		SetDueTime(TimeSpan.TryParse(value, out var result) ? result : DefaultDueTime());
	}

	private void SetDueTime(TimeSpan time)
	{
		selectedHour = Math.Clamp(time.Hours, 0, 23);
		selectedMinute = Math.Clamp(time.Minutes, 0, 59);
		RefreshTimeWheelText();
	}

	private string GetDueTimeText()
	{
		return $"{selectedHour:00}:{selectedMinute:00}";
	}

	private void RefreshTimeWheelText()
	{
		HourPrevText.Text = WrapTimeValue(selectedHour - 1, 24).ToString("00");
		HourText.Text = selectedHour.ToString("00");
		HourNextText.Text = WrapTimeValue(selectedHour + 1, 24).ToString("00");
		MinutePrevText.Text = WrapTimeValue(selectedMinute - 1, 60).ToString("00");
		MinuteText.Text = selectedMinute.ToString("00");
		MinuteNextText.Text = WrapTimeValue(selectedMinute + 1, 60).ToString("00");
	}

	private static int WrapTimeValue(int value, int count)
	{
		return (value % count + count) % count;
	}

	private void ChangeTimeWheelValue(bool hour, int delta)
	{
		AnimateTimeWheel(hour, delta);
		if (hour)
		{
			selectedHour = WrapTimeValue(selectedHour + delta, 24);
		}
		else
		{
			selectedMinute = WrapTimeValue(selectedMinute + delta, 60);
		}
		RefreshTimeWheelText();
	}

	private void AnimateTimeWheel(bool hour, int delta)
	{
		TranslateTransform transform = hour ? HourWheelTransform : MinuteWheelTransform;
		transform.BeginAnimation(TranslateTransform.YProperty, null);
		transform.Y = Math.Clamp(transform.Y, -18.0, 18.0);
		var animation = new DoubleAnimation
		{
			From = delta > 0 ? 18.0 : -18.0,
			To = 0.0,
			Duration = TimeSpan.FromMilliseconds(150.0),
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		transform.BeginAnimation(TranslateTransform.YProperty, animation);
	}

	private bool IsPointInHourWheel(Point point)
	{
		return point.X < DueTimeWheel.ActualWidth / 2.0;
	}

	private void DueTimeWheel_MouseWheel(object sender, MouseWheelEventArgs e)
	{
		ChangeTimeWheelValue(IsPointInHourWheel(e.GetPosition(DueTimeWheel)), e.Delta > 0 ? -1 : 1);
		e.Handled = true;
	}

	private void DueTimeWheel_MouseDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton != MouseButton.Left || !DueTimeWheel.IsEnabled)
		{
			return;
		}

		timeWheelDragging = true;
		timeWheelDragStart = e.GetPosition(DueTimeWheel);
		timeWheelDraggingHour = IsPointInHourWheel(timeWheelDragStart);
		BeginTimeWheelDragVisual();
		DueTimeWheel.CaptureMouse();
		e.Handled = true;
	}

	private void DueTimeWheel_MouseMove(object sender, MouseEventArgs e)
	{
		if (!timeWheelDragging)
		{
			return;
		}

		UpdateTimeWheelDrag(e.GetPosition(DueTimeWheel).Y);
		e.Handled = true;
	}

	private void DueTimeWheel_MouseUp(object sender, MouseButtonEventArgs e)
	{
		if (!timeWheelDragging)
		{
			return;
		}

		timeWheelDragging = false;
		EndTimeWheelDragVisual();
		DueTimeWheel.ReleaseMouseCapture();
		e.Handled = true;
	}

	private void DueTimeWheel_TouchDown(object sender, TouchEventArgs e)
	{
		if (!DueTimeWheel.IsEnabled)
		{
			return;
		}

		timeWheelDragging = true;
		timeWheelDragStart = e.GetTouchPoint(DueTimeWheel).Position;
		timeWheelDraggingHour = IsPointInHourWheel(timeWheelDragStart);
		BeginTimeWheelDragVisual();
		e.TouchDevice.Capture(DueTimeWheel);
		e.Handled = true;
	}

	private void DueTimeWheel_TouchMove(object sender, TouchEventArgs e)
	{
		if (!timeWheelDragging)
		{
			return;
		}

		UpdateTimeWheelDrag(e.GetTouchPoint(DueTimeWheel).Position.Y);
		e.Handled = true;
	}

	private void DueTimeWheel_TouchUp(object sender, TouchEventArgs e)
	{
		if (!timeWheelDragging)
		{
			return;
		}

		timeWheelDragging = false;
		EndTimeWheelDragVisual();
		e.TouchDevice.Capture(null);
		e.Handled = true;
	}

	private void UpdateTimeWheelDrag(double currentY)
	{
		double dragOffset = Math.Clamp(currentY - timeWheelDragStart.Y, -18.0, 18.0);
		TimeWheelDragTransform.Y = dragOffset;
		SetActiveWheelTransformY(dragOffset);
		int steps = (int)((currentY - timeWheelDragStart.Y) / TimeWheelStepPixels);
		if (steps == 0)
		{
			return;
		}

		ChangeTimeWheelValue(timeWheelDraggingHour, -steps);
		timeWheelDragStart.Y += steps * TimeWheelStepPixels;
		dragOffset = Math.Clamp(currentY - timeWheelDragStart.Y, -18.0, 18.0);
		TimeWheelDragTransform.Y = dragOffset;
		SetActiveWheelTransformY(dragOffset);
	}

	private void BeginTimeWheelDragVisual()
	{
		Grid.SetColumn(TimeWheelDragBorder, timeWheelDraggingHour ? 0 : 2);
		TimeWheelDragBorder.Opacity = 1.0;
		TimeWheelDragTransform.Y = 0.0;
		SetActiveWheelTransformY(0.0);
	}

	private void EndTimeWheelDragVisual()
	{
		TimeWheelDragTransform.Y = 0.0;
		AnimateActiveWheelBackToCenter();
		TimeWheelDragBorder.Opacity = 0.0;
	}

	private void SetActiveWheelTransformY(double offset)
	{
		TranslateTransform transform = timeWheelDraggingHour ? HourWheelTransform : MinuteWheelTransform;
		transform.BeginAnimation(TranslateTransform.YProperty, null);
		transform.Y = offset;
	}

	private void AnimateActiveWheelBackToCenter()
	{
		TranslateTransform transform = timeWheelDraggingHour ? HourWheelTransform : MinuteWheelTransform;
		var animation = new DoubleAnimation
		{
			To = 0.0,
			Duration = TimeSpan.FromMilliseconds(120.0),
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		transform.BeginAnimation(TranslateTransform.YProperty, animation);
	}

	private static void ApplyBrushOpacity(DependencyObject target, DependencyProperty property, double opacity)
	{
		if (target.GetValue(property) is not SolidColorBrush brush)
		{
			return;
		}

		SolidColorBrush next = brush.Clone();
		next.Opacity = Math.Clamp(opacity, 0.35, 1.0);
		target.SetValue(property, next);
	}

	private static void ApplyTextOpacity(DependencyObject root, double opacity)
	{
		int childrenCount = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < childrenCount; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is TextBlock { Foreground: SolidColorBrush foreground } textBlock)
			{
				SolidColorBrush solidColorBrush = foreground.Clone();
				solidColorBrush.Opacity = Math.Clamp(opacity, 0.35, 1.0);
				textBlock.Foreground = solidColorBrush;
			}
			ApplyTextOpacity(child, opacity);
		}
	}

	private static void ApplyButtonBackgroundOpacity(DependencyObject root, double opacity)
	{
		int childrenCount = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < childrenCount; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is Button button)
			{
				button.Background = new SolidColorBrush(Color.FromRgb(245, 245, 245))
				{
					Opacity = Math.Clamp(opacity, 0.35, 1.0)
				};
			}
			ApplyButtonBackgroundOpacity(child, opacity);
		}
	}

	private void LoadEditingHomework()
	{
		Homework homework = storage.Data.Homeworks.FirstOrDefault((Homework h) => h.Id == editingId);
		if (homework == null)
		{
			SetDueDate(DateTime.Today);
			SetDueTime(DefaultDueTime());
			return;
		}
		ApplyLanguage();
		SubjectBox.SelectedItem = storage.Data.Subjects.FirstOrDefault((Subject s) => s.Id == homework.SubjectId);
		ContentBox.Text = homework.Content;
		appliedTemplate = FindMatchingNumberedTemplate(storage.Data.Subjects.FirstOrDefault((Subject s) => s.Id == homework.SubjectId), homework.Content);
		NoSubmissionRequiredBox.IsChecked = homework.NoSubmissionRequired;
		SetDueDate(homework.DueAt.Date);
		SetDueTime(homework.DueAt.TimeOfDay);
		UpdateDueControlsEnabled();
	}

	private void SubjectBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		BuildTemplates();
	}

	private void BuildTemplates()
	{
		TemplatePanel.Children.Clear();
		if (SubjectBox.SelectedItem is not Subject subject)
		{
			return;
		}
		foreach (string template in subject.Templates)
		{
			Button button = new Button
			{
				Content = TemplateButtonText(subject, template),
				MinWidth = 84.0,
				Height = 42.0,
				Margin = new Thickness(0.0, 0.0, 8.0, 8.0),
				Background = new SolidColorBrush(Color.FromRgb(245, 245, 245))
				{
					Opacity = Math.Clamp(storage.Data.Settings.Opacity, 0.35, 1.0)
				}
			};
			button.Click += (_, _) => ApplyTemplate(subject, template);
			TemplatePanel.Children.Add(button);
		}
	}

	private void ApplyTemplate(Subject subject, string template)
	{
		appliedTemplate = template;
		string pattern;
		int number;
		string text = ((!TryGetNumberedTemplate(subject, template, out pattern, out number)) ? template : pattern.Replace("{n}", number.ToString()));
		if (!string.IsNullOrEmpty(ContentBox.Text) && !char.IsWhiteSpace(ContentBox.Text[^1]))
		{
			ContentBox.Text += " ";
		}
		ContentBox.Text += text;
		ContentBox.Focus();
		ContentBox.CaretIndex = ContentBox.Text.Length;
	}

	private void BuildQuickTimes()
	{
		IEnumerable<QuickTimeRule> rules = storage.Data.Settings.QuickTimeRules.Count > 0 ? storage.Data.Settings.QuickTimeRules : DefaultQuickTimeRules();
		foreach (QuickTimeRule item in rules)
		{
			if (TryResolveQuickTime(item.Rule, out var dueAt))
			{
				AddQuickButton(item.Name, dueAt);
			}
		}
	}

	private void AddQuickButton(string text, DateTime dueAt)
	{
		Button button = new Button
		{
			Content = text,
			MinWidth = 94.0,
			Height = 42.0,
			Margin = new Thickness(0.0, 0.0, 8.0, 8.0),
			Background = new SolidColorBrush(Color.FromRgb(245, 245, 245))
			{
				Opacity = Math.Clamp(storage.Data.Settings.Opacity, 0.35, 1.0)
			}
		};
		button.Click += (_, _) => SetDueAt(dueAt);
		QuickTimePanel.Children.Add(button);
	}

	private DateTime TodayAt(string time)
	{
		return DateTime.Today.Add(ParseTime(time));
	}

	private static TimeSpan ParseTime(string value)
	{
		return TimeSpan.TryParse(value, out var result) ? result : new TimeSpan(8, 0, 0);
	}

	private List<QuickTimeRule> DefaultQuickTimeRules()
	{
		return
		[
			new QuickTimeRule { Name = Localizer.Text(storage, "NoonToday"), Rule = $"today {storage.Data.Settings.LunchDismissalTime}" },
			new QuickTimeRule { Name = Localizer.Text(storage, "Tonight"), Rule = $"today {storage.Data.Settings.EveningDismissalTime}" },
			new QuickTimeRule { Name = Localizer.Text(storage, "TomorrowMorning"), Rule = $"tomorrow {storage.Data.Settings.MorningSubmitTime}" },
			new QuickTimeRule { Name = Localizer.Text(storage, "TomorrowNoon"), Rule = $"tomorrow {storage.Data.Settings.LunchDismissalTime}" },
			new QuickTimeRule { Name = Localizer.Text(storage, "TomorrowNight"), Rule = $"tomorrow {storage.Data.Settings.EveningDismissalTime}" },
			new QuickTimeRule { Name = Localizer.Text(storage, "OneHourLater"), Rule = "+1hour" },
			new QuickTimeRule { Name = Localizer.Text(storage, "ThirtyMinutesLater"), Rule = "+30min" }
		];
	}

	private static bool TryResolveQuickTime(string rule, out DateTime dueAt)
	{
		dueAt = DateTime.Now.AddHours(1.0);
		string value = rule.Trim().ToLowerInvariant();
		if (value.StartsWith("+", StringComparison.Ordinal))
		{
			string amountText = new string(value.Skip(1).TakeWhile(char.IsDigit).ToArray());
			if (!int.TryParse(amountText, out var amount))
			{
				return false;
			}

			string unit = value[(amountText.Length + 1)..].Trim();
			dueAt = unit.StartsWith("hour", StringComparison.Ordinal) || unit.StartsWith("h", StringComparison.Ordinal)
				? DateTime.Now.AddHours(amount)
				: DateTime.Now.AddMinutes(amount);
			return true;
		}

		string[] array = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (array.Length == 3 && (array[0] == "weekday" || array[0] == "week" || array[0] == "周" || array[0] == "星期"))
		{
			if (!int.TryParse(array[1], out var day) || day is < 1 or > 7 || !TimeSpan.TryParse(array[2], out var weekdayTime))
			{
				return false;
			}

			int currentDay = DateTime.Today.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)DateTime.Today.DayOfWeek;
			dueAt = DateTime.Today.AddDays(day - currentDay).Add(weekdayTime);
			return true;
		}

		if (array.Length != 2 || !TimeSpan.TryParse(array[1], out var time))
		{
			return false;
		}

		dueAt = array[0] switch
		{
			"today" or "今天" => DateTime.Today.Add(time),
			"tomorrow" or "明天" => DateTime.Today.AddDays(1.0).Add(time),
			_ => dueAt
		};
		return array[0] is "today" or "今天" or "tomorrow" or "明天";
	}

	private void SetDueAt(DateTime dueAt)
	{
		NoSubmissionRequiredBox.IsChecked = false;
		SetDueDate(dueAt.Date);
		SetDueTime(dueAt.TimeOfDay);
		UpdateDueControlsEnabled();
	}

	private void NoSubmissionRequiredBox_Changed(object sender, RoutedEventArgs e)
	{
		UpdateDueControlsEnabled();
	}

	private void ClearContentButton_Click(object sender, RoutedEventArgs e)
	{
		MemoryOptimizer.AfterUserAction();
		ContentBox.Clear();
		ContentBox.Focus();
	}

	private void UpdateDueControlsEnabled()
	{
		bool isEnabled = NoSubmissionRequiredBox.IsChecked != true;
		DueDatePicker.IsEnabled = isEnabled;
		DueTimeWheel.IsEnabled = isEnabled;
		QuickTimePanel.IsEnabled = isEnabled;
	}

	private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ButtonState == MouseButtonState.Pressed)
		{
			DragMove();
		}
	}

	private async void Save_Click(object sender, RoutedEventArgs e)
	{
		MemoryOptimizer.AfterUserAction();
		if (saving || closingDialog)
		{
			return;
		}
		saving = true;
		try
		{
			if (await SaveAsync())
			{
				await CloseDialogAsync(result: true);
			}
		}
		finally
		{
			saving = false;
		}
	}

	private async void SaveContinue_Click(object sender, RoutedEventArgs e)
	{
		MemoryOptimizer.AfterUserAction();
		if (saving || closingDialog)
		{
			return;
		}
		saving = true;
		try
		{
			if (await SaveAsync())
			{
				editingId = null;
				ContentBox.Clear();
				NoSubmissionRequiredBox.IsChecked = false;
				SetDueDate(DateTime.Today);
				SetDueTime(DefaultDueTime());
				UpdateDueControlsEnabled();
				BuildTemplates();
				ErrorText.Text = Localizer.Text(storage, "SavedContinue");
			}
		}
		finally
		{
			saving = false;
		}
	}

	private async Task<bool> SaveAsync()
	{
		ErrorText.Text = "";
		if (SubjectBox.SelectedItem is not Subject subject)
		{
			ErrorText.Text = Localizer.Text(storage, "ChooseSubject");
			return false;
		}

		string content = ContentBox.Text.Trim();
		if (string.IsNullOrWhiteSpace(content))
		{
			ErrorText.Text = Localizer.Text(storage, "ContentRequired");
			return false;
		}

		bool noSubmissionRequired = NoSubmissionRequiredBox.IsChecked == true;
		DateTime dueAt;
		if (noSubmissionRequired)
		{
			dueAt = DateTime.MaxValue;
		}
		else if (!DueDatePicker.SelectedDate.HasValue || !TimeSpan.TryParse(GetDueTimeText(), out var dueTime))
		{
			ErrorText.Text = Localizer.Text(storage, "InvalidDueTime");
			return false;
		}
		else
		{
			dueAt = DueDatePicker.SelectedDate.Value.Date.Add(dueTime);
		}

		if (!noSubmissionRequired && dueAt <= DateTime.Now)
		{
			ErrorText.Text = Localizer.Text(storage, "DueTimePast");
			return false;
		}
		if (storage.IsDuplicate(editingId, subject.Id, content, dueAt, noSubmissionRequired))
		{
			ErrorText.Text = Localizer.Text(storage, "DuplicateHomework");
			return false;
		}

		Homework? homework = storage.Data.Homeworks.FirstOrDefault((Homework h) => h.Id == editingId);
		if (homework is null)
		{
			homework = new Homework();
			storage.Data.Homeworks.Add(homework);
		}

		bool reminderTargetChanged = homework.SubjectId != subject.Id
			|| homework.Content != content
			|| homework.DueAt != dueAt
			|| homework.NoSubmissionRequired != noSubmissionRequired;
		homework.SubjectId = subject.Id;
		homework.SubjectName = subject.Name;
		homework.Content = content;
		homework.DueAt = dueAt;
		homework.NoSubmissionRequired = noSubmissionRequired;
		homework.Status = "active";
		if (reminderTargetChanged)
		{
			homework.RemindedAt = null;
			homework.DueRemindedAt = null;
			homework.DeferredReminderQueuedAt = null;
			homework.DeferredDueReminderQueuedAt = null;
		}
		IncrementTemplateCounter(subject, content);
		await storage.SaveAsync();
		DeleteDraft(storage);
		saved?.Invoke();
		return true;
	}

	private void IncrementTemplateCounter(Subject subject, string content)
	{
		string? template = appliedTemplate ?? FindMatchingNumberedTemplate(subject, content);
		if (template is null)
		{
			return;
		}

		string pattern = NumberedPattern(template);
		if (!TryExtractTemplateNumberFromContent(pattern, content, out var number))
		{
			return;
		}

		subject.TemplateCounters[template] = Math.Max(subject.TemplateCounters.GetValueOrDefault(template, 1), number + 1);
	}

	private static string TemplateButtonText(Subject subject, string template)
	{
		if (!TryGetNumberedTemplate(subject, template, out string pattern, out int number))
		{
			return template;
		}
		return pattern.Replace("{n}", number.ToString());
	}

	private static bool TryGetNumberedTemplate(Subject subject, string template, out string pattern, out int number)
	{
		pattern = NumberedPattern(template);
		number = subject.TemplateCounters.GetValueOrDefault(template, 1);
		return pattern.Contains("{n}", StringComparison.Ordinal);
	}

	private static string NumberedPattern(string template)
	{
		return template.Contains("{n}", StringComparison.Ordinal)
			? template
			: template == "课时作业" ? "课时作业 {n}" : template;
	}

	private static string? FindMatchingNumberedTemplate(Subject? subject, string content)
	{
		return subject?.Templates.FirstOrDefault((string template) => TryExtractTemplateNumber(NumberedPattern(template), content, out _));
	}

	private static bool TryExtractTemplateNumber(string pattern, string content, out int number)
	{
		number = 0;
		int markerIndex = pattern.IndexOf("{n}", StringComparison.Ordinal);
		if (markerIndex < 0)
		{
			return false;
		}

		string prefix = pattern[..markerIndex];
		string suffix = pattern[(markerIndex + 3)..];
		if (!content.StartsWith(prefix, StringComparison.CurrentCulture) || !content.EndsWith(suffix, StringComparison.CurrentCulture))
		{
			return false;
		}

		string numberText = content[prefix.Length..(content.Length - suffix.Length)].Trim();
		return int.TryParse(numberText, out number);
	}

	private static bool TryExtractTemplateNumberFromContent(string pattern, string content, out int number)
	{
		if (TryExtractTemplateNumber(pattern, content, out number))
		{
			return true;
		}
		number = 0;
		int markerIndex = pattern.IndexOf("{n}", StringComparison.Ordinal);
		if (markerIndex < 0)
		{
			return false;
		}

		string prefix = pattern[..markerIndex];
		string suffix = pattern[(markerIndex + 3)..];
		int prefixIndex = content.LastIndexOf(prefix, StringComparison.CurrentCulture);
		if (prefixIndex < 0)
		{
			return false;
		}

		int numberStart = prefixIndex + prefix.Length;
		int numberEnd = content.Length;
		if (!string.IsNullOrEmpty(suffix))
		{
			if (!content.EndsWith(suffix, StringComparison.CurrentCulture))
			{
				return false;
			}
			numberEnd -= suffix.Length;
		}
		if (numberEnd < numberStart)
		{
			return false;
		}

		string numberText = content[numberStart..numberEnd].Trim();
		return int.TryParse(numberText, out number);
	}

	private async void Cancel_Click(object sender, RoutedEventArgs e)
	{
		MemoryOptimizer.AfterUserAction();
		await CloseDialogAsync(result: false);
	}

	private async Task CloseDialogAsync(bool result)
	{
		if (closingDialog)
		{
			return;
		}
		closingDialog = true;
		await WindowOpenAnimation.PlayCloseAsync(WindowRoot);
		ReleaseHeavyControls();
		if (ComponentDispatcher.IsThreadModal && Owner?.IsEnabled == false)
		{
			DialogResult = result;
		}
		else
		{
			Close();
		}
		MemoryOptimizer.TrimSoon();
	}

	private void ReleaseHeavyControls()
	{
		ContentBox.Clear();
		TemplatePanel.Children.Clear();
		QuickTimePanel.Children.Clear();
		SubjectBox.ItemsSource = null;
	}

}
