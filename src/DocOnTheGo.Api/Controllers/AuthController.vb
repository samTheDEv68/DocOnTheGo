Option Strict On
Imports System.Linq
Imports System.Threading.Tasks
Imports DocOnTheGo.Api.Contracts
Imports DocOnTheGo.Api.Infrastructure
Imports DocOnTheGo.Core.Models
Imports DocOnTheGo.Core.Services
Imports Microsoft.AspNetCore.Mvc
Imports Microsoft.Extensions.Configuration

Namespace Controllers

    <ApiController>
    <Route("api/auth")>
    Public Class AuthController
        Inherits ControllerBase

        Private ReadOnly _store As IDataStore
        Private ReadOnly _tokens As TokenService
        Private ReadOnly _otp As OtpService
        Private ReadOnly _sms As ISmsSender
        Private ReadOnly _cfg As IConfiguration

        Public Sub New(store As IDataStore, tokens As TokenService, otp As OtpService, sms As ISmsSender, cfg As IConfiguration)
            _store = store
            _tokens = tokens
            _otp = otp
            _sms = sms
            _cfg = cfg
        End Sub

        ''' <summary>Patient account creation (Functional Guide section 1).</summary>
        <HttpPost("patients/register")>
        Public Function RegisterPatient(req As RegisterPatientRequest) As ActionResult(Of AuthResponse)
            Dim v = Validation.ValidatePatient(req.FullName, req.Email, req.Phone, req.DateOfBirth, Date.Today)
            If Not v.IsValid Then Return BadRequest(New With {.errors = v.Errors})
            If _store.FindPatientByContact(req.Email) IsNot Nothing OrElse _store.FindPatientByContact(req.Phone) IsNot Nothing Then
                Return Conflict(New With {.errors = {"An account with this email or phone already exists. Please log in."}})
            End If

            Dim tags = _store.Patients().Select(Function(x) x.PatientTag).ToHashSet()
            Dim p As New Patient With {
                .Id = IdGenerator.NewId(),
                .PatientTag = IdGenerator.NewPatientTag(tags),
                .FullName = req.FullName.Trim(),
                .Email = req.Email.Trim(),
                .Phone = Validation.NormalizePhone(req.Phone),
                .DateOfBirth = req.DateOfBirth,
                .Gender = req.Gender,
                .Location = If(req.Latitude.HasValue AndAlso req.Longitude.HasValue, New GeoPoint(req.Latitude.Value, req.Longitude.Value), Nothing)
            }
            _store.SavePatient(p)
            _store.AddAudit(New AuditEntry With {.Actor = p.Id, .Action = "patient.register", .Subject = p.PatientTag})
            Return New AuthResponse With {
                .Token = _tokens.Issue(p.Id, "Patient", p.FullName),
                .Role = "Patient", .UserId = p.Id, .DisplayName = p.FullName, .PatientTag = p.PatientTag}
        End Function

        ''' <summary>Doctor profile setup (Functional Guide section 2). Credentials are uploaded next.</summary>
        <HttpPost("doctors/register")>
        Public Function RegisterDoctor(req As RegisterDoctorRequest) As ActionResult(Of AuthResponse)
            Dim v = Validation.ValidateDoctor(req.FullName, req.LicenseNumber, req.Email, req.Phone)
            If Not v.IsValid Then Return BadRequest(New With {.errors = v.Errors})
            If _store.FindDoctorByContact(req.Email) IsNot Nothing Then
                Return Conflict(New With {.errors = {"A doctor with this email already exists. Please log in."}})
            End If
            If _store.Doctors().Any(Function(x) String.Equals(x.LicenseNumber, req.LicenseNumber.Trim(), StringComparison.OrdinalIgnoreCase)) Then
                Return Conflict(New With {.errors = {"This license number is already registered."}})
            End If

            Dim name = req.FullName.Trim()
            If Not name.StartsWith("Dr", StringComparison.OrdinalIgnoreCase) Then name = "Dr. " & name
            Dim d As New Doctor With {
                .Id = IdGenerator.NewId(),
                .FullName = name,
                .Email = req.Email.Trim(),
                .Phone = Validation.NormalizePhone(req.Phone),
                .LicenseNumber = req.LicenseNumber.Trim().ToUpperInvariant(),
                .PrimarySpecialization = req.PrimarySpecialization,
                .SubSpecialties = If(req.SubSpecialties, New List(Of String)()),
                .IsMobileUnit = req.IsMobileUnit,
                .ServiceRadiusKm = If(req.ServiceRadiusKm > 0, req.ServiceRadiusKm, 50),
                .Location = If(req.Latitude.HasValue AndAlso req.Longitude.HasValue, New GeoPoint(req.Latitude.Value, req.Longitude.Value), Nothing),
                .AvatarInitials = IdGenerator.InitialsOf(name)
            }
            _store.SaveDoctor(d)
            _store.AddAudit(New AuditEntry With {.Actor = d.Id, .Action = "doctor.register", .Subject = d.LicenseNumber})
            Return New AuthResponse With {.Token = _tokens.Issue(d.Id, "Doctor", d.FullName), .Role = "Doctor", .UserId = d.Id, .DisplayName = d.FullName}
        End Function

        ''' <summary>Passwordless log in, step 1: send a 6-digit code by SMS.</summary>
        <HttpPost("otp/request")>
        Public Async Function RequestOtp(req As OtpRequest) As Task(Of ActionResult(Of AuthResponse))
            Dim p = _store.FindPatientByContact(req.Contact)
            Dim d = If(p Is Nothing, _store.FindDoctorByContact(req.Contact), Nothing)
            If p Is Nothing AndAlso d Is Nothing Then Return NotFound(New With {.errors = {"No account found for that email or phone."}})
            Dim phone = If(p IsNot Nothing, p.Phone, d.Phone)
            Dim code = _otp.Create(req.Contact)
            If Not String.IsNullOrWhiteSpace(phone) Then
                Await _sms.SendAsync(phone, $"Your Doc On The Go code is {code}. It expires in 5 minutes.")
            End If
            Dim resp As New AuthResponse()
            If _cfg.GetValue(Of Boolean)("DocOnTheGo:ReturnOtpInResponse") Then resp.DevOtp = code
            Return resp
        End Function

        ''' <summary>Passwordless log in, step 2: exchange the code for a token.</summary>
        <HttpPost("otp/verify")>
        Public Function VerifyOtp(req As OtpVerifyRequest) As ActionResult(Of AuthResponse)
            If Not _otp.Verify(req.Contact, req.Code) Then Return Unauthorized(New With {.errors = {"That code is wrong or has expired."}})
            Dim p = _store.FindPatientByContact(req.Contact)
            If p IsNot Nothing Then
                Return New AuthResponse With {.Token = _tokens.Issue(p.Id, "Patient", p.FullName), .Role = "Patient", .UserId = p.Id, .DisplayName = p.FullName, .PatientTag = p.PatientTag}
            End If
            Dim d = _store.FindDoctorByContact(req.Contact)
            If d Is Nothing Then Return Unauthorized()
            Return New AuthResponse With {.Token = _tokens.Issue(d.Id, "Doctor", d.FullName), .Role = "Doctor", .UserId = d.Id, .DisplayName = d.FullName}
        End Function
    End Class

End Namespace
