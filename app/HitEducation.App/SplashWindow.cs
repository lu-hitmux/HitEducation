using System.Threading.Tasks;
using System.Windows;

namespace HitEducation.App;

public partial class SplashWindow : Window
{
	public SplashWindow()
	{
		InitializeComponent();
		WindowOpenAnimation.Play(SplashRoot);
	}

	public void ApplyLanguage(AppStorage storage)
	{
		TitleText.Text = Localizer.Text(storage, "SplashTitle");
		DetailText.Text = Localizer.Text(storage, "SplashDetail");
	}

	public async Task CloseWithAnimationAsync()
	{
		await WindowOpenAnimation.PlayCloseAsync(SplashRoot);
		Close();
	}
}
