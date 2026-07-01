using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HitEducation.App;

public static class WindowOpenAnimation
{
	private static readonly Duration AnimationDuration = new(TimeSpan.FromMilliseconds(350));

	private sealed class AnimationState
	{
		public int Version { get; set; }
	}

	private static readonly ConditionalWeakTable<FrameworkElement, AnimationState> AnimationStates = new();

	public static void Play(FrameworkElement element)
	{
		int version = NextVersion(element);
		element.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
		ScaleTransform scale = EnsureScaleTransform(element, 0.02);

		if (element.IsLoaded)
		{
			BeginOpen(element, scale, version);
		}
		else
		{
			element.Opacity = 0;
			scale.ScaleY = 0.02;
			RoutedEventHandler? loaded = null;
			loaded = (_, _) =>
			{
				element.Loaded -= loaded;
				if (IsCurrent(element, version))
				{
					BeginOpen(element, scale, version);
				}
			};
			element.Loaded += loaded;
		}
	}

	private static void BeginOpen(FrameworkElement element, ScaleTransform scale, int version)
	{
		element.CacheMode = new BitmapCache();
		var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
		var scaleAnimation = new DoubleAnimation
		{
			From = scale.ScaleY,
			To = 1,
			Duration = AnimationDuration,
			EasingFunction = ease
		};
		var opacityAnimation = new DoubleAnimation
		{
			From = element.Opacity,
			To = 1,
			Duration = AnimationDuration,
			EasingFunction = ease
		};
		opacityAnimation.Completed += (_, _) =>
		{
			if (!IsCurrent(element, version))
			{
				return;
			}

			scale.ScaleY = 1;
			element.Opacity = 1;
			scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
			element.BeginAnimation(UIElement.OpacityProperty, null);
			element.CacheMode = null;
		};
		scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
		element.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
	}

	public static async Task PlayCloseAsync(FrameworkElement element)
	{
		int version = NextVersion(element);
		element.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
		ScaleTransform scale = EnsureScaleTransform(element, 1);

		element.CacheMode = new BitmapCache();
		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
		var scaleAnimation = new DoubleAnimation
		{
			From = scale.ScaleY,
			To = 0.02,
			Duration = AnimationDuration,
			EasingFunction = ease
		};
		scaleAnimation.Completed += (_, _) => completion.TrySetResult();
		var opacityAnimation = new DoubleAnimation
		{
			From = element.Opacity,
			To = 0,
			Duration = AnimationDuration,
			EasingFunction = ease
		};
		scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
		element.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);

		await completion.Task;
		if (!IsCurrent(element, version))
		{
			return;
		}

		scale.ScaleY = 0.02;
		element.Opacity = 0;
		scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
		element.BeginAnimation(UIElement.OpacityProperty, null);
		element.CacheMode = null;
	}

	private static int NextVersion(FrameworkElement element)
	{
		AnimationState state = AnimationStates.GetOrCreateValue(element);
		return ++state.Version;
	}

	private static bool IsCurrent(FrameworkElement element, int version)
	{
		return AnimationStates.TryGetValue(element, out AnimationState? state) && state.Version == version;
	}

	private static ScaleTransform EnsureScaleTransform(FrameworkElement element, double fallbackScaleY)
	{
		if (element.RenderTransform is ScaleTransform scale)
		{
			return scale;
		}

		scale = new ScaleTransform(1, fallbackScaleY);
		element.RenderTransform = scale;
		return scale;
	}
}
