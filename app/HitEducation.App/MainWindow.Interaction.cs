using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace HitEducation.App;

public partial class MainWindow
{
	private async void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		lastUserActivity = DateTime.Now;
		if (e.ClickCount == 2)
		{
			storage.Data.Settings.AlwaysOnTop = !storage.Data.Settings.AlwaysOnTop;
			await storage.SaveAsync();
			ApplySettings();
		}
		else if (!storage.Data.Settings.LockWindow)
		{
			DragMove();
		}
	}

	private void HeaderBar_TouchDown(object sender, TouchEventArgs e)
	{
		lastUserActivity = DateTime.Now;
		if (IsInteractiveSource(e.OriginalSource) || storage.Data.Settings.LockWindow)
		{
			return;
		}

		touchDragStart = GetTouchScreenPoint(e);
		touchDragStartLeft = Left;
		touchDragStartTop = Top;
		e.TouchDevice.Capture(HeaderBar);
		e.Handled = true;
	}

	private void HeaderBar_TouchMove(object sender, TouchEventArgs e)
	{
		if (touchDragStart == null || storage.Data.Settings.LockWindow)
		{
			return;
		}

		Point touchPoint = GetTouchScreenPoint(e);
		Left = touchDragStartLeft + touchPoint.X - touchDragStart.Value.X;
		Top = touchDragStartTop + touchPoint.Y - touchDragStart.Value.Y;
		e.Handled = true;
	}

	private async void HeaderBar_TouchUp(object sender, TouchEventArgs e)
	{
		if (touchDragStart == null)
		{
			return;
		}

		touchDragStart = null;
		e.TouchDevice.Capture(null);
		ClampToWorkArea();
		await SaveWindowAsync();
		e.Handled = true;
	}

	private void HeaderBar_LostTouchCapture(object sender, TouchEventArgs e)
	{
		touchDragStart = null;
	}

	private static bool IsInteractiveSource(object source)
	{
		for (DependencyObject? current = source as DependencyObject; current != null; current = VisualTreeHelper.GetParent(current))
		{
			if (current is ButtonBase or TextBoxBase or ComboBox or ScrollBar)
			{
				return true;
			}
		}
		return false;
	}

	private Point GetTouchScreenPoint(TouchEventArgs e)
	{
		Point screenPoint = PointToScreen(e.GetTouchPoint(this).Position);
		PresentationSource? source = PresentationSource.FromVisual(this);
		return source?.CompositionTarget?.TransformFromDevice.Transform(screenPoint) ?? screenPoint;
	}

	private void HeaderButton_PreviewTouchDown(object sender, TouchEventArgs e)
	{
		lastUserActivity = DateTime.Now;
		headerTouchButton = sender as Button;
		headerTouchStart = e.GetTouchPoint(this).Position;
		if (sender is UIElement element)
		{
			e.TouchDevice.Capture(element);
		}
		e.Handled = true;
	}

	private void HeaderButton_PreviewTouchMove(object sender, TouchEventArgs e)
	{
		if (headerTouchButton == null)
		{
			return;
		}

		Point touchPoint = e.GetTouchPoint(this).Position;
		if (Math.Abs(touchPoint.X - headerTouchStart.X) > 12.0 || Math.Abs(touchPoint.Y - headerTouchStart.Y) > 12.0)
		{
			headerTouchButton = null;
			e.TouchDevice.Capture(null);
		}
		e.Handled = true;
	}

	private void HeaderButton_PreviewTouchUp(object sender, TouchEventArgs e)
	{
		if (headerTouchButton == null)
		{
			return;
		}

		Button button = headerTouchButton;
		headerTouchButton = null;
		e.TouchDevice.Capture(null);
		if (IsTouchInside(e, button))
		{
			InvokeHeaderButton(button);
		}
		e.Handled = true;
	}

	private void InvokeHeaderButton(Button button)
	{
		if (button == AddButton)
		{
			OpenAddWindow();
		}
		else if (button == SettingsButton)
		{
			OpenSettingsWindow();
		}
		else if (button == HideButton)
		{
			_ = HideAllWindowsAsync();
		}
		else if (button == MoreButton)
		{
			OpenMoreMenu();
		}
		else if (button == EmptyAddButton)
		{
			OpenAddWindow();
		}
	}

	private void BeginResize(int direction)
	{
		if (storage.Data.Settings.LockWindow)
		{
			return;
		}

		nint handle = new WindowInteropHelper(this).Handle;
		if (handle == IntPtr.Zero)
		{
			return;
		}

		ReleaseCapture();
		SendMessage(handle, WmSysCommand, ScSize + direction, IntPtr.Zero);
	}

	private void ResizeLeft_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		BeginResize(ResizeLeft);
	}

	private void ResizeRight_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		BeginResize(ResizeRight);
	}

	private void ResizeTop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		BeginResize(ResizeTop);
	}

	private void ResizeTopLeft_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		BeginResize(ResizeTopLeft);
	}

	private void ResizeTopRight_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		BeginResize(ResizeTopRight);
	}

	private void ResizeBottom_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		BeginResize(ResizeBottom);
	}

	private void ResizeBottomLeft_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		BeginResize(ResizeBottomLeft);
	}

	private void ResizeBottomRight_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		BeginResize(ResizeBottomRight);
	}

	private void BeginTouchResize(int direction, TouchEventArgs e)
	{
		if (TryGetHeaderButtonAtTouch(e, out Button button))
		{
			touchResizeForwardButton = button;
			touchResizeForwardStart = e.GetTouchPoint(this).Position;
			if (e.Source is UIElement hitElement)
			{
				e.TouchDevice.Capture(hitElement);
			}
			e.Handled = true;
			return;
		}

		if (storage.Data.Settings.LockWindow)
		{
			return;
		}

		touchResizeDirection = direction;
		touchResizeStart = GetTouchScreenPoint(e);
		touchResizeStartLeft = Left;
		touchResizeStartTop = Top;
		touchResizeStartWidth = Width;
		touchResizeStartHeight = Height;
		if (e.Source is UIElement element)
		{
			e.TouchDevice.Capture(element);
		}
		e.Handled = true;
	}

	private void Resize_TouchMove(object sender, TouchEventArgs e)
	{
		if (touchResizeForwardButton != null)
		{
			Point touchPoint = e.GetTouchPoint(this).Position;
			if (Math.Abs(touchPoint.X - touchResizeForwardStart.X) > 12.0 || Math.Abs(touchPoint.Y - touchResizeForwardStart.Y) > 12.0)
			{
				touchResizeForwardButton = null;
				e.TouchDevice.Capture(null);
			}
			e.Handled = true;
			return;
		}
		if (touchResizeDirection == null || storage.Data.Settings.LockWindow)
		{
			return;
		}

		Point point = GetTouchScreenPoint(e);
		double deltaX = point.X - touchResizeStart.X;
		double deltaY = point.Y - touchResizeStart.Y;
		double left = touchResizeStartLeft;
		double top = touchResizeStartTop;
		double width = touchResizeStartWidth;
		double height = touchResizeStartHeight;
		int direction = touchResizeDirection.Value;

		if (direction == ResizeLeft || direction == ResizeTopLeft || direction == ResizeBottomLeft)
		{
			width = Math.Max(MinWidth, touchResizeStartWidth - deltaX);
			left = touchResizeStartLeft + touchResizeStartWidth - width;
		}
		if (direction == ResizeRight || direction == ResizeTopRight || direction == ResizeBottomRight)
		{
			width = Math.Max(MinWidth, touchResizeStartWidth + deltaX);
		}
		if (direction == ResizeTop || direction == ResizeTopLeft || direction == ResizeTopRight)
		{
			height = Math.Max(MinHeight, touchResizeStartHeight - deltaY);
			top = touchResizeStartTop + touchResizeStartHeight - height;
		}
		if (direction == ResizeBottom || direction == ResizeBottomLeft || direction == ResizeBottomRight)
		{
			height = Math.Max(MinHeight, touchResizeStartHeight + deltaY);
		}

		Left = left;
		Top = top;
		Width = width;
		Height = height;
		Window_StateChanged(sender, EventArgs.Empty);
		e.Handled = true;
	}

	private async void Resize_TouchUp(object sender, TouchEventArgs e)
	{
		if (touchResizeForwardButton != null)
		{
			Button button = touchResizeForwardButton;
			touchResizeForwardButton = null;
			e.TouchDevice.Capture(null);
			if (IsTouchInside(e, button))
			{
				button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
			}
			e.Handled = true;
			return;
		}

		touchResizeDirection = null;
		e.TouchDevice.Capture(null);
		await SaveWindowAsync();
		e.Handled = true;
	}

	private void Resize_LostTouchCapture(object sender, TouchEventArgs e)
	{
		touchResizeDirection = null;
		touchResizeForwardButton = null;
	}

	private bool TryGetHeaderButtonAtTouch(TouchEventArgs e, out Button? button)
	{
		foreach (Button candidate in new[] { AddButton, SettingsButton, HideButton, MoreButton })
		{
			if (IsTouchInside(e, candidate))
			{
				button = candidate;
				return true;
			}
		}
		button = null;
		return false;
	}

	private bool IsTouchInsideHeaderButtons(TouchEventArgs e)
	{
		return IsTouchInside(e, HeaderButtonsPanel);
	}

	private static bool IsTouchInside(TouchEventArgs e, FrameworkElement element)
	{
		if (!element.IsVisible || !element.IsEnabled)
		{
			return false;
		}

		Point point = e.GetTouchPoint(element).Position;
		return point.X >= 0.0 && point.Y >= 0.0 && point.X <= element.ActualWidth && point.Y <= element.ActualHeight;
	}

	private void ResizeLeft_TouchDown(object sender, TouchEventArgs e)
	{
		BeginTouchResize(ResizeLeft, e);
	}

	private void ResizeRight_TouchDown(object sender, TouchEventArgs e)
	{
		BeginTouchResize(ResizeRight, e);
	}

	private void ResizeTop_TouchDown(object sender, TouchEventArgs e)
	{
		if (!IsTouchInsideHeaderButtons(e))
		{
			BeginTouchResize(ResizeTop, e);
		}
	}

	private void ResizeTopLeft_TouchDown(object sender, TouchEventArgs e)
	{
		BeginTouchResize(ResizeTopLeft, e);
	}

	private void ResizeTopRight_TouchDown(object sender, TouchEventArgs e)
	{
		if (!IsTouchInsideHeaderButtons(e))
		{
			BeginTouchResize(ResizeTopRight, e);
		}
	}

	private void ResizeBottom_TouchDown(object sender, TouchEventArgs e)
	{
		BeginTouchResize(ResizeBottom, e);
	}

	private void ResizeBottomLeft_TouchDown(object sender, TouchEventArgs e)
	{
		BeginTouchResize(ResizeBottomLeft, e);
	}

	private void ResizeBottomRight_TouchDown(object sender, TouchEventArgs e)
	{
		BeginTouchResize(ResizeBottomRight, e);
	}

	private void Window_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
	{
		if (storage.Data.Settings.LockWindow || e.Manipulators.Count() < 2)
		{
			return;
		}

		Vector scale = e.DeltaManipulation.Scale;
		Width = Math.Clamp(Width * scale.X, MinWidth, 900.0);
		Height = Math.Clamp(Height * scale.Y, MinHeight, 900.0);
		Window_StateChanged(sender, EventArgs.Empty);
		e.Handled = true;
	}

	private void HomeworkScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		lastUserActivity = DateTime.Now;
	}

	private void HomeworkScroll_TouchActivity(object sender, TouchEventArgs e)
	{
		lastUserActivity = DateTime.Now;
	}

	private void AutoScrollIfIdle()
	{
		int idleSeconds = storage.Data.Settings.AutoScrollIdleSeconds;
		if (idleSeconds < 0)
		{
			idleSeconds = DefaultAutoScrollIdleSeconds;
		}
		idleSeconds = Math.Clamp(idleSeconds, 0, 600);
		if ((DateTime.Now - lastUserActivity).TotalSeconds < idleSeconds || HomeworkScroll.Visibility != Visibility.Visible || HomeworkScroll.ScrollableHeight <= 0.0)
		{
			return;
		}
		if (autoScrollPauseUntil.HasValue)
		{
			if (DateTime.Now < autoScrollPauseUntil.Value)
			{
				return;
			}
			autoScrollPauseUntil = null;
		}
		double speed = Math.Clamp(storage.Data.Settings.AutoScrollSpeed, 0.1, 5.0);
		double nextOffset = HomeworkScroll.VerticalOffset + speed * autoScrollDirection;
		if (nextOffset >= HomeworkScroll.ScrollableHeight)
		{
			HomeworkScroll.ScrollToEnd();
			autoScrollDirection = -1;
			autoScrollPauseUntil = DateTime.Now.AddSeconds(Math.Clamp(storage.Data.Settings.AutoScrollEdgePauseSeconds, 0, 30));
		}
		else if (nextOffset <= 0.0)
		{
			HomeworkScroll.ScrollToTop();
			autoScrollDirection = 1;
			autoScrollPauseUntil = DateTime.Now.AddSeconds(Math.Clamp(storage.Data.Settings.AutoScrollEdgePauseSeconds, 0, 30));
		}
		else
		{
			HomeworkScroll.ScrollToVerticalOffset(nextOffset);
		}
	}

	private void AddButton_Click(object sender, RoutedEventArgs e)
	{
		OpenAddWindow();
	}

	private void ListButton_Click(object sender, RoutedEventArgs e)
	{
		OpenListWindow();
	}

	private void SettingsButton_Click(object sender, RoutedEventArgs e)
	{
		OpenSettingsWindow();
	}

	private void HideButton_Click(object sender, RoutedEventArgs e)
	{
		_ = HideAllWindowsAsync();
	}

	private void MoreButton_Click(object sender, RoutedEventArgs e)
	{
		OpenMoreMenu();
	}

	private void OpenMoreMenu()
	{
		MemoryOptimizer.AfterUserAction();
		var menu = new ContextMenu
		{
			PlacementTarget = MoreButton,
			Placement = PlacementMode.Bottom
		};
		menu.Items.Add(MenuItem(Localizer.Text(storage, "List"), () =>
		{
			OpenListWindow();
			return Task.CompletedTask;
		}));
		menu.Items.Add(MenuItem(Localizer.Text(storage, storage.Data.Settings.LockWindow ? "UnlockWindow" : "LockWindow"), async () =>
		{
			storage.Data.Settings.LockWindow = !storage.Data.Settings.LockWindow;
			await storage.SaveAsync();
		}));
		menu.Items.Add(MenuItem(Localizer.Text(storage, storage.Data.Settings.AlwaysOnTop ? "DisableTopmost" : "KeepTopmost"), async () =>
		{
			storage.Data.Settings.AlwaysOnTop = !storage.Data.Settings.AlwaysOnTop;
			ApplySettings();
			await storage.SaveAsync();
		}));
		menu.Items.Add(MenuItem(Localizer.Text(storage, "Settings"), () =>
		{
			OpenSettingsWindow();
			return Task.CompletedTask;
		}));
		menu.Items.Add(MenuItem(Localizer.Text(storage, "Exit"), ExitAsync));
		MoreButton.ContextMenu = menu;
		menu.IsOpen = true;
	}

	private static MenuItem MenuItem(string text, Func<Task> action)
	{
		var item = new MenuItem
		{
			Header = text,
			Height = 44.0
		};
		item.Click += async (_, _) =>
		{
			await action();
		};
		return item;
	}
}
