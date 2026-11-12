using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DocOnTheGo.Core.Models;

namespace DocOnTheGo.Mobile.Services;

/// <summary>Talks to the VB.NET Doc On The Go API.</summary>
public sealed class ApiBackend : IBackend
{
    private readonly HttpClient _http;

    public ApiBackend(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(30) };
    }

    public bool IsDemo => false;

    private HttpRequestMessage Req(HttpMethod method, string path, object? body = null)
    {
        var r = new HttpRequestMessage(method, path);
        if (!string.IsNullOrEmpty(Session.Token))
            r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Session.Token);
        if (body != null) r.Content = JsonContent.Create(body, body.GetType(), options: Json.Options);
        return r;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage req)
    {
        HttpResponseMessage res;
        try { res = await _http.SendAsync(req); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BackendException("Can't reach the Doc On The Go server. Check your connection and try again.");
        }

        var text = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
        {
            if (res.StatusCode == HttpStatusCode.Unauthorized && string.IsNullOrWhiteSpace(text))
                throw new BackendException("Your session has expired. Please log in again.");
            throw new BackendException(ExtractError(text) ?? $"The server returned an error ({(int)res.StatusCode}).");
        }
        return JsonSerializer.Deserialize<T>(text, Json.Options)!;
    }

    private static string? ExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out var errs))
            {
                if (errs.ValueKind == JsonValueKind.Array)
                    return string.Join("\n", errs.EnumerateArray().Select(e => e.GetString()));
                if (errs.ValueKind == JsonValueKind.Object)
                    return string.Join("\n", errs.EnumerateObject().SelectMany(p => p.Value.EnumerateArray().Select(e => e.GetString())));
            }
        }
        catch (JsonException) { }
        return null;
    }

    public Task<AuthResult> RegisterPatientAsync(RegisterPatient req) =>
        SendAsync<AuthResult>(Req(HttpMethod.Post, "api/auth/patients/register", req));

    public Task<AuthResult> RegisterDoctorAsync(RegisterDoctor req) =>
        SendAsync<AuthResult>(Req(HttpMethod.Post, "api/auth/doctors/register", req));

    public Task<AuthResult> RequestOtpAsync(string contact) =>
        SendAsync<AuthResult>(Req(HttpMethod.Post, "api/auth/otp/request", new { contact }));

    public Task<AuthResult> VerifyOtpAsync(string contact, string code) =>
        SendAsync<AuthResult>(Req(HttpMethod.Post, "api/auth/otp/verify", new { contact, code }));

    public async Task<Patient?> GetMyPatientProfileAsync() =>
        await SendAsync<Patient>(Req(HttpMethod.Get, "api/patients/me"));

    public async Task<Doctor?> GetMyDoctorProfileAsync() =>
        await SendAsync<Doctor>(Req(HttpMethod.Get, "api/doctors/me"));

    public Task<List<DoctorMatch>> SearchDoctorsAsync(string? query, GeoPoint? near)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(query)) qs.Add("q=" + Uri.EscapeDataString(query));
        if (near != null) { qs.Add($"lat={near.Latitude:R}"); qs.Add($"lon={near.Longitude:R}"); }
        return SendAsync<List<DoctorMatch>>(Req(HttpMethod.Get, "api/doctors" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "")));
    }

    public Task<TriageResponse> TriageAsync(string text, GeoPoint? near) =>
        SendAsync<TriageResponse>(Req(HttpMethod.Post, "api/triage", new { text, latitude = near?.Latitude, longitude = near?.Longitude }));

    public async Task<CredentialDocument> UploadCredentialAsync(CredentialKind kind, string fileName, Stream content)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(kind.ToString()), "kind");
        var file = new StreamContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(Core.Services.Validation.ContentTypeFor(fileName));
        form.Add(file, "file", fileName);
        var req = Req(HttpMethod.Post, "api/doctors/me/credentials");
        req.Content = form;
        return await SendAsync<CredentialDocument>(req);
    }

    public Task<Doctor> SubmitForVerificationAsync() =>
        SendAsync<Doctor>(Req(HttpMethod.Post, "api/doctors/me/submit"));

    public Task<List<Appointment>> GetAppointmentsAsync() =>
        SendAsync<List<Appointment>>(Req(HttpMethod.Get, "api/appointments"));

    public Task<Appointment> BookAppointmentAsync(string doctorId, DateTime scheduledUtc, ConsultationMode mode, string reason) =>
        SendAsync<Appointment>(Req(HttpMethod.Post, "api/appointments", new { doctorId, scheduledUtc, mode, reason }));

    public Task<Appointment> SetAppointmentStatusAsync(string appointmentId, AppointmentStatus status) =>
        SendAsync<Appointment>(Req(HttpMethod.Patch, $"api/appointments/{appointmentId}", new { status }));

    public Task<List<Conversation>> GetConversationsAsync() =>
        SendAsync<List<Conversation>>(Req(HttpMethod.Get, "api/conversations"));

    public Task<Conversation> StartConversationAsync(string doctorId, string? patientId = null) =>
        SendAsync<Conversation>(Req(HttpMethod.Post, "api/conversations", new { doctorId, patientId }));

    public Task<List<ChatMessage>> GetMessagesAsync(string conversationId) =>
        SendAsync<List<ChatMessage>>(Req(HttpMethod.Get, $"api/conversations/{conversationId}/messages"));

    public Task<ChatMessage> SendMessageAsync(string conversationId, string text) =>
        SendAsync<ChatMessage>(Req(HttpMethod.Post, $"api/conversations/{conversationId}/messages", new { text }));

    public Task<List<Prescription>> GetPrescriptionsAsync() =>
        SendAsync<List<Prescription>>(Req(HttpMethod.Get, "api/prescriptions"));
}
