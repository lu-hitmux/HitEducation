using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace HitEducation.App;

public partial class RandomPickerWindow : Window
{
	private readonly AppStorage storage;
	private readonly Random random = new();
	private readonly DispatcherTimer rollTimer = new();
	private DateTime rollUntil;
	private bool closingWithAnimation;

	public RandomPickerWindow(AppStorage storage)
	{
		InitializeComponent();
		this.storage = storage;
		rollTimer.Interval = TimeSpan.FromMilliseconds(70);
		rollTimer.Tick += RollTimer_Tick;
		ApplyLanguage();
		RefreshState();
		WindowOpenAnimation.Play(PickerRoot);
	}

	public async Task CloseWithAnimationAsync()
	{
		if (closingWithAnimation)
		{
			return;
		}

		closingWithAnimation = true;
		rollTimer.Stop();
		await WindowOpenAnimation.PlayCloseAsync(PickerRoot);
		Close();
	}

	private void ApplyLanguage()
	{
		Title = Localizer.Text(storage, "RandomPicker");
		TitleText.Text = Localizer.Text(storage, "RandomPicker");
		ImportButton.Content = Localizer.Text(storage, "ImportRoster");
		ConfigButton.Content = Localizer.Text(storage, "Configure");
		SampleButton.Content = Localizer.Text(storage, "ShowRosterSample");
		SampleText.Text = Localizer.Text(storage, "RosterSampleText");
		StartButton.Content = Localizer.Text(storage, "StartPicking");
		CloseButton.Content = Localizer.Text(storage, "Close");
	}

	private void RefreshState()
	{
		int count = storage.Data.RandomPicker.ActiveMembers.Count;
		string rosterName = storage.Data.RandomPicker.Rosters.FirstOrDefault(x => x.Id == storage.Data.RandomPicker.ActiveRosterId)?.Name ?? string.Empty;
		StatusText.Text = count == 0 ? Localizer.Text(storage, "NoRosterLoaded") : Localizer.Format(storage, "RosterLoadedCount", rosterName, count);
		StartButton.IsEnabled = count > 0 && !rollTimer.IsEnabled;
		ConfigButton.IsEnabled = count > 0 && !rollTimer.IsEnabled;
		ImportButton.IsEnabled = !rollTimer.IsEnabled;
		if (count == 0)
		{
			ShowText(Localizer.Text(storage, "ImportRosterFirst"), 52);
		}
	}

	private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.LeftButton == MouseButtonState.Pressed)
		{
			DragMove();
		}
	}

	private async void Import_Click(object sender, RoutedEventArgs e)
	{
		var dialog = new OpenFileDialog
		{
			Title = Localizer.Text(storage, "ImportRoster"),
			Filter = Localizer.Text(storage, "RosterFileFilter")
		};
		if (dialog.ShowDialog(this) != true)
		{
			return;
		}

		await ImportRosterAsync(dialog.FileName);
	}

	private async void Config_Click(object sender, RoutedEventArgs e)
	{
		var window = new RandomPickerConfigWindow(storage)
		{
			Owner = this,
			WindowStartupLocation = WindowStartupLocation.CenterOwner
		};
		window.ShowDialog();
		await storage.SaveAsync();
		RefreshState();
	}

	private void Start_Click(object sender, RoutedEventArgs e)
	{
		if (storage.Data.RandomPicker.ActiveMembers.Count == 0)
		{
			RefreshState();
			return;
		}

		NameText.FontSize = 42;
		rollUntil = DateTime.Now.AddSeconds(2.2);
		rollTimer.Start();
		RefreshState();
	}

	private void RollTimer_Tick(object? sender, EventArgs e)
	{
		var members = storage.Data.RandomPicker.ActiveMembers;
		if (members.Count == 0)
		{
			rollTimer.Stop();
			RefreshState();
			return;
		}

		if (DateTime.Now < rollUntil)
		{
			ShowMember(members[random.Next(members.Count)], final: false);
			return;
		}

		rollTimer.Stop();
		ShowMember(PickWeightedMember(), final: true);
		RefreshState();
	}

	private RandomPickerMember PickWeightedMember()
	{
		var members = storage.Data.RandomPicker.ActiveMembers.Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToList();
		var guaranteed = members.Where(x => Math.Clamp(x.Weight, 0.01, 10.0) >= 10.0).ToList();
		if (guaranteed.Count > 0)
		{
			return guaranteed[random.Next(guaranteed.Count)];
		}

		double total = members.Sum(x => EffectiveWeight(x.Weight));
		double target = random.NextDouble() * total;
		double current = 0.0;
		foreach (var member in members)
		{
			current += EffectiveWeight(member.Weight);
			if (target <= current)
			{
				return member;
			}
		}

		return members.Last();
	}

	private static double EffectiveWeight(double weight)
	{
		weight = Math.Clamp(weight, 0.01, 9.99);
		return weight * weight;
	}

	private void ShowMember(RandomPickerMember member, bool final)
	{
		NameText.Text = member.Name;
		NameText.FontSize = final ? 72 : 42;
		if (!string.IsNullOrWhiteSpace(member.ThumbnailPath) && File.Exists(member.ThumbnailPath))
		{
			SlideImage.Source = new BitmapImage(new Uri(member.ThumbnailPath, UriKind.Absolute));
			SlideImage.Visibility = Visibility.Visible;
			NameText.FontSize = final ? 30 : 22;
			return;
		}

		SlideImage.Source = null;
		SlideImage.Visibility = Visibility.Collapsed;
	}

	private void ShowText(string text, double fontSize)
	{
		SlideImage.Source = null;
		SlideImage.Visibility = Visibility.Collapsed;
		NameText.Text = text;
		NameText.FontSize = fontSize;
	}

	private void Sample_Click(object sender, RoutedEventArgs e)
	{
		SamplePanel.Visibility = SamplePanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
	}

	private void Window_DragOver(object sender, DragEventArgs e)
	{
		e.Effects = TryGetDroppedFile(e, out string? path) && RandomPickerImporter.IsSupported(path) ? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
	}

	private async void Window_Drop(object sender, DragEventArgs e)
	{
		if (!TryGetDroppedFile(e, out string? path) || !RandomPickerImporter.IsSupported(path))
		{
			StatusText.Text = Localizer.Text(storage, "UnsupportedRosterFile");
			return;
		}

		await ImportRosterAsync(path);
	}

	private async Task ImportRosterAsync(string path)
	{
		try
		{
			string rosterId = Guid.NewGuid().ToString("N");
			string rosterDirectory = Path.Combine(storage.DataDirectory, "rosters", rosterId);
			Directory.CreateDirectory(rosterDirectory);
			string savedPath = Path.Combine(rosterDirectory, Path.GetFileName(path));
			File.Copy(path, savedPath, overwrite: true);

			var members = RandomPickerImporter.Import(savedPath, Path.Combine(rosterDirectory, "thumbnails"));
			if (members.Count == 0)
			{
				Directory.Delete(rosterDirectory, recursive: true);
				StatusText.Text = Localizer.Text(storage, "RosterImportEmpty");
				return;
			}

			var roster = new RandomPickerRoster
			{
				Id = rosterId,
				Name = Path.GetFileNameWithoutExtension(path),
				SourcePath = savedPath,
				ImportedAt = DateTime.Now,
				Members = members
			};
			storage.Data.RandomPicker.Rosters.Add(roster);
			storage.Data.RandomPicker.ActiveRosterId = roster.Id;
			storage.Data.RandomPicker.Members = members;
			await storage.SaveAsync();
			ShowMember(members[0], final: false);
			RefreshState();
		}
		catch (Exception ex)
		{
			AppLogger.Error("Roster import failed: " + path, ex);
			StatusText.Text = Localizer.Format(storage, "ImportFailed", ex.Message);
		}
	}

	private static bool TryGetDroppedFile(DragEventArgs e, out string path)
	{
		path = string.Empty;
		if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files)
		{
			return false;
		}

		path = files[0];
		return File.Exists(path);
	}

	private async void Close_Click(object sender, RoutedEventArgs e)
	{
		await CloseWithAnimationAsync();
	}
}
