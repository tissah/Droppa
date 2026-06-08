using CommunityToolkit.Mvvm.ComponentModel;

namespace Droppa.ViewModels;

/// <summary>Shared state for view models: a busy flag, a page title, and a live header clock.</summary>
public partial class BaseViewModel : ObservableObject
{
    private System.Timers.Timer? _clock;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>Current date and time for the page header. Refreshed every second while the clock runs.</summary>
    [ObservableProperty]
    private string _currentDateTime = string.Empty;

    public bool IsNotBusy => !IsBusy;

    /// <summary>
    /// Starts the header clock. Call from the page's OnAppearing. Idempotent, and always refreshes
    /// the time immediately so the header is never blank.
    /// </summary>
    public void StartClock()
    {
        UpdateClock();
        if (_clock is not null) return;
        _clock = new System.Timers.Timer(1000) { AutoReset = true };
        _clock.Elapsed += (_, _) => MainThread.BeginInvokeOnMainThread(UpdateClock);
        _clock.Start();
    }

    /// <summary>Stops the header clock. Call from the page's OnDisappearing to avoid a leaked timer.</summary>
    public void StopClock()
    {
        _clock?.Stop();
        _clock?.Dispose();
        _clock = null;
    }

    private void UpdateClock() =>
        CurrentDateTime = DateTime.Now.ToString("dddd, dd MMM yyyy  •  HH:mm:ss");
}
