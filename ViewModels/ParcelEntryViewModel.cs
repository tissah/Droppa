using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Droppa.Models;

namespace Droppa.ViewModels;

/// <summary>
/// One editable parcel line on the Send page. Each parcel has its own item details and
/// receiver. The distance/ride fee is charged once for the whole booking; the weight-based
/// parcel charge is added later by the driver after weighing, and paid separately.
/// </summary>
public partial class ParcelEntryViewModel : ObservableObject
{
    public ParcelEntryViewModel(int number)
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
    public event Action<ParcelEntryViewModel>? RemoveRequested;

    [RelayCommand]
    private void Remove() => RemoveRequested?.Invoke(this);

    // --- Item details ---
    [ObservableProperty] private string _itemName = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private int _quantity = 1;
    [ObservableProperty] private string? _specialInstructions;

    // --- Receiver (one per parcel) ---
    [ObservableProperty] private string _receiverName = string.Empty;
    [ObservableProperty] private string _receiverPhone = string.Empty;

    /// <summary>Snapshots this line into a <see cref="Parcel"/> for quoting and submission.</summary>
    public Parcel ToParcel() => new()
    {
        ItemName = ItemName.Trim(),
        Description = Description?.Trim() ?? string.Empty,
        Quantity = Quantity,
        SpecialInstructions = string.IsNullOrWhiteSpace(SpecialInstructions) ? null : SpecialInstructions.Trim(),
        ReceiverName = ReceiverName.Trim(),
        ReceiverPhone = ReceiverPhone.Trim()
    };
}
