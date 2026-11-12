using System.Text.Json;
using DocOnTheGo.Core.Models;
using DocOnTheGo.Core.Services;

namespace DocOnTheGo.Mobile.Services;

/// <summary>
/// Offline demo mode: runs the shared VB.NET core (tagging, triage, matching, validation) on the
/// device and keeps data in a local JSON file. Used until a server URL is set under Profile.
/// </summary>
public sealed class DemoBackend : IBackend
{
    private sealed class State
    {
        public List<Patient> Patients { get; set; } = new();
        public List<Doctor> Doctors { get; set; } = new();
        public List<Appointment> Appointments { get; set; } = new();
        public List<Conversation> Conversations { get; set; } = new();
        public List<ChatMessage> Messages { get; set; } = new();
        public List<Prescription> Prescriptions { get; set; } = new();
        public Dictionary<string, string> Otps { get; set; } = new();
    }

    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static State? _state;
    private readonly SymptomTriageEngine _engine = new();

    private static string FilePath => Path.Combine(FileSystem.AppDataDirectory, "demo-data.json");

    public bool IsDemo => true;

    private static async Task<State> LoadAsync()
    {
        if (_state != null) return _state;
        try
        {
            if (File.Exists(FilePath))
                _state = JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(FilePath), Json.Options);
        }
        catch (JsonException) { _state = null; }
        if (_state == null)
        {
            _state = new State { Doctors = SeedData.SampleDoctors() };
            await SaveAsync();
        }
        return _state;
    }

    private static Task SaveAsync() =>
        File.WriteAllTextAsync(FilePath, JsonSerializer.Serialize(_state, Json.Options));

    private async Task<T> WithStateAsync<T>(Func<State, T> action, bool save = false)
    {
        await Lock.WaitAsync();
        try
        {
            var s = await LoadAsync();
            var result = action(s);
            if (save) await SaveAsync();
            return result;
        }
        finally { Lock.Release(); }
    }

    private static bool Same(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
        (string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase) ||
         (Validation.NormalizePhone(a).Length >= 7 && Validation.NormalizePhone(a) == Validation.NormalizePhone(b)));

    private static AuthResult AuthFor(Patient p) =>
        new() { Token = "demo", Role = "Patient", UserId = p.Id, DisplayName = p.FullName, PatientTag = p.PatientTag };

    private static AuthResult AuthFor(Doctor d) =>
        new() { Token = "demo", Role = "Doctor", UserId = d.Id, DisplayName = d.FullName };

    public Task<AuthResult> RegisterPatientAsync(RegisterPatient req) => WithStateAsync(s =>
    {
        var v = Validation.ValidatePatient(req.FullName, req.Email, req.Phone, req.DateOfBirth, DateTime.Today);
        if (!v.IsValid) throw new BackendException(string.Join("\n", v.Errors));
        if (s.Patients.Any(p => Same(p.Email, req.Email) || Same(p.Phone, req.Phone)))
            throw new BackendException("An account with this email or phone already exists. Please log in.");
        var p = new Patient
        {
            Id = IdGenerator.NewId(),
            PatientTag = IdGenerator.NewPatientTag(s.Patients.Select(x => x.PatientTag).ToHashSet()),
            FullName = req.FullName.Trim(),
            Email = req.Email.Trim(),
            Phone = Validation.NormalizePhone(req.Phone),
            DateOfBirth = req.DateOfBirth,
            Gender = req.Gender,
            Location = req.Latitude is double la && req.Longitude is double lo ? new GeoPoint(la, lo) : null
        };
        s.Patients.Add(p);
        return AuthFor(p);
    }, save: true);

    public Task<AuthResult> RegisterDoctorAsync(RegisterDoctor req) => WithStateAsync(s =>
    {
        var v = Validation.ValidateDoctor(req.FullName, req.LicenseNumber, req.Email, req.Phone);
        if (!v.IsValid) throw new BackendException(string.Join("\n", v.Errors));
        if (s.Doctors.Any(d => Same(d.Email, req.Email)))
            throw new BackendException("A doctor with this email already exists. Please log in.");
        if (s.Doctors.Any(d => string.Equals(d.LicenseNumber, req.LicenseNumber.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new BackendException("This license number is already registered.");
        var name = req.FullName.Trim();
        if (!name.StartsWith("Dr", StringComparison.OrdinalIgnoreCase)) name = "Dr. " + name;
        var d = new Doctor
        {
            Id = IdGenerator.NewId(),
            FullName = name,
            Email = req.Email.Trim(),
            Phone = Validation.NormalizePhone(req.Phone),
            LicenseNumber = req.LicenseNumber.Trim().ToUpperInvariant(),
            PrimarySpecialization = req.PrimarySpecialization,
            IsMobileUnit = req.IsMobileUnit,
            ServiceRadiusKm = req.ServiceRadiusKm > 0 ? req.ServiceRadiusKm : 50,
            Location = req.Latitude is double la && req.Longitude is double lo ? new GeoPoint(la, lo) : null,
            AvatarInitials = IdGenerator.InitialsOf(name)
        };
        s.Doctors.Add(d);
        return AuthFor(d);
    }, save: true);

    public Task<AuthResult> RequestOtpAsync(string contact) => WithStateAsync(s =>
    {
        if (!s.Patients.Any(p => Same(p.Email, contact) || Same(p.Phone, contact)) &&
            !s.Doctors.Any(d => Same(d.Email, contact) || Same(d.Phone, contact)))
            throw new BackendException("No account found for that email or phone on this device.");
        var code = IdGenerator.NewOtp();
        s.Otps[contact.Trim().ToLowerInvariant()] = code;
        // Demo mode has no SMS provider, so the code is shown on screen.
        return new AuthResult { DevOtp = code };
    }, save: true);

    public Task<AuthResult> VerifyOtpAsync(string contact, string code) => WithStateAsync(s =>
    {
        var key = contact.Trim().ToLowerInvariant();
        if (!s.Otps.TryGetValue(key, out var expected) || expected != code.Trim())
            throw new BackendException("That code is wrong or has expired.");
        s.Otps.Remove(key);
        var p = s.Patients.FirstOrDefault(x => Same(x.Email, contact) || Same(x.Phone, contact));
        if (p != null) return AuthFor(p);
        var d = s.Doctors.First(x => Same(x.Email, contact) || Same(x.Phone, contact));
        return AuthFor(d);
    }, save: true);

    public Task<Patient?> GetMyPatientProfileAsync() =>
        WithStateAsync(s => s.Patients.FirstOrDefault(p => p.Id == Session.UserId));

    public Task<Doctor?> GetMyDoctorProfileAsync() =>
        WithStateAsync(s => s.Doctors.FirstOrDefault(d => d.Id == Session.UserId));

    public Task<List<DoctorMatch>> SearchDoctorsAsync(string? query, GeoPoint? near) => WithStateAsync(s =>
    {
        IEnumerable<Doctor> docs = s.Doctors.Where(d => d.IsVerified);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var t = _engine.Analyze(query);
            var wanted = t.Recommended.Select(r => r.Specialization).ToHashSet();
            docs = docs.Where(d => d.FullName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                   SpecializationInfo.DisplayName(d.PrimarySpecialization).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                   d.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                                   (t.ExtractedSymptoms.Count > 0 && wanted.Contains(d.PrimarySpecialization)));
        }
        return DoctorMatcher.Match(docs, null, near, 50);
    });

    public Task<TriageResponse> TriageAsync(string text, GeoPoint? near) => WithStateAsync(s =>
    {
        var result = _engine.Analyze(text);
        return new TriageResponse { Triage = result, Doctors = DoctorMatcher.Match(s.Doctors, result, near, 10) };
    });

    public async Task<CredentialDocument> UploadCredentialAsync(CredentialKind kind, string fileName, Stream content)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms);
        var bytes = ms.ToArray();
        var v = Validation.ValidateCredentialFile(fileName, bytes.LongLength);
        if (!v.IsValid) throw new BackendException(string.Join("\n", v.Errors));
        var ok = Validation.HasAllowedSignature(bytes.Take(8).ToArray());
        return await WithStateAsync(s =>
        {
            var d = s.Doctors.First(x => x.Id == Session.UserId);
            d.Credentials.RemoveAll(c => c.Kind == kind);
            var doc = new CredentialDocument
            {
                Id = IdGenerator.NewId(), Kind = kind, FileName = Path.GetFileName(fileName),
                ContentType = Validation.ContentTypeFor(fileName), SizeBytes = bytes.LongLength,
                StorageKey = "on-device", AutoCheckPassed = ok,
                AutoCheckNotes = ok ? "File type confirmed." : "File content does not match a JPEG, PNG or PDF."
            };
            d.Credentials.Add(doc);
            return doc;
        }, save: true);
    }

    public Task<Doctor> SubmitForVerificationAsync() => WithStateAsync(s =>
    {
        var d = s.Doctors.First(x => x.Id == Session.UserId);
        if (!d.Credentials.Any(c => c.Kind == CredentialKind.MedicalDegree) || !d.Credentials.Any(c => c.Kind == CredentialKind.BoardCertification))
            throw new BackendException("Upload both your medical degree and board certification first.");
        d.Status = VerificationStatus.InProgress;
        d.StatusNotes = "Demo mode: no reviewer is connected. Use \"Approve (demo)\" to simulate a reviewer.";
        DoctorTagging.ApplyTags(d);
        return d;
    }, save: true);

    /// <summary>Demo only: simulates the admin reviewer approving the signed-in doctor.</summary>
    public Task<Doctor> DemoApproveMeAsync() => WithStateAsync(s =>
    {
        var d = s.Doctors.First(x => x.Id == Session.UserId);
        d.Status = VerificationStatus.Verified;
        d.StatusNotes = "Approved (demo simulation).";
        d.Rating = d.Rating == 0 ? 5 : d.Rating;
        DoctorTagging.ApplyTags(d);
        return d;
    }, save: true);

    public Task<List<Appointment>> GetAppointmentsAsync() => WithStateAsync(s =>
        s.Appointments.Where(a => (Session.IsDoctor ? a.DoctorId : a.PatientId) == Session.UserId)
                      .OrderByDescending(a => a.ScheduledUtc).ToList());

    public Task<Appointment> BookAppointmentAsync(string doctorId, DateTime scheduledUtc, ConsultationMode mode, string reason) => WithStateAsync(s =>
    {
        var p = s.Patients.FirstOrDefault(x => x.Id == Session.UserId) ?? throw new BackendException("Please log in as a patient to book.");
        var d = s.Doctors.FirstOrDefault(x => x.Id == doctorId && x.IsVerified) ?? throw new BackendException("That doctor is not available.");
        if (scheduledUtc < DateTime.UtcNow.AddMinutes(-5)) throw new BackendException("Choose a time in the future.");
        var a = new Appointment
        {
            Id = IdGenerator.NewId(), PatientId = p.Id, DoctorId = d.Id, PatientName = p.FullName, DoctorName = d.FullName,
            ScheduledUtc = scheduledUtc, Mode = mode, Reason = reason ?? "", Status = AppointmentStatus.Confirmed
        };
        s.Appointments.Add(a);
        return a;
    }, save: true);

    public Task<Appointment> SetAppointmentStatusAsync(string appointmentId, AppointmentStatus status) => WithStateAsync(s =>
    {
        var a = s.Appointments.First(x => x.Id == appointmentId);
        a.Status = status;
        return a;
    }, save: true);

    public Task<List<Conversation>> GetConversationsAsync() => WithStateAsync(s =>
        s.Conversations.Where(c => (Session.IsDoctor ? c.DoctorId : c.PatientId) == Session.UserId)
                       .OrderByDescending(c => c.LastUtc).ToList());

    public Task<Conversation> StartConversationAsync(string doctorId, string? patientId = null) => WithStateAsync(s =>
    {
        var pid = Session.IsDoctor ? patientId! : Session.UserId;
        var did = Session.IsDoctor ? Session.UserId : doctorId;
        var c = s.Conversations.FirstOrDefault(x => x.PatientId == pid && x.DoctorId == did);
        if (c != null) return c;
        var p = s.Patients.First(x => x.Id == pid);
        var d = s.Doctors.First(x => x.Id == did);
        c = new Conversation { Id = IdGenerator.NewId(), PatientId = pid, DoctorId = did, PatientName = p.FullName, DoctorName = d.FullName };
        s.Conversations.Add(c);
        return c;
    }, save: true);

    public Task<List<ChatMessage>> GetMessagesAsync(string conversationId) => WithStateAsync(s =>
        s.Messages.Where(m => m.ConversationId == conversationId).OrderBy(m => m.SentUtc).ToList());

    public Task<ChatMessage> SendMessageAsync(string conversationId, string text) => WithStateAsync(s =>
    {
        var m = new ChatMessage { Id = IdGenerator.NewId(), ConversationId = conversationId, SenderId = Session.UserId, SenderName = Session.DisplayName, Text = text.Trim() };
        s.Messages.Add(m);
        var c = s.Conversations.First(x => x.Id == conversationId);
        c.LastMessage = m.Text;
        c.LastUtc = m.SentUtc;
        return m;
    }, save: true);

    public Task<List<Prescription>> GetPrescriptionsAsync() => WithStateAsync(s =>
        s.Prescriptions.Where(p => (Session.IsDoctor ? p.DoctorId : p.PatientId) == Session.UserId).ToList());
}
