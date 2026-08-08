using System.Globalization;
using System.Text.Json;
using Microsoft.Maui.Devices.Sensors;

namespace Droppa.Services.Maps;

/// <summary>
/// <see cref="IDirectionsService"/> over the Google Directions API. Requests motorcycle routing
/// (<c>mode=two_wheeler</c>) with alternatives and returns them shortest-first. If the motorcycle
/// mode isn't available for the region, it transparently retries with <c>driving</c>.
/// </summary>
public class GoogleDirectionsService : IDirectionsService
{
    private readonly HttpClient _http;

    public GoogleDirectionsService(HttpClient http) => _http = http;

    public async Task<RouteResult?> GetRouteAsync(
        double originLat, double originLng,
        double destLat, double destLng,
        CancellationToken ct = default)
    {
        var routes = await GetRoutesAsync(originLat, originLng, destLat, destLng, ct);
        return routes.Count > 0 ? routes[0] : null;
    }

    public async Task<IReadOnlyList<RouteResult>> GetRoutesAsync(
        double originLat, double originLng,
        double destLat, double destLng,
        CancellationToken ct = default)
    {
        if (!GoogleMapsConfig.HasDirectionsKey) return Array.Empty<RouteResult>();

        // Motorcycle first; fall back to driving where two_wheeler isn't supported.
        var routes = await QueryAsync(originLat, originLng, destLat, destLng, "two_wheeler", ct);
        if (routes.Count == 0)
            routes = await QueryAsync(originLat, originLng, destLat, destLng, "driving", ct);

        return routes;
    }

    private async Task<IReadOnlyList<RouteResult>> QueryAsync(
        double originLat, double originLng, double destLat, double destLng, string mode, CancellationToken ct)
    {
        var ci = CultureInfo.InvariantCulture;
        var url = "https://maps.googleapis.com/maps/api/directions/json" +
                  $"?origin={originLat.ToString(ci)},{originLng.ToString(ci)}" +
                  $"&destination={destLat.ToString(ci)},{destLng.ToString(ci)}" +
                  $"&mode={mode}&alternatives=true&key={GoogleMapsConfig.DirectionsApiKey}";

        try
        {
            using var res = await _http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return Array.Empty<RouteResult>();

            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            if (root.GetProperty("status").GetString() != "OK") return Array.Empty<RouteResult>();
            if (!root.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0)
                return Array.Empty<RouteResult>();

            // Keep every alternative — the map draws the shortest as the chosen route and the rest
            // behind it in a lighter shade, the way Google Maps presents them.
            var results = new List<RouteResult>();
            foreach (var route in routes.EnumerateArray())
            {
                long meters = 0, seconds = 0;
                foreach (var leg in route.GetProperty("legs").EnumerateArray())
                {
                    meters += leg.GetProperty("distance").GetProperty("value").GetInt64();
                    seconds += leg.GetProperty("duration").GetProperty("value").GetInt64();
                }

                var encoded = route.GetProperty("overview_polyline").GetProperty("points").GetString();
                if (string.IsNullOrEmpty(encoded)) continue;

                var summary = route.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
                results.Add(new RouteResult(
                    DecodePolyline(encoded),
                    Math.Round(meters / 1000.0, 2),
                    Math.Round(seconds / 60.0, 1),
                    mode,
                    summary));
            }

            // Shortest by distance first: that's the one the driver is asked to take.
            results.Sort((a, b) => a.DistanceKm.CompareTo(b.DistanceKm));
            return results;
        }
        catch
        {
            return Array.Empty<RouteResult>();
        }
    }

    /// <summary>
    /// Decodes a Google "encoded polyline algorithm" string into coordinates.
    /// See https://developers.google.com/maps/documentation/utilities/polylinealgorithm
    /// </summary>
    private static List<Location> DecodePolyline(string encoded)
    {
        var points = new List<Location>();
        int index = 0, lat = 0, lng = 0;

        while (index < encoded.Length)
        {
            lat += DecodeValue(encoded, ref index);
            lng += DecodeValue(encoded, ref index);
            points.Add(new Location(lat / 1e5, lng / 1e5));
        }
        return points;
    }

    private static int DecodeValue(string encoded, ref int index)
    {
        int shift = 0, result = 0, b;
        do
        {
            b = encoded[index++] - 63;
            result |= (b & 0x1f) << shift;
            shift += 5;
        } while (b >= 0x20);

        return (result & 1) != 0 ? ~(result >> 1) : result >> 1;
    }
}
