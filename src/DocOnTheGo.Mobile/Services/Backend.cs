using System.Text.Json;
using System.Text.Json.Serialization;
using DocOnTheGo.Core.Models;

namespace DocOnTheGo.Mobile.Services;

public sealed class AuthResult
{
    public string Token { get; set; } = "";
    public string Role { get; set; } = "";
    public string UserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PatientTag { get; set; } = "";
    public string? DevOtp { get; set; }
}

public sealed class TriageResponse
{
    public TriageResult Triage { get; set; } = new();
    public List<DoctorMatch> Doctors { get; set; } = new();
}

public sealed class RegisterPatient
{
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public DateTime DateOfBirth { get; set; }
    public Gender Gender { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public sealed class RegisterDoctor
{
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string LicenseNumber { get; set; } = "";
    public Specialization PrimarySpecialization { get; set; }
    public List<string> SubSpecialties { get; set; } = new();
    public bool IsMobileUnit { get; set; }
    public double ServiceRadiusKm { get; set; } = 50;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

/// <summary>Raised for problems the user can act on; the message is shown as-is.</summary>
public sealed class BackendException(string message) : Exception(message);

/// <summary>
/// Everything the app needs from a server. <see cref="ApiBackend"/> talks to the VB.NET API;
/// <see cref="DemoBackend"/> runs the same VB.NET core logic on the phone so the app works offline.
/// </summary>
public interface IBackend
{
    bool IsDemo { get; }
    Task<AuthResult> RegisterPatientAsync(RegisterPatient req);
    Task<AuthResult> RegisterDoctorAsync(RegisterDoctor req);
    Task<AuthResult> RequestOtpAsync(string contact);
    Task<AuthResult> VerifyOtpAsync(string contact, string code);

    Task<Patient?> GetMyPatientProfileAsync();
    Task<Doctor?> GetMyDoctorProfileAsync();

    Task<List<DoctorMatch>> SearchDoctorsAsync(string? query, GeoPoint? near);
    Task<TriageResponse> TriageAsync(string text, GeoPoint? near);

    Task<CredentialDocument> UploadCredentialAsync(CredentialKind kind, string fileName, Stream content);
    Task<Doctor> SubmitForVerificationAsync();

    Task<List<Appointment>> GetAppointmentsAsync();
    Task<Appointment> BookAppointmentAsync(string doctorId, DateTime scheduledUtc, ConsultationMode mode, string reason);
    Task<Appointment> SetAppointmentStatusAsync(string appointmentId, AppointmentStatus status);

    Task<List<Conversation>> GetConversationsAsync();
    Task<Conversation> StartConversationAsync(string doctorId, string? patientId = null);
    Task<List<ChatMessage>> GetMessagesAsync(string conversationId);
    Task<ChatMessage> SendMessageAsync(string conversationId, string text);

    Task<List<Prescription>> GetPrescriptionsAsync();
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }
}
