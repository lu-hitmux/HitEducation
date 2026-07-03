using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace HitEducation.App;

public partial class RandomPickerConfigWindow : Window
{
	private readonly AppStorage storage;
	private readonly ObservableCollection<RandomPickerRoster> rosters;
	private readonly ObservableCollection<MemberWeightViewModel> members;
	private string initialActiveRosterId;
	private bool loadingRoster;
	private bool accepted;
	private bool closingWithAnimation;
	private bool importingRoster;
	private string dragStatusPath = string.Empty;

	public RandomPickerConfigWindow(AppStorage storage)
	{
		InitializeComponent();
		this.storage = storage;
		initialActiveRosterId = storage.Data.RandomPicker.ActiveRosterId;
		rosters = new ObservableCollection<RandomPickerRoster>();
		members = new ObservableCollection<MemberWeightViewModel>();
		RosterBox.ItemsSource = rosters;
		MemberItems.ItemsSource = members;
		RegisterDropHandlers();
		ApplyLanguage();
		LoadRosters();
		WindowOpenAnimation.Play(ConfigRoot);
	}

	public async Task CloseWithAnimationAsync()
	{
		if (closingWithAnimation)
		{
			return;
		}

		closingWithAnimation = true;
		await WindowOpenAnimation.PlayCloseAsync(ConfigRoot);
		Close();
	}

	private void ApplyLanguage()
	{
		Title = Localizer.Text(storage, "PickerConfig");
		TitleText.Text = Localizer.Text(storage, "PickerConfig");
		RosterLabel.Text = Localizer.Text(storage, "CurrentRoster");
		HelpText.Text = Localizer.Text(storage, "PickerWeightHelp");
		DeleteRosterButton.Content = Localizer.Text(storage, "Delete");
		ResetWeightsButton.Content = Localizer.Text(storage, "ResetWeights");
		CancelButton.Content = Localizer.Text(storage, "Cancel");
		OkButton.Content = Localizer.Text(storage, "OK");
	}

	private void LoadRosters()
	{
		loadingRoster = true;
		rosters.Clear();
		foreach (var roster in storage.Data.RandomPicker.Rosters)
		{
			rosters.Add(roster);
		}

		var selectedRoster = storage.Data.RandomPicker.Rosters.FirstOrDefault(x => x.Id == storage.Data.RandomPicker.ActiveRosterId);
		RosterBox.SelectedItem = selectedRoster;
		loadingRoster = false;
		DeleteRosterButton.IsEnabled = selectedRoster is not null;
		LoadMembers();
	}

	public void RefreshRostersFromStorage()
	{
		Dispatcher.Invoke(() =>
		{
			LoadRosters();
			HelpText.Text = Localizer.Text(storage, "PickerWeightHelp");
		});
	}

	public void SetExternalImporting(bool isImporting, string path, int percent)
	{
		Dispatcher.Invoke(() => SetImporting(isImporting, path, percent));
	}

	private void LoadMembers()
	{
		members.Clear();
		foreach (var member in storage.Data.RandomPicker.ActiveMembers)
		{
			members.Add(new MemberWeightViewModel(member.Name, member.Weight, member.ThumbnailPath));
		}
	}

	private void SaveMembers()
	{
		foreach (var member in storage.Data.RandomPicker.ActiveMembers)
		{
			var viewModel = members.FirstOrDefault(x => x.Name == member.Name);
			if (viewModel is not null)
			{
				member.Weight = viewModel.Weight;
			}
		}
	}

	private void RosterBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
	{
		if (loadingRoster || RosterBox.SelectedItem is not RandomPickerRoster roster)
		{
			return;
		}

		SaveMembers();
		storage.Data.RandomPicker.ActiveRosterId = roster.Id;
		storage.Data.RandomPicker.Members = roster.Members;
		DeleteRosterButton.IsEnabled = true;
		LoadMembers();
	}

	private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.LeftButton == MouseButtonState.Pressed)
		{
			DragMove();
		}
	}

	private void RegisterDropHandlers()
	{
		AllowDrop = true;
		ConfigRoot.AllowDrop = true;
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
				HelpText.Text = Localizer.Format(storage, "DropRosterToImport", Path.GetFileName(path));
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
		HelpText.Text = Localizer.Text(storage, "PickerWeightHelp");
		e.Handled = true;
	}

	private async void Window_Drop(object sender, DragEventArgs e)
	{
		dragStatusPath = string.Empty;
		if (!TryGetDroppedFile(e, out string? path) || !RandomPickerImporter.IsSupported(path))
		{
			AppLogger.Info("Random picker config drop rejected: unsupported or missing file.");
			HelpText.Text = Localizer.Text(storage, "UnsupportedRosterFile");
			return;
		}

		await ImportRosterAsync(path);
		e.Handled = true;
	}

	private async Task ImportRosterAsync(string path)
	{
		if (!RandomPickerImportService.TryBeginImport(path, out string normalizedPath))
		{
			AppLogger.Info("Random picker config import ignored as already active: " + path);
			return;
		}

		if (importingRoster)
		{
			RandomPickerImportService.EndImport(normalizedPath);
			return;
		}

		importingRoster = true;
		SetImporting(true, normalizedPath, 0);
		try
		{
			SaveMembers();
			AppLogger.Info("Random picker config import started: " + normalizedPath);
			var progress = new Progress<RandomPickerImportProgress>(p => SetImportProgress(p.Percent));
			var result = await RandomPickerImportService.ImportAsync(storage, normalizedPath, progress);
			var roster = result.Roster;
			var importedMembers = result.Members;
			storage.Data.RandomPicker.Rosters.Add(roster);
			storage.Data.RandomPicker.ActiveRosterId = roster.Id;
			storage.Data.RandomPicker.Members = importedMembers;
			await storage.SaveAsync();
			LoadRosters();
			HelpText.Text = Localizer.Format(storage, "RosterImportSucceeded", roster.Name, importedMembers.Count);
			AppLogger.Info($"Random picker config import succeeded. Roster={roster.Name}, Count={importedMembers.Count}, SourceCopy={roster.SourcePath}");
		}
		catch (Exception ex)
		{
			AppLogger.Error("Random picker config import failed: " + path, ex);
			HelpText.Text = Localizer.Format(storage, "ImportFailed", ex.Message);
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
		RosterBox.IsEnabled = !isImporting;
		DeleteRosterButton.IsEnabled = !isImporting && RosterBox.SelectedItem is RandomPickerRoster;
		ResetWeightsButton.IsEnabled = !isImporting && members.Count > 0;
		OkButton.IsEnabled = !isImporting;
		CancelButton.IsEnabled = !isImporting;
		if (isImporting)
		{
			HelpText.Text = Localizer.Format(storage, "ImportingRoster", Path.GetFileName(path));
		}
	}

	private void SetImportProgress(int percent)
	{
		int clampedPercent = Math.Clamp(percent, 0, 100);
		ImportProgress.Value = clampedPercent;
		ImportProgressText.Text = clampedPercent + "%";
	}

	private void DecreaseWeight_Click(object sender, RoutedEventArgs e)
	{
		if (((FrameworkElement)sender).DataContext is MemberWeightViewModel member)
		{
			member.AdjustWeight(-0.1);
		}
	}

	private void IncreaseWeight_Click(object sender, RoutedEventArgs e)
	{
		if (((FrameworkElement)sender).DataContext is MemberWeightViewModel member)
		{
			member.AdjustWeight(0.1);
		}
	}

	private void ResetWeights_Click(object sender, RoutedEventArgs e)
	{
		foreach (var member in members)
		{
			member.ResetWeight();
		}
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

	private async void DeleteRoster_Click(object sender, RoutedEventArgs e)
	{
		if (RosterBox.SelectedItem is not RandomPickerRoster roster)
		{
			return;
		}

		var result = MessageBox.Show(this, Localizer.Format(storage, "ConfirmDeleteRoster", roster.Name), Localizer.Text(storage, "DeleteRoster"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
		if (result != MessageBoxResult.Yes)
		{
			AppLogger.Info("Random picker roster delete cancelled: " + roster.Name);
			return;
		}

		AppLogger.Info($"Random picker roster delete confirmed. Roster={roster.Name}, Id={roster.Id}, SourceCopy={roster.SourcePath}");
		SaveMembers();
		storage.Data.RandomPicker.Rosters.Remove(roster);

		var nextRoster = storage.Data.RandomPicker.Rosters.FirstOrDefault();
		storage.Data.RandomPicker.ActiveRosterId = nextRoster?.Id ?? string.Empty;
		storage.Data.RandomPicker.Members = nextRoster?.Members ?? [];
		if (initialActiveRosterId == roster.Id)
		{
			initialActiveRosterId = storage.Data.RandomPicker.ActiveRosterId;
		}

		LoadRosters();
		DeleteRosterFiles(roster);
		await storage.SaveAsync();
	}

	private void DeleteRosterFiles(RandomPickerRoster roster)
	{
		string? directory = Path.GetDirectoryName(roster.SourcePath);
		if (string.IsNullOrWhiteSpace(directory))
		{
			return;
		}

		string rostersRoot = Path.GetFullPath(Path.Combine(storage.DataDirectory, "rosters"));
		string rosterDirectory = Path.GetFullPath(directory);
		if (!rosterDirectory.StartsWith(rostersRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		try
		{
			if (Directory.Exists(rosterDirectory))
			{
				Directory.Delete(rosterDirectory, recursive: true);
				AppLogger.Info("Deleted imported roster files: " + rosterDirectory);
			}
		}
		catch (Exception ex)
		{
			AppLogger.Error("Failed to delete imported roster files: " + rosterDirectory, ex);
		}
	}

	private async void Cancel_Click(object sender, RoutedEventArgs e)
	{
		storage.Data.RandomPicker.ActiveRosterId = initialActiveRosterId;
		await CloseWithAnimationAsync();
	}

	private async void Ok_Click(object sender, RoutedEventArgs e)
	{
		accepted = true;
		SaveMembers();
		await storage.SaveAsync();
		await CloseWithAnimationAsync();
	}

	private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
	{
		Width = Math.Max(MinWidth, ActualWidth + e.HorizontalChange);
		Height = Math.Max(MinHeight, ActualHeight + e.VerticalChange);
	}

	protected override void OnClosed(EventArgs e)
	{
		if (!accepted)
		{
			storage.Data.RandomPicker.ActiveRosterId = initialActiveRosterId;
		}

		base.OnClosed(e);
	}

	private sealed class MemberWeightViewModel : INotifyPropertyChanged
	{
		private int sliderValue;

		public event PropertyChangedEventHandler? PropertyChanged;

		public MemberWeightViewModel(string name, double weight, string thumbnailPath)
		{
			Name = name;
			ThumbnailPath = thumbnailPath;
			sliderValue = WeightToSlider(weight);
		}

		public string Name { get; }

		public string ThumbnailPath { get; }

		public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailPath) && File.Exists(ThumbnailPath);

		public int SliderValue
		{
			get => sliderValue;
			set
			{
				if (sliderValue == value)
				{
					return;
				}

				sliderValue = Math.Clamp(value, 0, 100);
				OnPropertyChanged();
				OnPropertyChanged(nameof(Weight));
				OnPropertyChanged(nameof(WeightText));
			}
		}

		public double Weight => SliderToWeight(sliderValue);

		public string WeightText => Weight < 1.0 ? Weight.ToString("0.00", CultureInfo.InvariantCulture) : Weight.ToString(Math.Abs(Weight - Math.Round(Weight)) < 0.001 ? "0" : "0.0", CultureInfo.InvariantCulture);

		public void AdjustWeight(double delta)
		{
			SliderValue = WeightToSlider(Math.Clamp(Weight + delta, 0.01, 10.0));
		}

		public void ResetWeight()
		{
			SliderValue = WeightToSlider(1.0);
		}

		private static int WeightToSlider(double weight)
		{
			weight = Math.Clamp(weight, 0.01, 10.0);
			return weight <= 1.0
				? (int)Math.Round((weight - 0.01) / 0.099, MidpointRounding.AwayFromZero)
				: (int)Math.Round(weight * 10.0, MidpointRounding.AwayFromZero);
		}

		private static double SliderToWeight(int value)
		{
			value = Math.Clamp(value, 0, 100);
			return value <= 10 ? Math.Round(0.01 + value * 0.099, 2) : Math.Round(value / 10.0, 1);
		}

		private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
