namespace Droppa.Models;

/// <summary>One step in the customer-facing order summary (e.g. "In transit").</summary>
public class OrderStage
{
    public required string Title { get; init; }

    /// <summary>True once the delivery has reached or passed this step.</summary>
    public bool Done { get; init; }

    /// <summary>True for the single step currently in progress.</summary>
    public bool IsCurrent { get; init; }

    /// <summary>✓ for done, ◉ for the step in progress, ○ for steps not yet reached.</summary>
    public string Glyph => Done ? "✓" : IsCurrent ? "◉" : "○";

    /// <summary>Done or in-progress steps are emphasised; steps not yet reached are muted.</summary>
    public bool Highlight => Done || IsCurrent;
}

/// <summary>
/// Builds the order summary the customer sees: order placed → accepted → parcel taken →
/// in transit → arrived → delivered. Driven by the API's numeric DeliveryStatus so it stays
/// in step with what the driver is doing.
/// </summary>
public static class OrderTimeline
{
    // Each customer-facing step and the API DeliveryStatus code that marks it reached.
    private static readonly (string Title, int Reached)[] Steps =
    [
        ("Order placed", 0),
        ("Accepted", 2),
        ("Parcel taken", 5),
        ("In transit", 6),
        ("Arrived", 7),
        ("Delivered", 8),
    ];

    /// <summary>Rejected (3) or Cancelled (9): the order didn't proceed.</summary>
    public static bool IsCancelled(int status) => status is 3 or 9;

    /// <summary>True once a driver has confirmed (accepted) the trip and it's still active.</summary>
    public static bool DriverConfirmed(int status) => status is 2 or 4 or 5 or 6 or 7 or 8;

    public static IReadOnlyList<OrderStage> Build(int status)
    {
        var cancelled = IsCancelled(status);
        var stages = new List<OrderStage>(Steps.Length);
        var currentTaken = false;

        foreach (var (title, reached) in Steps)
        {
            var done = !cancelled && status >= reached;
            // The first not-yet-done step is the one in progress.
            var isCurrent = !cancelled && !done && !currentTaken;
            if (isCurrent) currentTaken = true;
            stages.Add(new OrderStage { Title = title, Done = done, IsCurrent = isCurrent });
        }

        return stages;
    }
}
