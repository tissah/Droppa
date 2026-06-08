namespace Droppa.Services.Api;

/// <summary>
/// API connection settings. The default targets the Droppa API running on the
/// developer machine, reachable from the Android emulator via 10.0.2.2.
/// </summary>
public static class ApiConfig
{
    /// <summary>
    /// Base URL of the Droppa API.
    /// • Android emulator → http://10.0.2.2:5080 (10.0.2.2 maps to the host's localhost)
    /// • Physical device   → http://&lt;your-PC-LAN-IP&gt;:5080
    /// • HTTPS in prod      → https://api.droppa.mw
    /// </summary>
    public const string BaseUrl = "http://192.168.1.198:8050";
}
