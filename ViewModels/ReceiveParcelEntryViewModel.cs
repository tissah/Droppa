using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Droppa.Models;

namespace Droppa.ViewModels;

/// <summary>
/// One editable parcel line on the Receive page. Each parcel is collected from a different
/// sender and is identified by a waybill number or an attached receipt image. Receiving price
/// is unaffected by the number of parcels, so this line carries no charge of its own.
/// </summary>
public partial class ReceiveParcelEntryViewModel : ObservableObject
{
    public ReceiveParcelEntryViewModel(int number)
    {
        Number = number;
    }

    /// <summary>1-based position used as the card heading ("Parcel 1", "Parcel 2"…).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Heading))]
    private int _number;

    public string Heading => $"Parcel {Number}";

    /// <summary>True when this parcel may be removed (more than one parcel in the booking).</summary>
    [ObservableProperty] private bool _canRemove;

    /// <summary>Raised when the user taps Remove; the owning view model handles the removal.</summary>
    public event Action<ReceiveParcelEntryViewModel>? RemoveRequested;

    [RelayCommand]
    private void Remove() => RemoveRequested?.Invoke(this);

    /// <summary>Who the parcel is coming from.</summary>
    [ObservableProperty] private string _senderName = string.Empty;

    [ObservableProperty] private string? _waybillNumber;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReceipt))]
    private string? _receiptImagePath;

    [ObservableProperty] private string? _statusMessage;

    public bool HasReceipt => !string.IsNullOrEmpty(ReceiptImagePath);

    /// <summary>True when this parcel can be identified — it has a waybill number or a receipt image.</summary>
    public bool HasProof => !string.IsNullOrWhiteSpace(WaybillNumber) || HasReceipt;

    [RelayCommand]
    private async Task PickReceiptAsync()
    {
        try
        {
            var photo = await MediaPicker.Default.PickPhotoAsync();
            if (photo is not null)
            {
                ReceiptImagePath = photo.FullPath;
                StatusMessage = $"Receipt attached: {photo.FileName}";
            }
        }
        catch (FeatureNotSupportedException)
        {
            StatusMessage = "Photo picking isn't supported on this device.";
        }
    }

    /// <summary>Snapshots this line into a <see cref="Parcel"/> for quoting and submission.</summary>
    public Parcel ToParcel() => new()
    {
        SenderName = SenderName.Trim(),
        WaybillNumber = string.IsNullOrWhiteSpace(WaybillNumber) ? null : WaybillNumber.Trim(),
        ReceiptImagePath = ReceiptImagePath
    };
}
