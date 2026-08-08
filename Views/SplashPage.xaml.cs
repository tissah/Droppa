using Microsoft.Maui.Graphics;

namespace Droppa.Views;

/// <summary>
/// First page the app shows once the runtime is up. It repeats the launch splash (Droppa logo
/// on white) so there is no visual jump, adds a spinning ring, then hands over to login.
/// </summary>
public partial class SplashPage : ContentPage
{
    // How long the branded screen stays up before navigating on.
    private static readonly TimeSpan MinimumDisplay = TimeSpan.FromSeconds(1.8);

    private CancellationTokenSource? _spinCts;

    public SplashPage()
    {
        InitializeComponent();
        Spinner.Drawable = new SpinnerRingDrawable();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _spinCts = new CancellationTokenSource();
        _ = SpinAsync(_spinCts.Token);

        await Task.Delay(MinimumDisplay);

        if (Shell.Current is not null)
            await Shell.Current.GoToAsync("//login");
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _spinCts?.Cancel();
        _spinCts = null;
    }

    private async Task SpinAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                Spinner.Rotation = 0;
                await Spinner.RotateTo(360, 900, Easing.Linear);
            }
        }
        catch (TaskCanceledException)
        {
            // Page went away mid-animation; nothing to do.
        }
    }

    /// <summary>Grey track with a brand-blue arc riding on it — rotated by the page to spin.</summary>
    private sealed class SpinnerRingDrawable : IDrawable
    {
        private const float Thickness = 6f;
        private const float SweepDegrees = 100f;

        private static readonly Color Track = Color.FromArgb("#E3ECF8");
        private static readonly Color Arc = Color.FromArgb("#0A62D0");

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            // Inset by half the stroke so the ring isn't clipped at the edges.
            var inset = (Thickness / 2f) + 1f;
            var size = Math.Min(dirtyRect.Width, dirtyRect.Height) - (inset * 2f);
            if (size <= 0)
                return;

            var x = dirtyRect.Center.X - (size / 2f);
            var y = dirtyRect.Center.Y - (size / 2f);

            canvas.StrokeSize = Thickness;
            canvas.StrokeLineCap = LineCap.Round;

            canvas.StrokeColor = Track;
            canvas.DrawEllipse(x, y, size, size);

            // Angles are degrees counter-clockwise from 3 o'clock; sweeping clockwise from the
            // top puts the head of the arc where the eye expects it.
            canvas.StrokeColor = Arc;
            canvas.DrawArc(x, y, size, size, 90f, 90f - SweepDegrees, clockwise: true, closed: false);
        }
    }
}
