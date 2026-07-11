namespace Droppa.Services.Maps;

/// <summary>
/// Google Maps platform settings.
/// <para>
/// Two things must be configured for live maps + routing to work:
/// </para>
/// <list type="number">
/// <item>The <b>Maps SDK for Android</b> key in <c>Platforms/Android/AndroidManifest.xml</c>
///       (the <c>com.google.android.geo.API_KEY</c> meta-data) — this renders the map tiles.</item>
/// <item>The <b>Directions API</b> key below — used to compute the pickup→destination route.</item>
/// </list>
/// The same key can serve both if "Maps SDK for Android" and "Directions API" are enabled on it
/// and the Google Cloud project has billing turned on.
/// </summary>
public static class GoogleMapsConfig
{
    /// <summary>
    /// Key used for the Directions API REST calls. This is the same key set for the Maps SDK in
    /// <c>Platforms/Android/AndroidManifest.xml</c> — it needs "Directions API" enabled on it too.
    /// </summary>
    public const string DirectionsApiKey = "AIzaSyCQNO0FPWAQOpku0E27ecOEKKFJPxEzFx8";

    /// <summary>True once a real key has been set (used to skip routing gracefully in dev).</summary>
    public static bool HasDirectionsKey =>
        !string.IsNullOrWhiteSpace(DirectionsApiKey) && DirectionsApiKey != "YOUR_GOOGLE_DIRECTIONS_API_KEY";
}
