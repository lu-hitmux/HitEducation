using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;

namespace HitEducation.App;

public partial class App : Application
{
	public static AppStorage Storage { get; private set; } = null!;

	public App()
	{
		DispatcherUnhandledException += (_, e) =>
		{
			AppLogger.Error("UI thread unhandled exception", e.Exception);
			MessageBox.Show($"程序遇到错误，日志已写入：\n{AppLogger.LogFile}\n\n{e.Exception.Message}", "课堂作业", MessageBoxButton.OK, MessageBoxImage.Error);
			e.Handled = true;
		};

		AppDomain.CurrentDomain.UnhandledException += (_, e) =>
		{
			if (e.ExceptionObject is Exception exception)
			{
				AppLogger.Error("AppDomain unhandled exception", exception);
			}
			else
			{
				AppLogger.Info($"AppDomain unhandled exception object: {e.ExceptionObject}");
			}
		};

		TaskScheduler.UnobservedTaskException += (_, e) =>
		{
			AppLogger.Error("Unobserved task exception", e.Exception);
			e.SetObserved();
		};
	}

	protected override async void OnStartup(StartupEventArgs e)
	{
		try
		{
			base.OnStartup(e);
			ShutdownMode = ShutdownMode.OnExplicitShutdown;
			AppLogger.Info("Application startup begin");

			var splash = new SplashWindow();
			splash.Show();

			Storage = new AppStorage();
			AppLogger.Info($"Data file: {Storage.DataFile}");
			await Storage.LoadAsync();
			splash.ApplyLanguage(Storage);
			AutoStartManager.Save(Storage.Data.Settings.AutoStart);

			var skipAutoUpdateOnce = Array.Exists(e.Args, arg => string.Equals(arg, "--skip-auto-update-once", StringComparison.OrdinalIgnoreCase));
			if (Storage.Data.Settings.AutoUpdate && !skipAutoUpdateOnce)
			{
				UpdateChecker.TryStartUpdater(auto: true);
			}

			await Task.Delay(450);

			var mainWindow = new MainWindow(Storage);
			MainWindow = mainWindow;
			await splash.CloseWithAnimationAsync();
			mainWindow.Show();
			mainWindow.Activate();
			MemoryOptimizer.TrimOnceAfterDelay(4000, force: true);
			AppLogger.Info("Main window shown");
		}
		catch (Exception exception)
		{
			AppLogger.Error("Startup failed", exception);
			MessageBox.Show($"启动失败，日志已写入：\n{AppLogger.LogFile}\n\n{exception.Message}", "课堂作业", MessageBoxButton.OK, MessageBoxImage.Error);
			Shutdown(-1);
		}
	}
}
