using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace HitEducation.App;

public partial class MoreWindow : Window
{
	private readonly AppStorage storage;
	private readonly Action openAdd;
	private readonly Action openList;
	private readonly Action openPicker;
	private readonly Action openSettings;
	private readonly Func<Task> toggleLock;
	private readonly Func<Task> toggleTopmost;
	private readonly Func<Task> exit;
	private bool closingWithAnimation;

	public MoreWindow(AppStorage storage, Action openAdd, Action openList, Action openPicker, Action openSettings, Func<Task> toggleLock, Func<Task> toggleTopmost, Func<Task> exit)
	{
		InitializeComponent();
		this.storage = storage;
		this.openAdd = openAdd;
		this.openList = openList;
		this.openPicker = openPicker;
		this.openSettings = openSettings;
		this.toggleLock = toggleLock;
		this.toggleTopmost = toggleTopmost;
		this.exit = exit;
		ApplyText();
		WindowOpenAnimation.Play(MoreRoot);
	}

	public async Task CloseWithAnimationAsync()
	{
		if (closingWithAnimation)
		{
			return;
		}

		closingWithAnimation = true;
		await WindowOpenAnimation.PlayCloseAsync(MoreRoot);
		Close();
	}

	private void ApplyText()
	{
		Title = Localizer.Text(storage, "More");
		TitleText.Text = Localizer.Text(storage, "More");
		AddButtonText.Text = Localizer.Text(storage, "Add");
		ListText.Text = Localizer.Text(storage, "List");
		PickerText.Text = Localizer.Text(storage, "RandomPicker");
		SettingsText.Text = Localizer.Text(storage, "Settings");
		ExitText.Text = Localizer.Text(storage, "Exit");
		LockText.Text = Localizer.Text(storage, storage.Data.Settings.LockWindow ? "UnlockWindow" : "LockWindow");
		LockIcon.Text = storage.Data.Settings.LockWindow ? "🔓" : "🔒";
		TopmostText.Text = Localizer.Text(storage, storage.Data.Settings.AlwaysOnTop ? "DisableTopmost" : "KeepTopmost");
	}

	private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.LeftButton == MouseButtonState.Pressed)
		{
			DragMove();
		}
	}

	private async void Add_Click(object sender, RoutedEventArgs e)
	{
		openAdd();
		await CloseWithAnimationAsync();
	}

	private async void List_Click(object sender, RoutedEventArgs e)
	{
		openList();
		await CloseWithAnimationAsync();
	}

	private async void Picker_Click(object sender, RoutedEventArgs e)
	{
		openPicker();
		await CloseWithAnimationAsync();
	}

	private async void Settings_Click(object sender, RoutedEventArgs e)
	{
		openSettings();
		await CloseWithAnimationAsync();
	}

	private async void Lock_Click(object sender, RoutedEventArgs e)
	{
		await toggleLock();
		ApplyText();
	}

	private async void Topmost_Click(object sender, RoutedEventArgs e)
	{
		await toggleTopmost();
		ApplyText();
	}

	private async void Exit_Click(object sender, RoutedEventArgs e)
	{
		await exit();
	}
}
