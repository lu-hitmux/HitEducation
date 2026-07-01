using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace HitEducation.App;

public partial class HomeworkListWindow : Window
{
	private readonly AppStorage storage;

	private readonly Action refreshMain;

	private readonly Stack<List<Homework>> undoStack = new Stack<List<Homework>>();

	private readonly Stack<List<Homework>> redoStack = new Stack<List<Homework>>();

	private string? pendingBatchAction;

	private ButtonBase? touchClickButton;

	private Point touchClickStart;

	private bool showHistory;

	private bool closingWithAnimation;

	public HomeworkListWindow(AppStorage storage, Action refreshMain)
	{
		InitializeComponent();
		this.storage = storage;
		this.refreshMain = refreshMain;
		ConfigureWindowMode();
		ApplyDisplaySettings();
		WindowOpenAnimation.Play(ListRoot);
		EnableTouchButtonClicks();
		ApplyLanguage();
		Render();
	}

	private void ConfigureWindowMode()
	{
		WindowStyle = WindowStyle.None;
		AllowsTransparency = true;
		Background = Brushes.Transparent;
		ShowInTaskbar = false;
	}

	private void ApplyDisplaySettings()
	{
		ApplyBrushOpacity(ListRoot, Border.BackgroundProperty, storage.Data.Settings.Opacity);
		HomeworkList.Background = Brushes.Transparent;
		HomeworkList.BorderBrush = Brushes.Transparent;
		HomeworkList.HorizontalContentAlignment = HorizontalAlignment.Stretch;
	}

	private void ApplyLanguage()
	{
		base.Title = Localizer.Text(storage, "List");
		ActiveButton.Content = Localizer.Text(storage, "CurrentHomework");
		HistoryButtonText.Text = Localizer.Text(storage, "History");
		BatchArchiveButton.Visibility = Visibility.Collapsed;
		BatchRestoreButton.Visibility = Visibility.Collapsed;
		BatchDeleteButton.Visibility = Visibility.Collapsed;
		BatchArchiveButtonText.Text = Localizer.Text(storage, "BatchArchive");
		BatchRestoreButton.Content = Localizer.Text(storage, "BatchRestore");
		BatchDeleteButton.Content = Localizer.Text(storage, "BatchDelete");
		UndoButtonText.Text = Localizer.IsEnglish(storage.Data.Settings.Language) ? "Undo" : "撤销";
		RedoButtonText.Text = Localizer.IsEnglish(storage.Data.Settings.Language) ? "Redo" : "恢复撤销";
		CloseButtonText.Text = Localizer.IsEnglish(storage.Data.Settings.Language) ? "Close" : "关闭";
		UpdateBatchButtons();
	}

	public void RefreshLanguage()
	{
		ApplyLanguage();
		Render();
	}

	public async Task CloseWithAnimationAsync()
	{
		closingWithAnimation = true;
		await WindowOpenAnimation.PlayCloseAsync(ListRoot);
		ClearListItems();
		Close();
		MemoryOptimizer.TrimSoon();
	}

	public async Task HideWithAnimationAsync()
	{
		await WindowOpenAnimation.PlayCloseAsync(ListRoot);
		Hide();
		MemoryOptimizer.TrimSoon();
	}

	private async void Window_Closing(object? sender, CancelEventArgs e)
	{
		if (!closingWithAnimation)
		{
			e.Cancel = true;
			await CloseWithAnimationAsync();
		}
	}

	private void DragBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ButtonState == MouseButtonState.Pressed && IsDragSource(e.OriginalSource))
		{
			DragMove();
		}
	}

	private static bool IsDragSource(object source)
	{
		for (DependencyObject? current = source as DependencyObject; current != null; current = VisualTreeHelper.GetParent(current))
		{
			if (current is ButtonBase or ListBoxItem or ScrollBar or TextBox)
			{
				return false;
			}
		}
		return true;
	}

	private static bool ContainsDescendant(DependencyObject parent, DependencyObject child)
	{
		for (DependencyObject? current = child; current != null; current = VisualTreeHelper.GetParent(current))
		{
			if (ReferenceEquals(current, parent))
			{
				return true;
			}
		}
		return false;
	}

	private async void CloseButton_Click(object sender, RoutedEventArgs e)
	{
		MemoryOptimizer.AfterUserAction();
		await CloseWithAnimationAsync();
	}

	private async void UndoButton_Click(object sender, RoutedEventArgs e)
	{
		MemoryOptimizer.AfterUserAction();
		ExitBatchMode();
		await RestoreSnapshotAsync(undoStack, redoStack);
	}

	private async void RedoButton_Click(object sender, RoutedEventArgs e)
	{
		MemoryOptimizer.AfterUserAction();
		ExitBatchMode();
		await RestoreSnapshotAsync(redoStack, undoStack);
	}

	private async Task RestoreSnapshotAsync(Stack<List<Homework>> source, Stack<List<Homework>> destination)
	{
		if (source.Count == 0)
		{
			return;
		}
		destination.Push(CloneHomeworks(storage.Data.Homeworks));
		storage.Data.Homeworks = source.Pop();
		await storage.SaveAsync();
		Render();
		refreshMain();
		UpdateUndoRedoButtons();
	}

	private void CaptureUndoSnapshot()
	{
		undoStack.Push(CloneHomeworks(storage.Data.Homeworks));
		redoStack.Clear();
		UpdateUndoRedoButtons();
	}

	private void UpdateUndoRedoButtons()
	{
		UndoButton.IsEnabled = undoStack.Count > 0;
		RedoButton.IsEnabled = redoStack.Count > 0;
	}

	private static List<Homework> CloneHomeworks(List<Homework> homeworks)
	{
		return JsonSerializer.Deserialize<List<Homework>>(JsonSerializer.Serialize(homeworks)) ?? new List<Homework>();
	}

	private void Render()
	{
		ClearListItems();
		UpdateBatchButtons();
		List<Homework> items = storage.Data.Homeworks
			.Where(h => showHistory ? h.Status == "archived" : h.Status == "active")
			.OrderBy(h => h.NoSubmissionRequired)
			.ThenBy(h => h.DueAt)
			.ToList();

		foreach (Homework homework in items)
		{
			DockPanel dockPanel = new DockPanel
			{
				Margin = new Thickness(8.0),
				Tag = homework.Id,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				LastChildFill = true
			};
			StackPanel stackPanel = new StackPanel
			{
				Orientation = Orientation.Horizontal
			};
			DockPanel.SetDock(stackPanel, Dock.Right);
			if (!showHistory)
			{
				stackPanel.Children.Add(ActionButton(Localizer.Text(storage, "Edit"), () =>
				{
					Edit(homework.Id);
					return Task.CompletedTask;
				}));
			}
			if (!showHistory)
			{
				stackPanel.Children.Add(ActionButton(Localizer.Text(storage, "Done"), async () =>
				{
					await Archive(homework.Id, "completed");
				}));
			}
			if (showHistory)
			{
				stackPanel.Children.Add(ActionButton(Localizer.Text(storage, "Restore"), async () =>
				{
					await Restore(homework.Id);
				}));
			}
			stackPanel.Children.Add(ActionButton(Localizer.Text(storage, "Delete"), async () =>
			{
				await Delete(homework.Id);
			}));
			dockPanel.Children.Add(stackPanel);
			dockPanel.Children.Add(new TextBlock
			{
				Text = $"{Localizer.SubjectName(storage, homework.SubjectId, homework.SubjectName)}  {(homework.NoSubmissionRequired ? Localizer.Text(storage, "NoSubmissionRequired") : homework.DueAt.ToString("MM-dd HH:mm"))}\n{homework.Content}",
				FontSize = 18.0,
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0.0, 0.0, 10.0, 0.0)
			});
			HomeworkList.Items.Add(new ListBoxItem
			{
				Content = dockPanel,
				Tag = homework.Id,
				Padding = new Thickness(8.0),
				Background = Brushes.Transparent,
				HorizontalContentAlignment = HorizontalAlignment.Stretch
			});
		}
	}

	private void ClearListItems()
	{
		foreach (ListBoxItem item in HomeworkList.Items.OfType<ListBoxItem>())
		{
			item.Content = null;
		}
		HomeworkList.Items.Clear();
	}

	private Button ActionButton(string text, Func<Task> action)
	{
		Button button = new Button
		{
			Content = text,
			MinWidth = 64.0,
			Height = 40.0,
			Margin = new Thickness(4.0, 0.0, 0.0, 0.0),
			Background = new SolidColorBrush(Color.FromRgb(245, 245, 245))
			{
				Opacity = Math.Clamp(storage.Data.Settings.Opacity, 0.35, 1.0)
			}
		};
		button.Click += async (_, _) =>
		{
			await action();
		};
		return button;
	}

	private void Active_Click(object sender, RoutedEventArgs e)
	{
		MemoryOptimizer.AfterUserAction();
		ExitBatchMode();
		showHistory = false;
		Render();
	}

	private void History_Click(object sender, RoutedEventArgs e)
	{
		MemoryOptimizer.AfterUserAction();
		ExitBatchMode();
		showHistory = true;
		Render();
	}

	private void Edit(string id)
	{
		AddEditHomeworkWindow window = new AddEditHomeworkWindow(storage, id, () =>
		{
			Render();
			refreshMain();
		})
		{
			Owner = this
		};

		if (window.ShowDialog() == true)
		{
			Render();
			refreshMain();
		}
	}

	private async Task Archive(string id, string reason)
	{
		Homework homework = storage.Data.Homeworks.FirstOrDefault((Homework h) => h.Id == id);
		if (homework != null)
		{
			CaptureUndoSnapshot();
			homework.Status = "archived";
			homework.ArchivedAt = DateTime.Now;
			homework.ArchiveReason = reason;
			await storage.SaveAsync();
			Render();
			refreshMain();
		}
	}

	private async Task Restore(string id)
	{
		Homework homework = storage.Data.Homeworks.FirstOrDefault((Homework h) => h.Id == id);
		if (homework != null)
		{
			if (WouldCreateDuplicateOnRestore([homework]))
			{
				ShowDuplicateRestoreMessage();
				return;
			}
			CaptureUndoSnapshot();
			homework.Status = "active";
			homework.ArchivedAt = null;
			homework.ArchiveReason = null;
			await storage.SaveAsync();
			Render();
			refreshMain();
		}
	}

	private async Task Delete(string id)
	{
		if (MessageBox.Show(Localizer.Text(storage, "ConfirmDeleteSelected"), Localizer.Text(storage, "DeleteHomework"), MessageBoxButton.YesNo, MessageBoxImage.Exclamation) == MessageBoxResult.Yes)
		{
			CaptureUndoSnapshot();
			storage.DeleteHomework(id);
			await storage.SaveAsync();
			Render();
			refreshMain();
		}
	}

	private async void BatchArchive_Click(object sender, RoutedEventArgs e)
	{
		MemoryOptimizer.AfterUserAction();
		if (!EnsureBatchReady("archive"))
		{
			return;
		}
		List<Homework> selected = SelectedHomeworks().ToList();
		if (selected.Count == 0)
		{
			return;
		}
		CaptureUndoSnapshot();
		foreach (Homework homework in selected)
		{
			homework.Status = "archived";
			homework.ArchivedAt = DateTime.Now;
			homework.ArchiveReason = "manual";
		}
		await storage.SaveAsync();
		ExitBatchMode();
		Render();
		refreshMain();
	}

	private async void BatchRestore_Click(object sender, RoutedEventArgs e)
	{
		MemoryOptimizer.AfterUserAction();
		if (!EnsureBatchReady("restore"))
		{
			return;
		}
		List<Homework> selected = SelectedHomeworks().ToList();
		if (selected.Count == 0)
		{
			return;
		}
		if (WouldCreateDuplicateOnRestore(selected))
		{
			ShowDuplicateRestoreMessage();
			return;
		}
		CaptureUndoSnapshot();
		foreach (Homework homework in selected)
		{
			homework.Status = "active";
			homework.ArchivedAt = null;
			homework.ArchiveReason = null;
		}
		await storage.SaveAsync();
		ExitBatchMode();
		Render();
		refreshMain();
	}

	private bool WouldCreateDuplicateOnRestore(IEnumerable<Homework> restoring)
	{
		HashSet<string> restoringIds = restoring.Select(h => h.Id).ToHashSet();
		HashSet<HomeworkDuplicateKey> publishedKeys = storage.Data.Homeworks
			.Where(h => h.Status == "active" && !restoringIds.Contains(h.Id))
			.Select(DuplicateKey)
			.ToHashSet();

		foreach (Homework homework in restoring)
		{
			if (!publishedKeys.Add(DuplicateKey(homework)))
			{
				return true;
			}
		}

		return false;
	}

	private static HomeworkDuplicateKey DuplicateKey(Homework homework)
	{
		return new HomeworkDuplicateKey(homework.SubjectId, homework.Content.Trim().ToUpper(), homework.DueAt, homework.NoSubmissionRequired);
	}

	private void ShowDuplicateRestoreMessage()
	{
		MessageBox.Show(Localizer.Text(storage, "DuplicateHomework"), Localizer.Text(storage, "Restore"), MessageBoxButton.OK, MessageBoxImage.Warning);
	}

	private readonly record struct HomeworkDuplicateKey(string SubjectId, string Content, DateTime DueAt, bool NoSubmissionRequired);

	private async void BatchDelete_Click(object sender, RoutedEventArgs e)
	{
		MemoryOptimizer.AfterUserAction();
		if (!EnsureBatchReady("delete"))
		{
			return;
		}
		List<string> list = SelectedIds().ToList();
		if (list.Count == 0 || MessageBox.Show(Localizer.Format(storage, "ConfirmDeleteCount", list.Count), Localizer.Text(storage, "BatchDelete"), MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
		{
			return;
		}
		CaptureUndoSnapshot();
		foreach (string item in list)
		{
			storage.DeleteHomework(item);
		}
		await storage.SaveAsync();
		ExitBatchMode();
		Render();
		refreshMain();
	}

	private bool EnsureBatchReady(string action)
	{
		if (pendingBatchAction != action)
		{
			pendingBatchAction = action;
			HomeworkList.SelectionMode = SelectionMode.Multiple;
			HomeworkList.SelectedItems.Clear();
			UpdateBatchButtons();
			return false;
		}
		return true;
	}

	private void ExitBatchMode()
	{
		pendingBatchAction = null;
		if (HomeworkList.SelectionMode != SelectionMode.Single)
		{
			HomeworkList.SelectedItems.Clear();
		}
		else
		{
			HomeworkList.SelectedItem = null;
		}
		HomeworkList.SelectionMode = SelectionMode.Extended;
		UpdateBatchButtons();
	}

	private void UpdateBatchButtons()
	{
		FooterBatchArchiveText.Text = BatchButtonText("archive", Localizer.Text(storage, "BatchArchive"));
		FooterBatchRestoreText.Text = BatchButtonText("restore", Localizer.Text(storage, "BatchRestore"));
		FooterBatchDeleteText.Text = BatchButtonText("delete", Localizer.Text(storage, "BatchDelete"));
	}

	private string BatchButtonText(string action, string normalText)
	{
		if (pendingBatchAction != action)
		{
			return normalText;
		}
		if (Localizer.IsEnglish(storage.Data.Settings.Language))
		{
			return "Apply " + normalText;
		}
		return "执行" + normalText;
	}

	private IEnumerable<string> SelectedIds()
	{
		return from i in HomeworkList.SelectedItems.OfType<ListBoxItem>()
			select (string)i.Tag;
	}

	private IEnumerable<Homework> SelectedHomeworks()
	{
		HashSet<string> selectedIds = SelectedIds().ToHashSet();
		return storage.Data.Homeworks.Where((Homework h) => selectedIds.Contains(h.Id));
	}

	private void EnableTouchButtonClicks()
	{
		AddHandler(UIElement.PreviewTouchDownEvent, new EventHandler<TouchEventArgs>(Button_PreviewTouchDown), handledEventsToo: true);
		AddHandler(UIElement.PreviewTouchMoveEvent, new EventHandler<TouchEventArgs>(Button_PreviewTouchMove), handledEventsToo: true);
		AddHandler(UIElement.PreviewTouchUpEvent, new EventHandler<TouchEventArgs>(Button_PreviewTouchUp), handledEventsToo: true);
	}

	private void Button_PreviewTouchDown(object? sender, TouchEventArgs e)
	{
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
		if (touchClickButton != null)
		{
			Point position = e.GetTouchPoint(this).Position;
			if (Math.Abs(position.X - touchClickStart.X) > 12.0 || Math.Abs(position.Y - touchClickStart.Y) > 12.0)
			{
				touchClickButton = null;
				e.TouchDevice.Capture(null);
			}
			e.Handled = true;
		}
	}

	private void Button_PreviewTouchUp(object? sender, TouchEventArgs e)
	{
		if (touchClickButton != null)
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

	private static void ApplyBrushOpacity(DependencyObject target, DependencyProperty property, double opacity)
	{
		if (target.GetValue(property) is SolidColorBrush solidColorBrush)
		{
			SolidColorBrush brush = solidColorBrush.Clone();
			brush.Opacity = Math.Clamp(opacity, 0.35, 1.0);
			target.SetValue(property, brush);
		}
	}

}
