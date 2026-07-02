using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace HitEducation.App;

public partial class MainWindow
{
	private async void OpenAddWindow()
	{
		MemoryOptimizer.AfterUserAction();
		await windowSwitchLock.WaitAsync();
		try
		{
			await CloseMoreWindowAsync();
			await CloseRandomPickerWindowAsync();
			if (homeworkWindow is not null)
			{
				if (!homeworkWindow.IsEditing)
				{
					ShowExistingWindow(homeworkWindow, homeworkWindow.WindowRoot);
					return;
				}

				await CloseHomeworkWindowAsync(saveAddDraft: false);
			}

			await CloseSettingsWindowAsync();
			await CloseListWindowAsync();

			homeworkWindow = new AddEditHomeworkWindow(storage, null, RenderHomeworks)
			{
				Owner = this,
				WindowStartupLocation = WindowStartupLocation.Manual
			};
			homeworkWindow.Closed += (_, _) => homeworkWindow = null;
			ShowNewChildWindow(homeworkWindow);
		}
		finally
		{
			windowSwitchLock.Release();
		}
	}

	private async void OpenEditWindow(string id)
	{
		MemoryOptimizer.AfterUserAction();
		await windowSwitchLock.WaitAsync();
		try
		{
			await CloseHomeworkWindowAsync(saveAddDraft: true);
			await CloseListWindowAsync();
			await CloseMoreWindowAsync();
			await CloseRandomPickerWindowAsync();

			homeworkWindow = new AddEditHomeworkWindow(storage, id, RenderHomeworks)
			{
				Owner = this,
				WindowStartupLocation = WindowStartupLocation.Manual
			};
			homeworkWindow.Closed += (_, _) => homeworkWindow = null;
			ShowNewChildWindow(homeworkWindow);
		}
		finally
		{
			windowSwitchLock.Release();
		}
	}

	private async void OpenListWindow()
	{
		MemoryOptimizer.AfterUserAction();
		await windowSwitchLock.WaitAsync();
		try
		{
			await CloseMoreWindowAsync();
			await CloseRandomPickerWindowAsync();
			if (listWindow is not null)
			{
				ShowExistingWindow(listWindow, listWindow.ListRoot);
				return;
			}

			await CloseHomeworkWindowAsync(saveAddDraft: true);
			await CloseSettingsWindowAsync();

			listWindow = new HomeworkListWindow(storage, RenderHomeworks)
			{
				Owner = this,
				WindowStartupLocation = WindowStartupLocation.Manual
			};
			listWindow.Closed += (_, _) => listWindow = null;
			ShowNewChildWindow(listWindow);
		}
		finally
		{
			windowSwitchLock.Release();
		}
	}

	private async void OpenSettingsWindow()
	{
		MemoryOptimizer.AfterUserAction();
		await windowSwitchLock.WaitAsync();
		try
		{
			await CloseMoreWindowAsync();
			await CloseRandomPickerWindowAsync();
			if (settingsWindow is not null)
			{
				ShowExistingWindow(settingsWindow, settingsWindow.SettingsRoot);
				return;
			}

			await CloseHomeworkWindowAsync(saveAddDraft: true);
			await CloseListWindowAsync();

			settingsWindow = new SettingsWindow(storage, () =>
			{
				ApplySettings();
				listWindow?.RefreshLanguage();
				RenderHomeworks();
			})
			{
				Owner = this,
				WindowStartupLocation = WindowStartupLocation.Manual
			};
			settingsWindow.Closed += (_, _) => settingsWindow = null;
			ShowNewChildWindow(settingsWindow);
		}
		finally
		{
			windowSwitchLock.Release();
		}
	}

	private async void OpenMoreWindow()
	{
		MemoryOptimizer.AfterUserAction();
		await windowSwitchLock.WaitAsync();
		try
		{
			if (moreWindow is not null)
			{
				ShowExistingWindow(moreWindow, moreWindow.MoreRoot);
				return;
			}

			await CloseHomeworkWindowAsync(saveAddDraft: true);
			await CloseSettingsWindowAsync();
			await CloseListWindowAsync();
			await CloseRandomPickerWindowAsync();

			moreWindow = new MoreWindow(storage, OpenAddWindow, OpenListWindow, OpenRandomPickerWindow, OpenSettingsWindow, ToggleLockWindowAsync, ToggleTopmostAsync, ExitAsync)
			{
				Owner = this,
				WindowStartupLocation = WindowStartupLocation.Manual
			};
			moreWindow.Closed += (_, _) => moreWindow = null;
			ShowNewChildWindow(moreWindow);
		}
		finally
		{
			windowSwitchLock.Release();
		}
	}

	private async void OpenRandomPickerWindow()
	{
		MemoryOptimizer.AfterUserAction();
		await windowSwitchLock.WaitAsync();
		try
		{
			if (randomPickerWindow is not null)
			{
				ShowExistingWindow(randomPickerWindow, randomPickerWindow.PickerRoot);
				return;
			}

			await CloseHomeworkWindowAsync(saveAddDraft: true);
			await CloseSettingsWindowAsync();
			await CloseListWindowAsync();
			await CloseMoreWindowAsync();

			randomPickerWindow = new RandomPickerWindow(storage)
			{
				Owner = this,
				WindowStartupLocation = WindowStartupLocation.Manual
			};
			randomPickerWindow.Closed += (_, _) => randomPickerWindow = null;
			ShowNewChildWindow(randomPickerWindow);
		}
		finally
		{
			windowSwitchLock.Release();
		}
	}

	private async Task ToggleLockWindowAsync()
	{
		storage.Data.Settings.LockWindow = !storage.Data.Settings.LockWindow;
		await storage.SaveAsync();
	}

	private async Task ToggleTopmostAsync()
	{
		storage.Data.Settings.AlwaysOnTop = !storage.Data.Settings.AlwaysOnTop;
		ApplySettings();
		await storage.SaveAsync();
	}

	private void PlaceBesideMainWindow(Window window)
	{
		var workArea = SystemParameters.WorkArea;
		FitChildWindowToWorkArea(window, workArea);
		var candidates = new[]
		{
			new Point(Left + Width + 12.0, Top),
			new Point(Left - window.Width - 12.0, Top),
			new Point(Left, Top + Height + 12.0),
			new Point(Left, Top - window.Height - 12.0),
			new Point(workArea.Right - window.Width, workArea.Top),
			new Point(workArea.Left, workArea.Top)
		};
		var mainRect = new Rect(Left, Top, Width, Height);

		foreach (var candidate in candidates)
		{
			var left = Math.Min(Math.Max(workArea.Left, candidate.X), workArea.Right - window.Width);
			var top = Math.Min(Math.Max(workArea.Top, candidate.Y), workArea.Bottom - window.Height);
			var rect = new Rect(left, top, window.Width, window.Height);
			if (workArea.Contains(rect) && !rect.IntersectsWith(mainRect))
			{
				window.Left = left;
				window.Top = top;
				return;
			}
		}

		window.Left = Math.Min(Math.Max(workArea.Left, (workArea.Width - window.Width) / 2.0), workArea.Right - window.Width);
		window.Top = Math.Min(Math.Max(workArea.Top, (workArea.Height - window.Height) / 2.0), workArea.Bottom - window.Height);
	}

	private static void FitChildWindowToWorkArea(Window window, Rect workArea)
	{
		const double margin = 24.0;
		double availableHeight = Math.Max(window.MinHeight, workArea.Height - margin);
		window.MaxHeight = availableHeight;
		if (double.IsNaN(window.Height) || window.Height <= 0.0 || window.Height > availableHeight)
		{
			window.Height = availableHeight;
		}
	}

	private static void ShowExistingWindow(Window window, FrameworkElement animationRoot)
	{
		FitChildWindowToWorkArea(window, SystemParameters.WorkArea);
		if (!window.IsVisible)
		{
			window.Show();
			WindowOpenAnimation.Play(animationRoot);
		}

		if (window.WindowState == WindowState.Minimized)
		{
			window.WindowState = WindowState.Normal;
		}

		window.Activate();
	}

	private void ShowNewChildWindow(Window window)
	{
		PlaceBesideMainWindow(window);
		window.Show();
		window.Activate();
		MemoryOptimizer.TrimAfterDelay(1400, force: true);
	}

	private async void CloseHomeworkWindow(bool saveAddDraft)
	{
		await CloseHomeworkWindowAsync(saveAddDraft);
	}

	private async Task CloseHomeworkWindowAsync(bool saveAddDraft)
	{
		if (homeworkWindow is null)
		{
			return;
		}

		if (saveAddDraft && !homeworkWindow.IsEditing)
		{
			if (homeworkWindow.HasDraftContent())
			{
				await homeworkWindow.SaveDraftAsync();
			}
			else
			{
				AddEditHomeworkWindow.DeleteDraft(storage);
			}
		}
		await homeworkWindow.CloseWithAnimationAsync();
		homeworkWindow = null;
	}

	private async Task CloseSettingsWindowAsync()
	{
		if (settingsWindow is null)
		{
			return;
		}

		await settingsWindow.CloseWithAnimationAsync();
		settingsWindow = null;
	}

	private async Task CloseMoreWindowAsync()
	{
		if (moreWindow is null)
		{
			return;
		}

		await moreWindow.CloseWithAnimationAsync();
		moreWindow = null;
	}

	private async Task CloseRandomPickerWindowAsync()
	{
		if (randomPickerWindow is null)
		{
			return;
		}

		await randomPickerWindow.CloseWithAnimationAsync();
		randomPickerWindow = null;
	}

	private async Task CloseListWindowAsync()
	{
		if (listWindow is null)
		{
			return;
		}

		await listWindow.CloseWithAnimationAsync();
		listWindow = null;
	}

	private void ToggleVisible()
	{
		hiddenForFullscreen = false;
		if (IsVisible)
		{
			_ = HideAllWindowsAsync();
			return;
		}
		Show();
		BringMainWindowToFront();
		WindowOpenAnimation.Play(RootPanel);
	}

	private void BringMainWindowToFront()
	{
		if (WindowState == WindowState.Minimized)
		{
			WindowState = WindowState.Normal;
		}

		Activate();
		Focus();
		if (!storage.Data.Settings.AlwaysOnTop)
		{
			nint handle = new WindowInteropHelper(this).Handle;
			if (handle != IntPtr.Zero)
			{
				SetForegroundWindow(handle);
			}
		}
	}

	private Task HideAllWindowsAsync()
	{
		int generation = Interlocked.Increment(ref hideAllWindowsGeneration);
		hideAllWindowsTask = HideAllWindowsCoreAsync(generation);
		return hideAllWindowsTask;
	}

	private async Task HideAllWindowsCoreAsync(int generation)
	{
		await windowSwitchLock.WaitAsync();
		try
		{
			if (homeworkWindow is not null && !homeworkWindow.IsEditing)
			{
				await homeworkWindow.SaveDraftAsync();
			}

			if (homeworkWindow is not null)
			{
				await homeworkWindow.CloseWithAnimationAsync();
				homeworkWindow = null;
			}

			await CloseSettingsWindowAsync();
			await CloseListWindowAsync();
			await CloseMoreWindowAsync();
			await CloseRandomPickerWindowAsync();

			foreach (var notificationWindow in notificationWindows.ToList())
			{
				await notificationWindow.CloseWithAnimationAsync();
			}

			notificationWindows.Clear();
			await WindowOpenAnimation.PlayCloseAsync(RootPanel);
			if (generation == hideAllWindowsGeneration)
			{
				Hide();
				MemoryOptimizer.TrimSoon();
			}
		}
		finally
		{
			windowSwitchLock.Release();
		}
	}

	private async Task ShowAfterHideAllWindowsAsync()
	{
		Interlocked.Increment(ref hideAllWindowsGeneration);
		Task pendingHide = hideAllWindowsTask;
		if (!pendingHide.IsCompleted && !IsVisible)
		{
			await pendingHide;
		}
		if (!IsVisible)
		{
			Show();
		}
		BringMainWindowToFront();
		WindowOpenAnimation.Play(RootPanel);
	}
}
