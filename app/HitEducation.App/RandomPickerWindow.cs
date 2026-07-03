using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace HitEducation.App;

public partial class RandomPickerWindow : Window
{
	public event Action? RosterImported;
	public event Action<bool, string, int>? RosterImportProgressChanged;

	private readonly AppStorage storage;
	private readonly Random random = new();
	private readonly DispatcherTimer rollTimer = new();
	private const int WmDropFiles = 0x0233;
	private DateTime rollUntil;
	private DateTime lastImportAt;
	private bool closingWithAnimation;
	private bool importingRoster;
	private string dragStatusPath = string.Empty;
	private string lastImportPath = string.Empty;

	public RandomPickerWindow(AppStorage storage)
	{
		InitializeComponent();
		this.storage = storage;
		RegisterDropHandlers();
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

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);
		var source = (HwndSource?)PresentationSource.FromVisual(this);
		if (source is not null)
		{
			source.AddHook(WndProc);
			DragAcceptFiles(source.Handle, true);
		}
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

	private void RefreshState(bool forceDisplay = false)
	{
		int count = storage.Data.RandomPicker.ActiveMembers.Count;
		string rosterName = storage.Data.RandomPicker.ActiveRoster?.Name ?? string.Empty;
		StatusText.Text = count == 0 ? Localizer.Text(storage, "NoRosterLoaded") : Localizer.Format(storage, "RosterLoadedCount", rosterName, count);
		StartButton.IsEnabled = count > 0 && !rollTimer.IsEnabled;
		ConfigButton.IsEnabled = count > 0 && !rollTimer.IsEnabled;
		ImportButton.IsEnabled = !rollTimer.IsEnabled;
		if (count == 0)
		{
			ShowText(Localizer.Text(storage, "ImportRosterFirst"), 52);
		}
		else if (forceDisplay || string.IsNullOrWhiteSpace(NameText.Text) || NameText.Text == Localizer.Text(storage, "ImportRosterFirst"))
		{
			ShowMember(storage.Data.RandomPicker.ActiveMembers[0], final: false);
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

	private void Config_Click(object sender, RoutedEventArgs e)
	{
		var window = new RandomPickerConfigWindow(storage)
		{
			Owner = this,
			WindowStartupLocation = WindowStartupLocation.CenterOwner
		};
		void RefreshConfig() => window.RefreshRostersFromStorage();
		void ShowConfigProgress(bool isImporting, string path, int percent) => window.SetExternalImporting(isImporting, path, percent);
		RosterImported += RefreshConfig;
		RosterImportProgressChanged += ShowConfigProgress;
		window.ShowDialog();
		RosterImported -= RefreshConfig;
		RosterImportProgressChanged -= ShowConfigProgress;
		RefreshState(forceDisplay: true);
	}

	private void Start_Click(object sender, RoutedEventArgs e)
	{
		if (storage.Data.RandomPicker.ActiveMembers.Count == 0)
		{
			RefreshState();
			return;
		}

		AppLogger.Info($"Random picker started. Roster={storage.Data.RandomPicker.ActiveRoster?.Name ?? string.Empty}, Count={storage.Data.RandomPicker.ActiveMembers.Count}");
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
		var picked = PickWeightedMember();
		AppLogger.Info($"Random picker result. Roster={storage.Data.RandomPicker.ActiveRoster?.Name ?? string.Empty}, Name={picked.Name}, Weight={picked.Weight:0.##}");
		ShowMember(picked, final: true);
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

	private void RegisterDropHandlers()
	{
		AllowDrop = true;
		PickerRoot.AllowDrop = true;
		AddHandler(DragDrop.PreviewDragEnterEvent, new DragEventHandler(Window_DragOver), handledEventsToo: true);
		AddHandler(DragDrop.PreviewDragOverEvent, new DragEventHandler(Window_DragOver), handledEventsToo: true);
		AddHandler(DragDrop.PreviewDragLeaveEvent, new DragEventHandler(Window_DragLeave), handledEventsToo: true);
		AddHandler(DragDrop.PreviewDropEvent, new DragEventHandler(Window_Drop), handledEventsToo: true);
		AddHandler(DragDrop.DragEnterEvent, new DragEventHandler(Window_DragOver), handledEventsToo: true);
		AddHandler(DragDrop.DragOverEvent, new DragEventHandler(Window_DragOver), handledEventsToo: true);
		AddHandler(DragDrop.DragLeaveEvent, new DragEventHandler(Window_DragLeave), handledEventsToo: true);
		AddHandler(DragDrop.DropEvent, new DragEventHandler(Window_Drop), handledEventsToo: true);
	}

	private void Window_DragOver(object sender, DragEventArgs e)
	{
		if (TryGetDroppedFile(e, out string path) && RandomPickerImporter.IsSupported(path))
		{
			e.Effects = DragDropEffects.Copy;
			if (!string.Equals(dragStatusPath, path, StringComparison.OrdinalIgnoreCase))
			{
				dragStatusPath = path;
				StatusText.Text = Localizer.Format(storage, "DropRosterToImport", Path.GetFileName(path));
			}
		}
		else
		{
			dragStatusPath = string.Empty;
			e.Effects = DragDropEffects.None;
		}

		e.Handled = true;
	}

	private void Window_DragLeave(object sender, DragEventArgs e)
	{
		dragStatusPath = string.Empty;
		RefreshState();
		e.Handled = true;
	}

	private async void Window_Drop(object sender, DragEventArgs e)
	{
		dragStatusPath = string.Empty;
		if (!TryGetDroppedFile(e, out string? path) || !RandomPickerImporter.IsSupported(path))
		{
			AppLogger.Info("Random picker drop rejected: unsupported or missing file.");
			StatusText.Text = Localizer.Text(storage, "UnsupportedRosterFile");
			return;
		}

		await ImportRosterAsync(path);
	}

	private async Task ImportRosterAsync(string path)
	{
		if (!RandomPickerImportService.TryBeginImport(path, out string normalizedPath))
		{
			AppLogger.Info("Random picker import ignored as already active: " + path);
			return;
		}

		if (importingRoster || (string.Equals(lastImportPath, normalizedPath, StringComparison.OrdinalIgnoreCase) && DateTime.Now - lastImportAt < TimeSpan.FromSeconds(2)))
		{
			RandomPickerImportService.EndImport(normalizedPath);
			AppLogger.Info("Random picker import ignored as duplicate: " + normalizedPath);
			return;
		}

		importingRoster = true;
		SetImporting(true, normalizedPath, 0);
		try
		{
			AppLogger.Info("Random picker import started: " + normalizedPath);
			var progress = new Progress<RandomPickerImportProgress>(p => SetImportProgress(normalizedPath, p.Percent));
			var result = await RandomPickerImportService.ImportAsync(storage, normalizedPath, progress);
			var roster = result.Roster;
			var members = result.Members;
			storage.Data.RandomPicker.Rosters.Add(roster);
			storage.Data.RandomPicker.ActiveRosterId = roster.Id;
			storage.Data.RandomPicker.Members = members;
			await storage.SaveAsync();
			lastImportPath = normalizedPath;
			lastImportAt = DateTime.Now;
			AppLogger.Info($"Random picker import succeeded. Roster={roster.Name}, Count={members.Count}, SourceCopy={roster.SourcePath}");
			ShowMember(members[0], final: false);
			RefreshState();
			RosterImported?.Invoke();
		}
		catch (Exception ex)
		{
			AppLogger.Error("Roster import failed: " + normalizedPath, ex);
			StatusText.Text = Localizer.Format(storage, "ImportFailed", ex.Message);
		}
		finally
		{
			importingRoster = false;
			SetImporting(false, normalizedPath, 100);
			RandomPickerImportService.EndImport(normalizedPath);
		}
	}

	private void SetImporting(bool isImporting, string path, int percent)
	{
		int clampedPercent = Math.Clamp(percent, 0, 100);
		ImportProgressPanel.Visibility = isImporting ? Visibility.Visible : Visibility.Collapsed;
		ImportProgress.Value = isImporting ? clampedPercent : 0;
		ImportProgressText.Text = isImporting ? clampedPercent + "%" : "0%";
		ImportButton.IsEnabled = !isImporting;
		ConfigButton.IsEnabled = !isImporting && storage.Data.RandomPicker.ActiveMembers.Count > 0;
		StartButton.IsEnabled = !isImporting && storage.Data.RandomPicker.ActiveMembers.Count > 0 && !rollTimer.IsEnabled;
		if (isImporting)
		{
			StatusText.Text = Localizer.Format(storage, "ImportingRoster", Path.GetFileName(path));
		}

		RosterImportProgressChanged?.Invoke(isImporting, path, clampedPercent);
	}

	private void SetImportProgress(string path, int percent)
	{
		int clampedPercent = Math.Clamp(percent, 0, 100);
		ImportProgress.Value = clampedPercent;
		ImportProgressText.Text = clampedPercent + "%";
		RosterImportProgressChanged?.Invoke(true, path, clampedPercent);
	}

	private static bool TryGetDroppedFile(DragEventArgs e, out string path)
	{
		path = string.Empty;
		if (!e.Data.GetDataPresent(DataFormats.FileDrop, autoConvert: true) || e.Data.GetData(DataFormats.FileDrop, autoConvert: true) is not string[] { Length: > 0 } files)
		{
			return false;
		}

		path = files[0];
		return File.Exists(path);
	}

	private void Content_DragEnter(object sender, DragEventArgs e) => Window_DragOver(sender, e);

	private void Content_DragOver(object sender, DragEventArgs e) => Window_DragOver(sender, e);

	private void Content_DragLeave(object sender, DragEventArgs e) => Window_DragLeave(sender, e);

	private void Content_Drop(object sender, DragEventArgs e) => Window_Drop(sender, e);

	private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
	{
		Width = Math.Max(MinWidth, ActualWidth + e.HorizontalChange);
		Height = Math.Max(MinHeight, ActualHeight + e.VerticalChange);
	}

	private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
	{
		if (msg != WmDropFiles)
		{
			return IntPtr.Zero;
		}

		try
		{
			uint fileCount = DragQueryFile(wParam, 0xFFFFFFFF, null, 0);
			if (fileCount > 0)
			{
				uint length = DragQueryFile(wParam, 0, null, 0);
				var buffer = new System.Text.StringBuilder((int)length + 1);
				DragQueryFile(wParam, 0, buffer, (uint)buffer.Capacity);
				string path = buffer.ToString();
				if (File.Exists(path) && RandomPickerImporter.IsSupported(path))
				{
					AppLogger.Info("Random picker Win32 drop received: " + path);
					_ = ImportRosterAsync(path);
				}
				else
				{
					AppLogger.Info("Random picker Win32 drop rejected: " + path);
					StatusText.Text = Localizer.Text(storage, "UnsupportedRosterFile");
				}
			}
		}
		finally
		{
			DragFinish(wParam);
			handled = true;
		}

		return IntPtr.Zero;
	}

	private async void Close_Click(object sender, RoutedEventArgs e)
	{
		await CloseWithAnimationAsync();
	}

	[System.Runtime.InteropServices.DllImport("shell32.dll")]
	private static extern void DragAcceptFiles(IntPtr hwnd, bool accept);

	[System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
	private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, System.Text.StringBuilder? fileName, uint bufferSize);

	[System.Runtime.InteropServices.DllImport("shell32.dll")]
	private static extern void DragFinish(IntPtr hDrop);
}
