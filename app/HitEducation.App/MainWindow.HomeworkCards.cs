using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace HitEducation.App;

public partial class MainWindow
{
	private int deferredHomeworkRefreshGeneration;

	private void RenderHomeworks()
	{
		RenderHomeworks(null);
	}

	private void RenderHomeworks(Dictionary<string, Point>? previousCardPositions)
	{
		storage.ArchiveExpired();
		List<Homework> items = storage.GetActiveHomeworks().ToList();
		ClearHomeworkCards();
		HomeworkItems.Items.Clear();
		CountText.Text = Localizer.Format(storage, "PendingCount", items.Count);
		EmptyToolbar.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
		HomeworkScroll.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
		foreach (Homework homework in items)
		{
			HomeworkItems.Items.Add(CreateHomeworkCard(homework));
		}
		UpdateHomeworkCardWidths();
		AnimateHomeworkReflow(previousCardPositions);
		MemoryOptimizer.TrimAfterDelay(1200, force: true);
	}

	private void ClearHomeworkCards()
	{
		foreach (Border card in HomeworkItems.Items.OfType<Border>())
		{
			card.BeginAnimation(UIElement.OpacityProperty, null);
			card.BeginAnimation(FrameworkElement.HeightProperty, null);
			card.BeginAnimation(FrameworkElement.MarginProperty, null);
			if (card.RenderTransform is TranslateTransform transform)
			{
				transform.BeginAnimation(TranslateTransform.XProperty, null);
				transform.BeginAnimation(TranslateTransform.YProperty, null);
			}
			card.Child = null;
		}
	}

	private Dictionary<string, Point> CaptureHomeworkCardPositions()
	{
		Dictionary<string, Point> positions = new();
		foreach (Border card in HomeworkItems.Items.OfType<Border>())
		{
			if (card.Tag is string key)
			{
				positions[key] = card.TranslatePoint(new Point(0.0, 0.0), HomeworkItems);
			}
		}
		return positions;
	}

	private void AnimateHomeworkReflow(Dictionary<string, Point>? previousCardPositions)
	{
		if (previousCardPositions == null || previousCardPositions.Count == 0)
		{
			return;
		}
		HomeworkItems.UpdateLayout();
		var duration = new Duration(TimeSpan.FromMilliseconds(HomeworkReflowAnimationMilliseconds));
		var easing = new CubicEase
		{
			EasingMode = EasingMode.EaseOut
		};
		foreach (Border card in HomeworkItems.Items.OfType<Border>())
		{
			if (card.Tag is not string key || !previousCardPositions.TryGetValue(key, out Point oldPosition))
			{
				continue;
			}
			Point newPosition = card.TranslatePoint(new Point(0.0, 0.0), HomeworkItems);
			double deltaX = oldPosition.X - newPosition.X;
			double deltaY = oldPosition.Y - newPosition.Y;
			if (Math.Abs(deltaX) < 0.5 && Math.Abs(deltaY) < 0.5)
			{
				continue;
			}
			if (card.RenderTransform is not TranslateTransform transform)
			{
				transform = new TranslateTransform();
				card.RenderTransform = transform;
			}
			transform.BeginAnimation(TranslateTransform.XProperty, null);
			transform.BeginAnimation(TranslateTransform.YProperty, null);
			transform.X = deltaX;
			transform.Y = deltaY;
			transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
			{
				To = 0.0,
				Duration = duration,
				EasingFunction = easing
			});
			transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
			{
				To = 0.0,
				Duration = duration,
				EasingFunction = easing
			});
		}
	}

	private Border CreateHomeworkCard(Homework homework)
	{
		DateTime dueAt = homework.DueAt;
		double minutesLeft = (dueAt - DateTime.Now).TotalMinutes;
		bool isOverdue = !homework.NoSubmissionRequired && minutesLeft <= 0.0;
		double urgency = homework.NoSubmissionRequired ? 0.0 : Math.Clamp(1.0 - minutesLeft / Math.Max(1, storage.Data.Settings.RemindMinutesBefore), 0.0, 1.0);
		System.Windows.Media.Color background = homework.NoSubmissionRequired
			? System.Windows.Media.Color.FromRgb(241, 245, 249)
			: isOverdue
				? System.Windows.Media.Color.FromRgb(226, 232, 240)
				: Interpolate(System.Windows.Media.Color.FromRgb(255, 247, 237), System.Windows.Media.Color.FromRgb(254, 226, 226), urgency);
		Subject subject = storage.Data.Subjects.FirstOrDefault((Subject s) => s.Id == homework.SubjectId);
		Border card = new Border
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0),
			Padding = new Thickness(12.0),
			Background = new SolidColorBrush(background),
			BorderBrush = new SolidColorBrush(subject?.GetColor() ?? System.Windows.Media.Color.FromRgb(100, 116, 139)),
			BorderThickness = new Thickness(0.0, 0.0, 0.0, 3.0),
			CornerRadius = new CornerRadius(8.0),
			Tag = homework.Id,
			RenderTransform = new TranslateTransform()
		};
		ApplyBrushOpacity(card, Border.BackgroundProperty, storage.Data.Settings.Opacity);

		Grid root = new Grid();
		root.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		root.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		StackPanel textStack = new StackPanel();
		TextBlock contentText = new TextBlock
		{
			Text = homework.Content,
			TextWrapping = TextWrapping.Wrap,
			FontSize = storage.Data.Settings.FontSize,
			FontWeight = FontWeights.SemiBold,
			Foreground = TextBrush(System.Windows.Media.Color.FromRgb(23, 32, 51))
		};
		textStack.Children.Add(contentText);
		TextBlock dueText = new TextBlock
		{
			Text = (homework.NoSubmissionRequired ? (Localizer.SubjectName(storage, homework.SubjectId, homework.SubjectName) + " · " + Localizer.Text(storage, "NoSubmissionRequired")) : $"{Localizer.SubjectName(storage, homework.SubjectId, homework.SubjectName)} · {dueAt:MM-dd HH:mm}"),
			Margin = new Thickness(0.0, 8.0, 0.0, 0.0),
			FontSize = 14.0,
			Foreground = TextBrush(System.Windows.Media.Color.FromRgb(102, 112, 133))
		};
		textStack.Children.Add(dueText);
		Grid.SetColumn(textStack, 0);
		root.Children.Add(textStack);
		double buttonFontSize = FloatingButtonFontSize(storage.Data.Settings.FontSize);
		double buttonHeight = FloatingButtonHeight(buttonFontSize);
		StackPanel actions = new StackPanel
		{
			Orientation = Orientation.Vertical,
			Margin = new Thickness(10.0, 0.0, 0.0, 0.0)
		};
		Button done = new Button
		{
			Content = Localizer.Text(storage, "Done"),
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0),
			Background = ButtonBackgroundBrush()
		};
		ApplyFloatingButtonMetrics(done, buttonFontSize, buttonHeight, 64.0);
		done.Click += async (_, _) =>
		{
			await CompleteHomeworkAsync(homework.Id);
		};
		Button edit = new Button
		{
			Content = Localizer.Text(storage, "Edit"),
			Background = ButtonBackgroundBrush()
		};
		ApplyFloatingButtonMetrics(edit, buttonFontSize, buttonHeight, 64.0);
		edit.Click += (_, _) =>
		{
			OpenEditWindow(homework.Id);
		};
		actions.Children.Add(done);
		actions.Children.Add(edit);
		Grid.SetColumn(actions, 1);
		root.Children.Add(actions);
		card.Child = root;
		return card;
	}

	private void HomeworkScroll_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		ScheduleHomeworkCardWidthUpdate();
	}

	private void HomeworkScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
	{
		if (Math.Abs(e.ViewportWidthChange) > 0.1)
		{
			ScheduleHomeworkCardWidthUpdate();
		}
	}

	private void HomeworkItems_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		ScheduleHomeworkCardWidthUpdate();
	}

	private void ScheduleHomeworkCardWidthUpdate()
	{
		UpdateHomeworkCardWidths();
		Dispatcher.BeginInvoke(UpdateHomeworkCardWidths, DispatcherPriority.Loaded);
	}

	private void UpdateHomeworkCardWidths()
	{
		double viewportWidth = HomeworkScroll.ViewportWidth;
		if (double.IsNaN(viewportWidth) || viewportWidth <= 0.0)
		{
			viewportWidth = HomeworkScroll.ActualWidth;
		}
		double availableWidth = viewportWidth - HomeworkItems.Margin.Left - HomeworkItems.Margin.Right;
		if (availableWidth <= 0.0)
		{
			return;
		}
		if (double.IsNaN(HomeworkItems.Width) || Math.Abs(HomeworkItems.Width - availableWidth) > 0.1)
		{
			HomeworkItems.Width = availableWidth;
		}
		bool useTwoColumns = availableWidth >= TwoColumnHomeworkWidth;
		double cardWidth = useTwoColumns
			? Math.Max(0.0, (availableWidth - HomeworkColumnGap * 2.0 - HomeworkLayoutSlack) / 2.0)
			: Math.Max(0.0, availableWidth - HomeworkLayoutSlack);
		Thickness margin = useTwoColumns
			? new Thickness(0.0, 0.0, HomeworkColumnGap, 10.0)
			: new Thickness(0.0, 0.0, 0.0, 10.0);
		foreach (Border card in HomeworkItems.Items.OfType<Border>())
		{
			card.Width = cardWidth;
			card.Margin = margin;
		}
	}

	private async Task<Dictionary<string, Point>> AnimateHomeworkRemovalAsync(IEnumerable<string> ids)
	{
		HashSet<string> idSet = ids.ToHashSet();
		Dictionary<string, Point> previousCardPositions = CaptureHomeworkCardPositions();
		List<Border> cards = (from card in HomeworkItems.Items.OfType<Border>()
			where card.Tag is string value && idSet.Contains(value)
			select card).ToList();
		if (cards.Count == 0)
		{
			return previousCardPositions;
		}
		var duration = new Duration(TimeSpan.FromMilliseconds(HomeworkRemoveAnimationMilliseconds));
		var easing = new CubicEase
		{
			EasingMode = EasingMode.EaseInOut
		};
		foreach (Border card in cards)
		{
			if (card.RenderTransform == null)
			{
				card.RenderTransform = new TranslateTransform();
			}
			if (card.RenderTransform is not TranslateTransform transform)
			{
				transform = new TranslateTransform();
				card.RenderTransform = transform;
			}
			transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
			{
				To = Math.Max(card.ActualWidth, HomeworkScroll.ActualWidth) + 32.0,
				Duration = duration,
				EasingFunction = easing
			});
			card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
			{
				To = 0.0,
				Duration = duration,
				EasingFunction = easing
			});
		}
		await Task.Delay(HomeworkRemoveAnimationMilliseconds + 40);
		return previousCardPositions;
	}

	private void RemoveHomeworkCardsFromView(IEnumerable<string> ids, Dictionary<string, Point> previousCardPositions, bool refreshAfterReflow)
	{
		HashSet<string> idSet = ids.ToHashSet();
		foreach (Border card in HomeworkItems.Items.OfType<Border>().Where(card => card.Tag is string value && idSet.Contains(value)).ToList())
		{
			HomeworkItems.Items.Remove(card);
		}
		int count = storage.GetActiveHomeworks().Count();
		CountText.Text = Localizer.Format(storage, "PendingCount", count);
		EmptyToolbar.Visibility = ((count != 0) ? Visibility.Collapsed : Visibility.Visible);
		HomeworkScroll.Visibility = ((count == 0) ? Visibility.Collapsed : Visibility.Visible);
		UpdateHomeworkCardWidths();
		AnimateHomeworkReflow(previousCardPositions);
		MemoryOptimizer.TrimAfterDelay(1200, force: true);
		if (refreshAfterReflow)
		{
			ScheduleHomeworkRefreshAfterReflow();
		}
	}

	private void ScheduleHomeworkRefreshAfterReflow()
	{
		int generation = ++deferredHomeworkRefreshGeneration;
		_ = RefreshHomeworksAfterReflowAsync(generation);
	}

	private async Task RefreshHomeworksAfterReflowAsync(int generation)
	{
		await Task.Delay(HomeworkReflowAnimationMilliseconds + 80);
		if (generation != deferredHomeworkRefreshGeneration || !IsLoaded)
		{
			return;
		}
		RenderHomeworks();
	}

	private static System.Windows.Media.Color Interpolate(System.Windows.Media.Color start, System.Windows.Media.Color end, double amount)
	{
		return System.Windows.Media.Color.FromRgb(Blend(start.R, end.R), Blend(start.G, end.G), Blend(start.B, end.B));
		byte Blend(byte a, byte b)
		{
			return (byte)((double)(int)a + (double)(b - a) * amount);
		}
	}

	private SolidColorBrush TextBrush(System.Windows.Media.Color color)
	{
		return new SolidColorBrush(color)
		{
			Opacity = Math.Clamp(storage.Data.Settings.FontOpacity, 0.35, 1.0)
		};
	}

	private SolidColorBrush ButtonBackgroundBrush()
	{
		return new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 245, 245))
		{
			Opacity = Math.Clamp(storage.Data.Settings.Opacity, 0.35, 1.0)
		};
	}

	private async Task CompleteHomeworkAsync(string id)
	{
		Dictionary<string, Point> previousCardPositions = await AnimateHomeworkRemovalAsync(new[] { id });
		storage.CompleteHomework(id);
		await storage.SaveAsync();
		RemoveHomeworkCardsFromView(new[] { id }, previousCardPositions, refreshAfterReflow: true);
	}
}
