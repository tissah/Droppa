namespace Droppa.Models;

/// <summary>
/// Describes the item being moved. Different fields are required depending on the
/// service: "Send" needs item details; "Receive" needs a waybill or a receipt image.
/// </summary>
public class Parcel
{
    // --- Used when SENDING a parcel ---
    public string ItemName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public string? SpecialInstructions { get; set; }

    /// <summary>Who the parcel is going to.</summary>
    public string? ReceiverName { get; set; }

    /// <summary>The receiver's contact number.</summary>
    public string? ReceiverPhone { get; set; }

    // --- Used when RECEIVING a parcel ---
    public string? WaybillNumber { get; set; }
    public string? ReceiptImagePath { get; set; }
}
