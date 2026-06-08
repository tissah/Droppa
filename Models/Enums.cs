namespace Droppa.Models;

/// <summary>The two top-level services offered by Droppa.</summary>
public enum ServiceType
{
    SendParcel,
    ReceiveParcel
}

/// <summary>Lifecycle of a delivery, mirroring section 7 of the spec.</summary>
public enum DeliveryStatus
{
    Pending,
    Accepted,
    PickupInProgress,
    ParcelCollected,
    InTransit,
    Delivered,
    Cancelled
}

/// <summary>Distinguishes the kind of account.</summary>
public enum UserRole
{
    Customer,
    Driver,
    Administrator
}
