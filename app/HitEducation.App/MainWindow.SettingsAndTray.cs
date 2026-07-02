using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace HitEducation.App;

public partial class MainWindow
{
	private Forms.NotifyIcon CreateTrayIcon()
	{
		var icon = new Forms.NotifyIcon
		{
			Icon = LoadTrayIcon(),
			Text = Localizer.Text(storage, "AppName"),
			Visible = false,
			ContextMenuStrip = CreateTrayMenu()
		};
		icon.DoubleClick += (_, _) => Dispatcher.Invoke(ToggleVisible);
		return icon;
	}

	private Forms.ContextMenuStrip CreateTrayMenu()
	{
		var menu = new Forms.ContextMenuStrip();
		menu.Items.Add(Localizer.Text(storage, "ShowHide"), null, (_, _) => Dispatcher.Invoke(ToggleVisible));
		menu.Items.Add(Localizer.Text(storage, "AddHomework"), null, (_, _) => Dispatcher.Invoke(OpenAddWindow));
		menu.Items.Add(Localizer.Text(storage, "OpenSettings"), null, (_, _) => Dispatcher.Invoke(OpenSettingsWindow));
		menu.Items.Add(Localizer.Text(storage, "Exit"), null, async (_, _) => await Dispatcher.InvokeAsync(ExitAsync).Task.Unwrap());
		return menu;
	}

	private void RefreshTrayMenu()
	{
		var oldMenu = trayIcon.ContextMenuStrip;
		trayIcon.ContextMenuStrip = CreateTrayMenu();
		if (oldMenu != null)
		{
			oldMenu.Dispose();
		}
	}

	private static System.Drawing.Icon LoadTrayIcon()
	{
		Stream? stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Icons/hiteducation-app-mono.ico"))?.Stream;
		return stream == null ? System.Drawing.SystemIcons.Application : new System.Drawing.Icon(stream);
	}

	private void ApplySettings()
	{
		Topmost = storage.Data.Settings.AlwaysOnTop;
		ApplyLanguage();
		ApplyBrushOpacity(RootPanel, Border.BackgroundProperty, storage.Data.Settings.Opacity);
		ApplyBrushOpacity(HeaderBar, Panel.BackgroundProperty, storage.Data.Settings.Opacity);
		ApplyBackgroundImage();
		ApplyButtonBackgroundOpacity(this, storage.Data.Settings.Opacity);
		ApplyImageOpacity(EmptyAddButtonIcon, storage.Data.Settings.Opacity);
		ApplyTextOpacity(TitleText, storage.Data.Settings.FontOpacity);
		ApplyTextOpacity(CountText, storage.Data.Settings.FontOpacity);
		ApplyTextOpacity(EmptyText, storage.Data.Settings.FontOpacity);
		HomeworkItems.FontSize = storage.Data.Settings.FontSize;
		ApplyFloatingButtonFontSize(storage.Data.Settings.FontSize);
	}

	private void ApplyBackgroundImage()
	{
		AppSettings settings = storage.Data.Settings;
		if (!settings.BackgroundImageEnabled || string.IsNullOrWhiteSpace(settings.BackgroundImagePath) || !File.Exists(settings.BackgroundImagePath))
		{
			ContentBackgroundImage.Visibility = Visibility.Collapsed;
			ContentBackgroundImage.Background = null;
			return;
		}

		try
		{
			var image = new BitmapImage();
			image.BeginInit();
			image.CacheOption = BitmapCacheOption.OnLoad;
			image.UriSource = new Uri(settings.BackgroundImagePath, UriKind.Absolute);
			image.EndInit();
			image.Freeze();

			ContentBackgroundImage.Background = new ImageBrush(image)
			{
				Opacity = Math.Clamp(settings.Opacity, 0.35, 1.0),
				Stretch = Stretch.Fill,
				AlignmentX = AlignmentX.Center,
				AlignmentY = AlignmentY.Center
			};
			ContentBackgroundImage.Visibility = Visibility.Visible;
		}
		catch (Exception ex)
		{
			AppLogger.Error("Background image load failed: " + settings.BackgroundImagePath, ex);
			ContentBackgroundImage.Visibility = Visibility.Collapsed;
			ContentBackgroundImage.Background = null;
		}
	}

	private void ApplyFloatingButtonFontSize(double fontSize)
	{
		double buttonFontSize = FloatingButtonFontSize(fontSize);
		double buttonHeight = FloatingButtonHeight(buttonFontSize);
		HeaderBar.Height = buttonHeight + 28.0;
		ApplyFloatingButtonMetrics(AddButton, buttonFontSize, buttonHeight, 56.0);
		ApplyFloatingButtonMetrics(SettingsButton, buttonFontSize, buttonHeight, 56.0);
		ApplyFloatingButtonMetrics(HideButton, buttonFontSize, buttonHeight, 56.0);
		ApplyFloatingButtonMetrics(MoreButton, buttonFontSize, buttonHeight, 56.0);
		EmptyAddButton.FontSize = buttonFontSize;
		EmptyAddButton.MinWidth = 144.0 + Math.Max(0.0, buttonFontSize - DefaultFloatingButtonFontSize) * 2.0;
		EmptyAddButton.MinHeight = 162.0 + Math.Max(0.0, buttonFontSize - DefaultFloatingButtonFontSize) * 2.0;
	}

	private static void ApplyFloatingButtonMetrics(Button button, double fontSize, double height, double baseMinWidth)
	{
		button.FontSize = fontSize;
		button.Height = height;
		button.MinWidth = baseMinWidth + Math.Max(0.0, fontSize - DefaultFloatingButtonFontSize);
		button.Padding = new Thickness(Math.Max(6.0, fontSize * 0.28), 0.0, Math.Max(6.0, fontSize * 0.28), 0.0);
	}

	private static double FloatingButtonFontSize(double homeworkFontSize)
	{
		double scaled = DefaultFloatingButtonFontSize + (homeworkFontSize - DefaultHomeworkFontSize) * FloatingButtonFontScale;
		return Math.Clamp(scaled, 14.0, Math.Max(14.0, Math.Min(21.0, homeworkFontSize - 2.0)));
	}

	private static double FloatingButtonHeight(double buttonFontSize)
	{
		return Math.Clamp(buttonFontSize + 14.0, 38.0, 40.0);
	}

	private void ApplyLanguage()
	{
		Title = Localizer.Text(storage, "AppName");
		TitleText.Text = Localizer.Text(storage, "MainTitle");
		AddButtonText.Text = Localizer.Text(storage, "Add");
		SettingsButton.Content = Localizer.Text(storage, "Settings");
		HideButton.Content = Localizer.Text(storage, "Hide");
		MoreButton.Content = Localizer.Text(storage, "More");
		EmptyText.Text = Localizer.Text(storage, "NoHomework");
		EmptyAddButtonText.Text = Localizer.Text(storage, "AddHomework");
		trayIcon.Text = Localizer.Text(storage, "AppName");
		RefreshTrayMenu();
	}

	private static void ApplyBrushOpacity(DependencyObject target, DependencyProperty property, double opacity)
	{
		if (target.GetValue(property) is SolidColorBrush brush)
		{
			SolidColorBrush next = brush.Clone();
			next.Opacity = Math.Clamp(opacity, 0.35, 1.0);
			target.SetValue(property, next);
		}
	}

	private static void ApplyTextOpacity(TextBlock text, double opacity)
	{
		if (text.Foreground is SolidColorBrush brush)
		{
			SolidColorBrush next = brush.Clone();
			next.Opacity = Math.Clamp(opacity, 0.35, 1.0);
			text.Foreground = next;
		}
	}

	private static void ApplyImageOpacity(System.Windows.Controls.Image image, double opacity)
	{
		image.Opacity = Math.Clamp(opacity, 0.35, 1.0);
	}

	private static void ApplyButtonBackgroundOpacity(DependencyObject root, double opacity)
	{
		int childCount = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < childCount; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is Button button)
			{
				if (button.Background is not SolidColorBrush brush)
				{
					brush = new SolidColorBrush(Color.FromRgb(245, 245, 245));
				}
				SolidColorBrush next = brush.Clone();
				next.Opacity = Math.Clamp(opacity, 0.35, 1.0);
				button.Background = next;
			}
			ApplyButtonBackgroundOpacity(child, opacity);
		}
	}
}
