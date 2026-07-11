using CommunityToolkit.Mvvm.ComponentModel;

namespace Droppa.ViewModels;

/// <summary>Shared state for view models: a busy flag, a page title, and a live header clock.</summary>
public partial class BaseViewModel : ObservableObject
{
    private System.Timers.Timer? _clock;
    private System.Timers.Timer? _poll;

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
    /// How often the page should pull fresh data on its own while it's visible. The default
    /// (<see cref="TimeSpan.Zero"/>) disables auto-refresh; override on pages like the driver job
    /// board so new requests appear without the driver tapping Refresh.
    /// </summary>
    protected virtual TimeSpan AutoRefreshInterval => TimeSpan.Zero;

    /// <summary>The periodic work run while the page is visible (typically re-fetching the list).
    /// Runs on the main thread. Override alongside <see cref="AutoRefreshInterval"/>.</summary>
    protected virtual Task AutoRefreshAsync() => Task.CompletedTask;

    /// <summary>
    /// Starts the header clock and, if the page opts in, background auto-refresh. Call from the
    /// page's OnAppearing. Idempotent, and always refreshes the time immediately so the header
    /// is never blank.
    /// </summary>
    public void StartClock()
    {
        UpdateClock();
        if (_clock is null)
        {
            _clock = new System.Timers.Timer(1000) { AutoReset = true };
            _clock.Elapsed += (_, _) => MainThread.BeginInvokeOnMainThread(UpdateClock);
            _clock.Start();
        }
        StartAutoRefresh();
    }

    /// <summary>Stops the header clock and any auto-refresh. Call from the page's OnDisappearing to avoid leaked timers.</summary>
    public void StopClock()
    {
        _clock?.Stop();
        _clock?.Dispose();
        _clock = null;

        _poll?.Stop();
        _poll?.Dispose();
        _poll = null;
    }

    private void StartAutoRefresh()
    {
        var interval = AutoRefreshInterval;
        if (interval <= TimeSpan.Zero || _poll is not null) return;
        _poll = new System.Timers.Timer(interval.TotalMilliseconds) { AutoReset = true };
        _poll.Elapsed += (_, _) => MainThread.BeginInvokeOnMainThread(async () => await AutoRefreshAsync());
        _poll.Start();
    }

    private void UpdateClock() =>
        CurrentDateTime = DateTime.Now.ToString("dddd, dd MMM yyyy  •  HH:mm:ss");
}
