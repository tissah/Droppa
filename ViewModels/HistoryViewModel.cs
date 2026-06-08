using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Droppa.Models;
using Droppa.Services;

namespace Droppa.ViewModels;

public partial class HistoryViewModel : BaseViewModel
{
    private readonly IBookingService _booking;
    private readonly IPaymentService _payments;

    public HistoryViewModel(IBookingService booking, IPaymentService payments)
    {
        _booking = booking;
        _payments = payments;
        Title = "My deliveries";
    }

    public ObservableCollection<Booking> Bookings { get; } = [];

    [ObservableProperty] private string? _errorMessage;

    [RelayCommand]
    private async Task TrackAsync(Booking? booking)
    {
        if (booking is null || !booking.CanTrack) return;
        await Shell.Current.GoToAsync($"track?deliveryId={booking.DeliveryId}");
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CancelAsync(Booking? booking)
    {
        if (booking is null || !booking.CanCancel || IsBusy) return;

        var pickedUp = CancellationPolicy.IsPickedUp(booking.ServerStatus);
        var refund = booking.RefundIfCancelled;
        var fee = CancellationPolicy.CancellationFee(booking.ServerStatus, booking.TotalFee);

        // Spell out the refund consequences before the customer commits.
        var prompt = pickedUp
            ? "The parcel has already been picked up, so this cancellation is non-refundable. Cancel the trip anyway?"
            : refund > 0
                ? $"A {CancellationPolicy.CancellationFeePercent:N0}% cancellation fee (MWK {fee:N0}) applies. "
                  + $"MWK {refund:N0} of your MWK {booking.TotalFee:N0} trip charge will be refunded to you. Cancel the trip?"
                : "Cancel this trip?";

        var confirmed = await Shell.Current.DisplayAlert("Cancel trip", prompt, "Cancel trip", "Keep trip");
        if (!confirmed) return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;

            await _booking.CancelAsync(booking);

            string message;
            if (refund > 0)
            {
                var result = await _payments.RefundAsync(refund);
                message = result.Success
                    ? $"Your trip was cancelled and MWK {refund:N0} has been refunded to you. Ref: {result.TransactionId}"
                    : $"Your trip was cancelled, but the refund could not be processed: {result.Message}";
            }
            else
            {
                message = pickedUp
                    ? "Your trip was cancelled. As the parcel had already been picked up, no refund applies."
                    : "Your trip was cancelled.";
            }

            await LoadAsync();
            await Shell.Current.DisplayAlert("Trip cancelled", message, "OK");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadAsync()
    {
        var history = await _booking.GetHistoryAsync();
        Bookings.Clear();
        foreach (var booking in history)
            Bookings.Add(booking);
    }
}
