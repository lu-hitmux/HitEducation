using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace HitEducation.App;

public partial class MainWindow : Window
{
	private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

	private struct WindowRect
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	private sealed record DeferredReminder(Homework Homework, bool IsDue, DateTime QueuedAt)
	{
		public static IEnumerable<DeferredReminder> FromHomework(Homework homework)
		{
			if (homework.DeferredReminderQueuedAt is not null)
			{
				yield return new DeferredReminder(homework, IsDue: false, homework.DeferredReminderQueuedAt.Value);
			}
			if (homework.DeferredDueReminderQueuedAt is not null)
			{
				yield return new DeferredReminder(homework, IsDue: true, homework.DeferredDueReminderQueuedAt.Value);
			}
		}
	}

	private const int WmSysCommand = 274;

	private const int ScSize = 61440;

	private const int ResizeLeft = 1;

	private const int ResizeRight = 2;

	private const int ResizeTop = 3;

	private const int ResizeTopLeft = 4;

	private const int ResizeTopRight = 5;

	private const int ResizeBottom = 6;

	private const int ResizeBottomLeft = 7;

	private const int ResizeBottomRight = 8;

	private const int GaRoot = 2;

	private const int GwlStyle = -16;

	private const int WsCaption = 12582912;

	private const int WsThickFrame = 262144;

	private const int WsMaximize = 16777216;

	private const int DwmwaExtendedFrameBounds = 9;

	private const double TwoColumnHomeworkWidth = 720.0;

	private const double HomeworkColumnGap = 10.0;

	private const double HomeworkLayoutSlack = 4.0;

	private const double DefaultHomeworkFontSize = 24.0;

	private const double DefaultFloatingButtonFontSize = 15.0;

	private const double FloatingButtonFontScale = 0.45;

	private const int HomeworkRemoveAnimationMilliseconds = 260;

	private const int HomeworkReflowAnimationMilliseconds = 280;

	private const int DefaultAutoScrollIdleSeconds = 20;

	private const uint SwpNoMove = 2u;

	private const uint SwpNoSize = 1u;

	private const uint SwpNoActivate = 16u;

	private static readonly nint HwndTopmost = new IntPtr(-1);

	private static readonly nint HwndNotopmost = new IntPtr(-2);

	private readonly AppStorage storage;

	private readonly NotifyIcon trayIcon;

	private readonly DispatcherTimer archiveTimer = new();

	private readonly DispatcherTimer autoScrollTimer = new();

	private readonly DispatcherTimer fullscreenTimer = new();

	private readonly DispatcherTimer homeworkStateTimer = new();

	private readonly DispatcherTimer deferredReminderTimer = new();

	private readonly DispatcherTimer windowSaveTimer = new();

	private readonly SemaphoreSlim windowSwitchLock = new(1, 1);

	private bool allowClose;

	private bool closingWithAnimation;

	private bool restoringWindow;

	private bool suspendedTopmostForFullscreen;

	private bool hiddenForFullscreen;

	private Task hideAllWindowsTask = Task.CompletedTask;

	private int hideAllWindowsGeneration;

	private bool wasAnyHomeworkOverdue;

	private string? lastFullscreenWindowDescription;

	private nint lastFullscreenWindowHandle;

	private int lastFullscreenProcessId;

	private DateTime? fullscreenMissingSince;

	private readonly List<NotificationWindow> notificationWindows = new();

	private SettingsWindow? settingsWindow;

	private AddEditHomeworkWindow? homeworkWindow;

	private HomeworkListWindow? listWindow;

	private MoreWindow? moreWindow;

	private RandomPickerWindow? randomPickerWindow;

	private readonly Queue<DeferredReminder> deferredReminders = new();

	private DateTime? classPeriodEndedAt;

	private Point? touchDragStart;

	private double touchDragStartLeft;

	private double touchDragStartTop;

	private int? touchResizeDirection;

	private Point touchResizeStart;

	private double touchResizeStartLeft;

	private double touchResizeStartTop;

	private double touchResizeStartWidth;

	private double touchResizeStartHeight;

	private System.Windows.Controls.Button? touchResizeForwardButton;

	private Point touchResizeForwardStart;

	private System.Windows.Controls.Button? headerTouchButton;

	private Point headerTouchStart;

	private DateTime lastUserActivity = DateTime.Now;

	private int autoScrollDirection = 1;

	private DateTime? autoScrollPauseUntil;

	public MainWindow(AppStorage storage)
	{
		InitializeComponent();
		this.storage = storage;
		trayIcon = CreateTrayIcon();
		archiveTimer.Interval = TimeSpan.FromSeconds(20.0);
		archiveTimer.Tick += async (_, _) => await ArchiveExpiredAsync();
		autoScrollTimer.Interval = TimeSpan.FromMilliseconds(32.0);
		autoScrollTimer.Tick += (_, _) => AutoScrollIfIdle();
		fullscreenTimer.Interval = TimeSpan.FromMilliseconds(100.0);
		fullscreenTimer.Tick += (_, _) => UpdateTopmostForFullscreen();
		homeworkStateTimer.Interval = TimeSpan.FromSeconds(1.0);
		homeworkStateTimer.Tick += async (_, _) => await UpdateHomeworkTimeStateAsync();
		deferredReminderTimer.Interval = TimeSpan.FromSeconds(1.0);
		deferredReminderTimer.Tick += (_, _) => ShowNextDeferredReminder();
		windowSaveTimer.Interval = TimeSpan.FromMilliseconds(500.0);
		windowSaveTimer.Tick += async (_, _) =>
		{
			windowSaveTimer.Stop();
			await SaveWindowAsync();
		};
		IsManipulationEnabled = true;
	}

	private void Window_Loaded(object sender, RoutedEventArgs e)
	{
		AppLogger.Info("Main window loaded");
		ApplySettings();
		RestoreWindowPlacement();
		RenderHomeworks();
		WindowOpenAnimation.Play(RootPanel);
		trayIcon.Visible = true;
		archiveTimer.Start();
		autoScrollTimer.Start();
		fullscreenTimer.Start();
		homeworkStateTimer.Start();
		deferredReminderTimer.Start();
	}

	private void RestoreWindowPlacement()
	{
		restoringWindow = true;
		WindowPlacement placement = storage.Data.Window;
		Rect workArea = SystemParameters.WorkArea;
		Width = Math.Max(MinWidth, placement.Width);
		Height = Math.Max(MinHeight, placement.Height);
		Left = placement.Left ?? (workArea.Width - Width) / 2.0;
		Top = placement.Top ?? (workArea.Height - Height) / 2.0;
		ClampToWorkArea();
		restoringWindow = false;
	}

	private void ClampToWorkArea()
	{
		Rect workArea = SystemParameters.WorkArea;
		Left = Math.Min(Math.Max(workArea.Left, Left), workArea.Right - Math.Min(Width, workArea.Width));
		Top = Math.Min(Math.Max(workArea.Top, Top), workArea.Bottom - Math.Min(Height, workArea.Height));
	}

	private void Window_StateChanged(object sender, EventArgs e)
	{
		if (IsLoaded)
		{
			ScheduleHomeworkCardWidthUpdate();
		}
		if (IsLoaded && !restoringWindow && WindowState == WindowState.Normal)
		{
			storage.Data.Window.Left = Left;
			storage.Data.Window.Top = Top;
			storage.Data.Window.Width = Width;
			storage.Data.Window.Height = Height;
			windowSaveTimer.Stop();
			windowSaveTimer.Start();
		}
	}

	private async Task SaveWindowAsync()
	{
		if (IsLoaded && WindowState == WindowState.Normal)
		{
			windowSaveTimer.Stop();
			ClampToWorkArea();
			storage.Data.Window.Left = Left;
			storage.Data.Window.Top = Top;
			storage.Data.Window.Width = Width;
			storage.Data.Window.Height = Height;
			await storage.SaveAsync();
		}
	}

	private async void Window_Closing(object? sender, CancelEventArgs e)
	{
		if (!allowClose)
		{
			await SaveWindowAsync();
			e.Cancel = true;
			_ = HideAllWindowsAsync();
			return;
		}
		await SaveWindowAsync();
		if (!closingWithAnimation)
		{
			e.Cancel = true;
			closingWithAnimation = true;
			await CloseAllChildWindowsWithAnimationAsync();
			await WindowOpenAnimation.PlayCloseAsync(RootPanel);
			Close();
			return;
		}
		fullscreenTimer.Stop();
		autoScrollTimer.Stop();
		archiveTimer.Stop();
		homeworkStateTimer.Stop();
		deferredReminderTimer.Stop();
		foreach (NotificationWindow notificationWindow in notificationWindows.ToList())
		{
			notificationWindow.Close();
		}
		notificationWindows.Clear();
		ClearHomeworkCards();
		HomeworkItems.Items.Clear();
		trayIcon.ContextMenuStrip?.Dispose();
		trayIcon.Visible = false;
		trayIcon.Dispose();
	}

	private async Task ExitAsync()
	{
		allowClose = true;
		await SaveWindowAsync();
		Close();
		System.Windows.Application.Current.Shutdown();
	}

	private async Task CloseAllChildWindowsWithAnimationAsync()
	{
		if (homeworkWindow is not null)
		{
			await homeworkWindow.CloseWithAnimationAsync();
			homeworkWindow = null;
		}
		if (settingsWindow is not null)
		{
			await settingsWindow.CloseWithAnimationAsync();
			settingsWindow = null;
		}
		if (listWindow is not null)
		{
			await listWindow.CloseWithAnimationAsync();
			listWindow = null;
		}
		foreach (NotificationWindow notificationWindow in notificationWindows.ToList())
		{
			if (notificationWindow.IsVisible)
			{
				await notificationWindow.CloseWithAnimationAsync();
			}
			else
			{
				notificationWindow.Close();
			}
		}
		notificationWindows.Clear();
	}

	[DllImport("user32.dll")]
	private static extern bool ReleaseCapture();

	[DllImport("user32.dll")]
	private static extern nint SendMessage(nint hwnd, int msg, nint wParam, nint lParam);

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(nint hwnd);

	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(nint hwnd, out WindowRect rect);

	[DllImport("dwmapi.dll")]
	private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out WindowRect rect, int size);

	[DllImport("user32.dll")]
	private static extern bool SetWindowPos(nint hwnd, nint hwndInsertAfter, int x, int y, int cx, int cy, uint flags);

	[DllImport("user32.dll")]
	private static extern bool EnumWindows(EnumWindowsProc enumProc, nint lParam);

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(nint hwnd);

	[DllImport("user32.dll")]
	private static extern nint GetAncestor(nint hwnd, int flags);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint hwnd, out int processId);

	[DllImport("user32.dll")]
	private static extern int GetWindowLong(nint hwnd, int index);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(nint hwnd, StringBuilder className, int maxCount);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetWindowText(nint hwnd, StringBuilder text, int maxCount);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetWindowTextLength(nint hwnd);

}
