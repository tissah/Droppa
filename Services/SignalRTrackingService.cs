using Droppa.Services.Api;
using Microsoft.AspNetCore.SignalR.Client;

namespace Droppa.Services;

/// <summary>
/// <see cref="ITrackingService"/> implemented over the API's <c>/hubs/tracking</c> SignalR hub.
/// The JWT is supplied via the access-token provider (the hub accepts it on the query string).
/// </summary>
public class SignalRTrackingService : ITrackingService
{
    private readonly ITokenStore _tokens;
    private HubConnection? _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SignalRTrackingService(ITokenStore tokens) => _tokens = tokens;

    public event Action<DriverLocationUpdate>? DriverLocationUpdated;
    public event Action<RideAcceptedInfo>? RideAccepted;
    public event Action<DeliveryStatusUpdate>? DeliveryStatusChanged;
    public event Action<ParcelChargeRequest>? ParcelChargeRequested;

    public async Task EnsureConnectedAsync(CancellationToken ct = default)
    {
        if (_connection is { State: HubConnectionState.Connected }) return;

        await _gate.WaitAsync(ct);
        try
        {
            if (_connection is null)
            {
                var hubUrl = ApiConfig.BaseUrl.TrimEnd('/') + "/hubs/tracking";
                _connection = new HubConnectionBuilder()
                    .WithUrl(hubUrl, options =>
                        options.AccessTokenProvider = () => Task.FromResult<string?>(_tokens.AccessToken))
                    .WithAutomaticReconnect()
                    .Build();

                RegisterHandlers(_connection);
            }

            if (_connection.State == HubConnectionState.Disconnected)
                await _connection.StartAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SubscribeToDeliveryAsync(int deliveryRequestId, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        await _connection!.InvokeAsync("SubscribeToDelivery", deliveryRequestId, ct);
    }

    public async Task UnsubscribeFromDeliveryAsync(int deliveryRequestId, CancellationToken ct = default)
    {
        if (_connection is { State: HubConnectionState.Connected })
            await _connection.InvokeAsync("UnsubscribeFromDelivery", deliveryRequestId, ct);
    }

    public async Task StopAsync()
    {
        if (_connection is not null)
        {
            await _connection.StopAsync();
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    private void RegisterHandlers(HubConnection connection)
    {
        // Payloads are anonymous objects on the server; capture them as loosely-typed DTOs.
        connection.On<LocationPayload>("DriverLocationUpdated", p =>
            DriverLocationUpdated?.Invoke(new DriverLocationUpdate(p.DeliveryRequestId, p.Lat, p.Lng, p.EtaMinutes)));

        connection.On<RideAcceptedPayload>("RideAccepted", p =>
            RideAccepted?.Invoke(new RideAcceptedInfo(
                p.DeliveryRequestId, p.DriverName, p.DriverPhone, p.DriverPhoto, p.Motorcycle, p.Registration)));

        connection.On<StatusPayload>("DeliveryStatusChanged", p =>
            DeliveryStatusChanged?.Invoke(new DeliveryStatusUpdate(p.DeliveryRequestId, p.Status)));

        connection.On<ParcelChargePayload>("ParcelChargeRequested", p =>
            ParcelChargeRequested?.Invoke(new ParcelChargeRequest(p.DeliveryRequestId)));
    }

    // Wire shapes mirroring the server's anonymous payloads (case-insensitive by default in SignalR JSON).
    private record LocationPayload(int DeliveryRequestId, double Lat, double Lng, double? EtaMinutes);
    private record RideAcceptedPayload(
        int DeliveryRequestId, string? DriverName, string? DriverPhone,
        string? DriverPhoto, string? Motorcycle, string? Registration);
    private record StatusPayload(int DeliveryRequestId, string Status);
    private record ParcelChargePayload(int DeliveryRequestId);
}
