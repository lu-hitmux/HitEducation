using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace HitEducation.App;

public partial class NotificationWindow : Window
{
	private const double StackGap = 8.0;

	private readonly DispatcherTimer closeTimer = new DispatcherTimer();

	private readonly int initialStackIndex;

	private bool closingWithAnimation;

	public NotificationWindow(string title, string detail, string label, int durationSeconds, int stackIndex)
	{
		InitializeComponent();
		initialStackIndex = Math.Max(0, stackIndex);
		WindowOpenAnimation.Play(NotificationRoot);
		LabelText.Text = label;
		TitleText.Text = title;
		DetailText.Text = detail;
		closeTimer.Interval = TimeSpan.FromSeconds(Math.Max(3, durationSeconds));
		closeTimer.Tick += async (_, _) =>
		{
			closeTimer.Stop();
			await CloseWithAnimationAsync();
		};
	}

	private void Window_Loaded(object sender, RoutedEventArgs e)
	{
		MoveToStackIndex(initialStackIndex);
		closeTimer.Start();
	}

	public void MoveToStackIndex(int stackIndex)
	{
		Width = Math.Min(980, SystemParameters.WorkArea.Width * 0.86);
		Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - Width) / 2;
		Top = SystemParameters.WorkArea.Top + Math.Max(0, stackIndex) * (Height + StackGap);
	}

	public async Task CloseWithAnimationAsync()
	{
		if (closingWithAnimation) return;
		closingWithAnimation = true;
		await WindowOpenAnimation.PlayCloseAsync(NotificationRoot);
		Close();
		MemoryOptimizer.TrimSoon();
	}
}
