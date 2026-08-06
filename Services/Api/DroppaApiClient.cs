using System.Net.Http.Json;
using System.Text.Json;

namespace Droppa.Services.Api;

/// <summary>Thrown when the API returns a non-success response.</summary>
public class DroppaApiException(string message) : Exception(message);

/// <summary>
/// Typed client over the Droppa REST API. Attaches the bearer token to authenticated
/// calls and surfaces server error messages as <see cref="DroppaApiException"/>.
/// </summary>
public class DroppaApiClient
{
    private readonly HttpClient _http;
    private readonly ITokenStore _tokens;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public DroppaApiClient(HttpClient http, ITokenStore tokens)
    {
        _http = http;
        _tokens = tokens;
    }

    /// <summary>
    /// Raised when an authenticated request is rejected with 401 Unauthorized — i.e. the session
    /// has expired or been revoked. A central guard signs the user out and returns them to login.
    /// </summary>
    public event Action? Unauthorized;

    // ---- Auth (anonymous) ----
    public Task<AuthResponseDto> RegisterAsync(RegisterRequestDto body, CancellationToken ct = default) =>
        PostAsync<AuthResponseDto>("/api/Auth/register", body, auth: false, ct);

    public Task<AuthResponseDto> LoginAsync(LoginRequestDto body, CancellationToken ct = default) =>
        PostAsync<AuthResponseDto>("/api/Auth/login", body, auth: false, ct);

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        using var req = Build(HttpMethod.Post, "/api/Auth/logout", new RefreshRequestDto(refreshToken), auth: false);
        await _http.SendAsync(req, ct); // best-effort
    }

    // ---- Lookups / customer (authenticated) ----
    public Task<List<DistrictDto>> GetDistrictsAsync(CancellationToken ct = default) =>
        GetAsync<List<DistrictDto>>("/api/districts", ct);

    public Task<List<CourierServiceDto>> GetCourierServicesAsync(CancellationToken ct = default) =>
        GetAsync<List<CourierServiceDto>>("/api/courier-services", ct);

    /// <summary>
    /// Bookable courier branches — only branches of active couriers in served districts.
    /// Pass <paramref name="districtId"/> to get just the branches in one district.
    /// </summary>
    public Task<List<CourierBranchDto>> GetCourierBranchesAsync(
        int? districtId = null, int? courierId = null, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (districtId is > 0) query.Add($"districtId={districtId}");
        if (courierId is > 0) query.Add($"courierId={courierId}");
        var path = "/api/courier-branches" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
        return GetAsync<List<CourierBranchDto>>(path, ct);
    }

    public Task<PricingDto> GetPricingAsync(CancellationToken ct = default) =>
        GetAsync<PricingDto>("/api/pricing", ct);

    public Task<QuoteDto> QuoteSendAsync(CreateSendDto body, CancellationToken ct = default) =>
        PostAsync<QuoteDto>("/api/deliveries/quote/send", body, auth: true, ct);

    public Task<DeliveryDto> CreateSendAsync(CreateSendDto body, CancellationToken ct = default) =>
        PostAsync<DeliveryDto>("/api/deliveries/send", body, auth: true, ct);

    public Task<DeliveryDto> CreateReceiveAsync(CreateReceiveDto body, CancellationToken ct = default) =>
        PostAsync<DeliveryDto>("/api/deliveries/receive", body, auth: true, ct);

    public Task<List<DeliveryDto>> GetDeliveriesAsync(CancellationToken ct = default) =>
        GetAsync<List<DeliveryDto>>("/api/deliveries", ct);

    public Task<DeliveryDto> GetDeliveryAsync(int id, CancellationToken ct = default) =>
        GetAsync<DeliveryDto>($"/api/deliveries/{id}", ct);

    public Task<TrackingDto> GetTrackingAsync(int id, CancellationToken ct = default) =>
        GetAsync<TrackingDto>($"/api/deliveries/{id}/tracking", ct);

    /// <summary>Customer: cancel a delivery that hasn't been delivered yet. Returns the updated delivery.</summary>
    public async Task<DeliveryDto> CancelDeliveryAsync(int id, CancellationToken ct = default)
    {
        using var req = Build(HttpMethod.Post, $"/api/deliveries/{id}/cancel", body: null, auth: true);
        return await SendAsync<DeliveryDto>(req, ct);
    }

    // ---- Driver (authenticated, Driver role) ----
    public Task<DriverProfileDto> GetDriverMeAsync(CancellationToken ct = default) =>
        GetAsync<DriverProfileDto>("/api/driver/me", ct);

    public Task<List<DriverJobDto>> GetDriverJobsAsync(CancellationToken ct = default) =>
        GetAsync<List<DriverJobDto>>("/api/driver/jobs", ct);

    public Task<List<DeliveryDto>> GetDriverDeliveriesAsync(CancellationToken ct = default) =>
        GetAsync<List<DeliveryDto>>("/api/driver/deliveries", ct);

    public Task<DeliveryDto> AcceptRideAsync(int deliveryRequestId, CancellationToken ct = default) =>
        PostAsync<DeliveryDto>("/api/driver/rides/accept", new RideActionDto(deliveryRequestId), auth: true, ct);

    public async Task RejectRideAsync(int deliveryRequestId, CancellationToken ct = default)
    {
        using var req = Build(HttpMethod.Post, "/api/driver/rides/reject", new RideActionDto(deliveryRequestId), auth: true);
        await SendAsync<string>(req, ct);
    }

    public async Task UpdateDriverLocationAsync(UpdateLocationDto body, CancellationToken ct = default)
    {
        using var req = Build(HttpMethod.Post, "/api/driver/location", body, auth: true);
        await SendAsync<string>(req, ct);
    }

    public async Task UpdateDeliveryStatusAsync(UpdateDeliveryStatusDto body, CancellationToken ct = default)
    {
        using var req = Build(HttpMethod.Post, "/api/driver/deliveries/status", body, auth: true);
        await SendAsync<string>(req, ct);
    }

    // ---- Parcel weight & charge payment (customer) ----

    /// <summary>
    /// Customer: record the parcel weight they entered and the resulting weight-based charge, so the
    /// combined total (ride + parcel) can be paid and the driver can then collect. Backend must
    /// accept this on the customer role (mirrors the driver's parcel-weight endpoint).
    /// </summary>
    public async Task SetParcelWeightByCustomerAsync(SetParcelWeightDto body, CancellationToken ct = default)
    {
        using var req = Build(HttpMethod.Post, "/api/deliveries/parcel-weight", body, auth: true);
        await SendAsync<string>(req, ct);
    }

    public async Task PayParcelChargeAsync(ParcelPaymentDto body, CancellationToken ct = default)
    {
        using var req = Build(HttpMethod.Post, "/api/deliveries/parcel-payment", body, auth: true);
        await SendAsync<string>(req, ct);
    }

    // ---- plumbing ----
    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var req = Build(HttpMethod.Get, path, body: null, auth: true);
        return await SendAsync<T>(req, ct);
    }

    private async Task<T> PostAsync<T>(string path, object body, bool auth, CancellationToken ct)
    {
        using var req = Build(HttpMethod.Post, path, body, auth);
        return await SendAsync<T>(req, ct);
    }

    private HttpRequestMessage Build(HttpMethod method, string path, object? body, bool auth)
    {
        var req = new HttpRequestMessage(method, path);
        if (body is not null) req.Content = JsonContent.Create(body);
        if (auth && !string.IsNullOrEmpty(_tokens.AccessToken))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _tokens.AccessToken);
        return req;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage req, CancellationToken ct)
    {
        HttpResponseMessage res;
        try
        {
            res = await _http.SendAsync(req, ct);
        }
        catch (Exception ex)
        {
            throw new DroppaApiException($"Unable to connect to the server. {ex.Message}");
        }

        var content = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            // An authenticated request rejected with 401 means the session is no longer valid.
            // (We ignore 401s on anonymous calls like a failed login, which aren't session expiry.)
            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized && req.Headers.Authorization is not null)
                Unauthorized?.Invoke();
            throw new DroppaApiException(ExtractError(content, res.StatusCode));
        }

        if (typeof(T) == typeof(string)) return (T)(object)content;
        var value = JsonSerializer.Deserialize<T>(content, Json);
        return value ?? throw new DroppaApiException("The server returned an empty response.");
    }

    private static string ExtractError(string content, System.Net.HttpStatusCode status)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("error", out var e) && e.GetString() is { } msg)
                return msg;
        }
        catch { /* not JSON */ }

        return status == System.Net.HttpStatusCode.Unauthorized
            ? "Your session has expired. Please sign in again."
            : $"Request failed ({(int)status}).";
    }
}
