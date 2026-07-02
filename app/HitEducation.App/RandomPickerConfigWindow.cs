using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace HitEducation.App;

public partial class RandomPickerConfigWindow : Window
{
	private readonly AppStorage storage;
	private readonly ObservableCollection<MemberWeightViewModel> members;
	private bool loadingRoster;
	private bool closingWithAnimation;

	public RandomPickerConfigWindow(AppStorage storage)
	{
		InitializeComponent();
		this.storage = storage;
		members = new ObservableCollection<MemberWeightViewModel>();
		MemberItems.ItemsSource = members;
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
		CancelButton.Content = Localizer.Text(storage, "Cancel");
		OkButton.Content = Localizer.Text(storage, "OK");
	}

	private void LoadRosters()
	{
		loadingRoster = true;
		RosterBox.ItemsSource = storage.Data.RandomPicker.Rosters;
		RosterBox.SelectedItem = storage.Data.RandomPicker.Rosters.FirstOrDefault(x => x.Id == storage.Data.RandomPicker.ActiveRosterId);
		loadingRoster = false;
		LoadMembers();
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
		LoadMembers();
	}

	private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.LeftButton == MouseButtonState.Pressed)
		{
			DragMove();
		}
	}

	private async void Cancel_Click(object sender, RoutedEventArgs e)
	{
		await CloseWithAnimationAsync();
	}

	private async void Ok_Click(object sender, RoutedEventArgs e)
	{
		SaveMembers();
		await storage.SaveAsync();
		await CloseWithAnimationAsync();
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

		public int SliderValue
		{
			get => sliderValue;
			set
			{
				if (sliderValue == value)
				{
					return;
				}

			sliderValue = Math.Clamp(value, 0, 60);
				OnPropertyChanged();
				OnPropertyChanged(nameof(Weight));
				OnPropertyChanged(nameof(WeightText));
			}
		}

		public double Weight => SliderToWeight(sliderValue);

		public string WeightText => Weight < 1.0 ? Weight.ToString("0.00", CultureInfo.InvariantCulture) : Weight.ToString("0", CultureInfo.InvariantCulture);

		private static int WeightToSlider(double weight)
		{
			weight = Math.Clamp(weight, 0.01, 10.0);
			return weight < 1.0 ? (int)Math.Round((weight - 0.01) / 0.099) : 10 + (int)Math.Round(weight * 5.0);
		}

		private static double SliderToWeight(int value)
		{
			value = Math.Clamp(value, 0, 60);
			return value <= 10 ? Math.Round(0.01 + value * 0.099, 2) : Math.Round((value - 10) / 5.0, 0);
		}

		private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
