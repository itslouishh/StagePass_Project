using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace StagePass.Client;

public class ApiException : Exception
{
    public int StatusCode { get; }
    public ApiException(string message, int status) : base(message) { StatusCode = status; }
}

/// <summary>All communication with the StagePass REST API (GET / POST / PUT / DELETE over HTTP + JSON).</summary>
public static class ApiClient
{
    public static string BaseUrl = "http://localhost:5000";
    public static string Token;
    public static bool IsAdmin => !string.IsNullOrEmpty(Token);

    static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    static async Task<T> Send<T>(HttpMethod method, string path, object body = null, bool admin = false)
    {
        using var req = new HttpRequestMessage(method, BaseUrl + path);
        if (body != null)
            req.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        if (admin && Token != null)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        using var resp = await Http.SendAsync(req);
        string text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            string msg = resp.ReasonPhrase;
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("error", out var e)) msg = e.GetString();
            }
            catch { /* body was not JSON */ }
            throw new ApiException(msg, (int)resp.StatusCode);
        }
        return JsonSerializer.Deserialize<T>(text, Json);
    }

    // ---- auth
    public static async Task Login(string user, string pass)
    {
        var r = await Send<LoginResponse>(HttpMethod.Post, "/api/auth/login", new LoginRequest { Username = user, Password = pass });
        Token = r.Token;
    }
    public static void Logout() => Token = null;

    // ---- venues / shows (CRUD)
    public static Task<List<Venue>> GetVenues() => Send<List<Venue>>(HttpMethod.Get, "/api/venues");
    public static Task<List<ShowInfo>> GetShows() => Send<List<ShowInfo>>(HttpMethod.Get, "/api/shows");
    public static Task<ShowInfo> CreateShow(ShowInput s) => Send<ShowInfo>(HttpMethod.Post, "/api/shows", s, true);
    public static Task<ShowInfo> UpdateShow(int id, ShowInput s) => Send<ShowInfo>(HttpMethod.Put, $"/api/shows/{id}", s, true);
    public static Task DeleteShow(int id) => Send<object>(HttpMethod.Delete, $"/api/shows/{id}", null, true);

    // ---- seats & bookings
    public static Task<SeatMap> GetSeats(int showId) => Send<SeatMap>(HttpMethod.Get, $"/api/shows/{showId}/seats");
    public static Task<Booking> CreateBooking(BookingRequest r) => Send<Booking>(HttpMethod.Post, "/api/bookings", r);
    public static Task<List<Booking>> FindBookings(string emailOrRef)
    {
        string key = emailOrRef.Contains('@') ? "email" : "reference";
        return Send<List<Booking>>(HttpMethod.Get, $"/api/bookings?{key}={Uri.EscapeDataString(emailOrRef.Trim())}");
    }
    public static Task<Booking> ChangeSeats(int bookingId, SeatChangeRequest r) => Send<Booking>(HttpMethod.Put, $"/api/bookings/{bookingId}", r);
    public static Task<Booking> CancelBooking(int bookingId) => Send<Booking>(HttpMethod.Delete, $"/api/bookings/{bookingId}");

    // ---- reports (admin)
    public static Task<ReportSummary> GetReport() => Send<ReportSummary>(HttpMethod.Get, "/api/reports/summary", null, true);
}
