using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Windows.Interop;

namespace HitEducation.App;

public partial class MainWindow
{
	private async void UpdateTopmostForFullscreen()
	{
		if (!storage.Data.Settings.AlwaysOnTop)
		{
			suspendedTopmostForFullscreen = false;
			hiddenForFullscreen = false;
			SetWindowTopmost(enabled: false);
			return;
		}

		if (!storage.Data.Settings.FullscreenDetectionEnabled)
		{
			if (suspendedTopmostForFullscreen || hiddenForFullscreen)
			{
				suspendedTopmostForFullscreen = false;
				hiddenForFullscreen = false;
				await ShowAfterHideAllWindowsAsync();
			}
			SetWindowTopmost(enabled: true);
			return;
		}

		if (IsAnyOtherWindowFullscreen())
		{
			fullscreenMissingSince = null;
			SetWindowTopmost(enabled: false);
			if (!suspendedTopmostForFullscreen)
			{
				suspendedTopmostForFullscreen = true;
				if (IsVisible && !hiddenForFullscreen)
				{
					hiddenForFullscreen = true;
					_ = HideAllWindowsAsync();
				}
				AppLogger.Info("Topmost suspended for fullscreen window: " + lastFullscreenWindowDescription);
			}
			return;
		}

		if (!suspendedTopmostForFullscreen)
		{
			return;
		}

		fullscreenMissingSince ??= DateTime.Now;
		if ((DateTime.Now - fullscreenMissingSince.Value).TotalMilliseconds < 250.0)
		{
			return;
		}

		suspendedTopmostForFullscreen = false;
		SetWindowTopmost(enabled: true);
		if (hiddenForFullscreen)
		{
			hiddenForFullscreen = false;
			await ShowAfterHideAllWindowsAsync();
			SetWindowTopmost(enabled: true);
		}
		AppLogger.Info("Topmost restored after fullscreen window: " + lastFullscreenWindowDescription);
		lastFullscreenWindowDescription = null;
		lastFullscreenWindowHandle = IntPtr.Zero;
		lastFullscreenProcessId = 0;
		fullscreenMissingSince = null;
	}

	private bool IsAnyOtherWindowFullscreen()
	{
		var selfHandle = new WindowInteropHelper(this).Handle;
		var currentProcessId = Environment.ProcessId;
		var foregroundWindow = GetForegroundWindow();

		if (IsCandidateFullscreenWindow(foregroundWindow, selfHandle, currentProcessId, allowInvisible: true))
		{
			return true;
		}

		if (lastFullscreenWindowHandle != IntPtr.Zero
			&& IsCandidateFullscreenWindow(lastFullscreenWindowHandle, selfHandle, currentProcessId, allowInvisible: true))
		{
			return true;
		}

		var found = false;
		EnumWindows((hwnd, _) =>
		{
			if (hwnd == IntPtr.Zero || hwnd == selfHandle)
			{
				return true;
			}

			if (!IsCandidateFullscreenWindow(hwnd, selfHandle, currentProcessId, allowInvisible: false))
			{
				return true;
			}

			found = true;
			return false;
		}, IntPtr.Zero);

		return found;
	}

	private bool IsCandidateFullscreenWindow(nint hwnd, nint selfHandle, int currentProcessId, bool allowInvisible)
	{
		if (hwnd == IntPtr.Zero || hwnd == selfHandle)
		{
			return false;
		}
		if (!allowInvisible && !IsWindowVisible(hwnd))
		{
			return false;
		}
		if (IsShellOrSystemWindow(hwnd))
		{
			return false;
		}
		GetWindowThreadProcessId(hwnd, out var processId);
		if (processId == currentProcessId)
		{
			return false;
		}
		var processName = GetProcessName(processId);
		if (!IsAllowedFullscreenProcess(processName))
		{
			return false;
		}
		if (!TryGetFullscreenLikeBounds(hwnd, out var rect))
		{
			return false;
		}
		lastFullscreenWindowHandle = hwnd;
		lastFullscreenProcessId = processId;
		lastFullscreenWindowDescription = DescribeWindow(hwnd, rect, processName);
		return true;
	}

	private bool IsAllowedFullscreenProcess(string processName)
	{
		var blockedProcessNames = storage.Data.Settings.FullscreenBlockedProcessNames;
		if (blockedProcessNames.Any(name => string.Equals(name.Trim(), processName, StringComparison.OrdinalIgnoreCase)))
		{
			return false;
		}

		var allowedProcessNames = storage.Data.Settings.FullscreenProcessNames;
		if (allowedProcessNames.Count == 0)
		{
			return true;
		}

		return allowedProcessNames.Any(name => string.Equals(name.Trim(), processName, StringComparison.OrdinalIgnoreCase));
	}

	private static bool TryGetFullscreenLikeBounds(nint hwnd, out WindowRect rect)
	{
		if (GetWindowRect(hwnd, out var windowRect) && (IsFullscreenRect(windowRect) || IsNearFullscreenRect(windowRect)))
		{
			rect = windowRect;
			return true;
		}

		if (DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out var frameRect, Marshal.SizeOf<WindowRect>()) == 0
			&& (IsFullscreenRect(frameRect) || IsNearFullscreenRect(frameRect)))
		{
			rect = frameRect;
			return true;
		}

		rect = default;
		return false;
	}

	private static bool IsShellOrSystemWindow(nint hwnd)
	{
		return GetWindowClassName(hwnd) is "Progman"
			or "WorkerW"
			or "Shell_TrayWnd"
			or "Shell_SecondaryTrayWnd"
			or "NotifyIconOverflowWindow"
			or "DV2ControlHost"
			or "Windows.UI.Core.CoreWindow";
	}

	private static string DescribeWindow(nint hwnd, WindowRect rect, string processName)
	{
		GetWindowThreadProcessId(hwnd, out var processId);
		var className = GetWindowClassName(hwnd);
		var title = GetWindowTitle(hwnd);
		var style = GetWindowLong(hwnd, GwlStyle);
		return $"pid={processId}, process={processName}, class={className}, title={title}, style=0x{style:X8}, rect=({rect.Left},{rect.Top},{rect.Right},{rect.Bottom})";
	}

	private static string GetProcessName(int processId)
	{
		try
		{
			using Process process = Process.GetProcessById(processId);
			return process.ProcessName;
		}
		catch
		{
			return "";
		}
	}

	private static string GetWindowClassName(nint hwnd)
	{
		var buffer = new StringBuilder(256);
		return GetClassName(hwnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : "";
	}

	private static string GetWindowTitle(nint hwnd)
	{
		var length = GetWindowTextLength(hwnd);
		if (length <= 0)
		{
			return "";
		}

		var buffer = new StringBuilder(length + 1);
		return GetWindowText(hwnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : "";
	}

	private static bool IsFullscreenRect(WindowRect rect)
	{
		int width = rect.Right - rect.Left;
		int height = rect.Bottom - rect.Top;
		if (width <= 0 || height <= 0)
		{
			return false;
		}
		return Screen.AllScreens.Any(screen =>
		{
			var bounds = screen.Bounds;
			return rect.Left <= bounds.Left + 4
				&& rect.Top <= bounds.Top + 4
				&& rect.Right >= bounds.Right - 4
				&& rect.Bottom >= bounds.Bottom - 4
				&& width >= bounds.Width - 8
				&& height >= bounds.Height - 8;
		});
	}

	private static bool IsNearFullscreenRect(WindowRect rect)
	{
		int width = rect.Right - rect.Left;
		int height = rect.Bottom - rect.Top;
		if (width <= 0 || height <= 0)
		{
			return false;
		}

		return Screen.AllScreens.Any(screen =>
		{
			var bounds = screen.Bounds;
			int horizontalOverlap = Math.Min(rect.Right, bounds.Right) - Math.Max(rect.Left, bounds.Left);
			int verticalOverlap = Math.Min(rect.Bottom, bounds.Bottom) - Math.Max(rect.Top, bounds.Top);
			if (horizontalOverlap <= 0 || verticalOverlap <= 0)
			{
				return false;
			}

			double coverage = (double)(horizontalOverlap * verticalOverlap) / (bounds.Width * bounds.Height);
			bool touchesHorizontalEdges = rect.Left <= bounds.Left + 12 && rect.Right >= bounds.Right - 12;
			bool touchesTopEdge = rect.Top <= bounds.Top + 12;
			return coverage >= 0.94 && touchesHorizontalEdges && touchesTopEdge;
		});
	}

	private void SetWindowTopmost(bool enabled)
	{
		Topmost = enabled;
		var handle = new WindowInteropHelper(this).Handle;
		if (handle == IntPtr.Zero)
		{
			return;
		}

		SetWindowPos(handle, enabled ? HwndTopmost : HwndNotopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
	}
}
