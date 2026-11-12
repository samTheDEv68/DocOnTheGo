Option Strict On
Imports System.Linq
Imports System.Security.Claims
Imports System.Threading.Tasks
Imports DocOnTheGo.Api.Contracts
Imports DocOnTheGo.Api.Infrastructure
Imports DocOnTheGo.Core.Models
Imports DocOnTheGo.Core.Services
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Mvc

Namespace Controllers

    ''' <summary>Appointments, secure messages, prescriptions and the signed-in patient's profile.</summary>
    <ApiController>
    <Authorize>
    <Route("api")>
    Public Class CareController
        Inherits ControllerBase

        Private ReadOnly _store As IDataStore
        Private ReadOnly _sms As ISmsSender

        Public Sub New(store As IDataStore, sms As ISmsSender)
            _store = store
            _sms = sms
        End Sub

        Private ReadOnly Property UserId As String
            Get
                Return User.FindFirstValue(ClaimTypes.NameIdentifier)
            End Get
        End Property

        Private ReadOnly Property IsDoctor As Boolean
            Get
                Return User.IsInRole("Doctor")
            End Get
        End Property

        ' ------------------------------------------------------------ Profile

        <Authorize(Roles:="Patient")>
        <HttpGet("patients/me")>
        Public Function MyProfile() As IActionResult
            Dim p = _store.FindPatient(UserId)
            If p Is Nothing Then Return NotFound()
            Return Ok(p)
        End Function

        ' ------------------------------------------------------------ Appointments

        <HttpGet("appointments")>
        Public Function MyAppointments() As IActionResult
            Dim mine = _store.Appointments().Where(Function(a) If(IsDoctor, a.DoctorId, a.PatientId) = UserId)
            Return Ok(mine.OrderByDescending(Function(a) a.ScheduledUtc))
        End Function

        <Authorize(Roles:="Patient")>
        <HttpPost("appointments")>
        Public Async Function Book(req As BookAppointmentRequest) As Task(Of IActionResult)
            Dim p = _store.FindPatient(UserId)
            Dim d = _store.FindDoctor(req.DoctorId)
            If p Is Nothing Then Return Unauthorized()
            If d Is Nothing OrElse Not d.IsVerified Then Return BadRequest(New With {.errors = {"That doctor is not available."}})
            If req.ScheduledUtc < DateTime.UtcNow.AddMinutes(-5) Then Return BadRequest(New With {.errors = {"Choose a time in the future."}})
            If req.Mode = ConsultationMode.Online AndAlso Not d.OffersOnline Then Return BadRequest(New With {.errors = {"This doctor only sees patients in person."}})

            Dim a As New Appointment With {
                .Id = IdGenerator.NewId(), .PatientId = p.Id, .DoctorId = d.Id,
                .PatientName = p.FullName, .DoctorName = d.FullName,
                .ScheduledUtc = req.ScheduledUtc, .Mode = req.Mode, .Reason = If(req.Reason, "")}
            _store.SaveAppointment(a)
            _store.AddAudit(New AuditEntry With {.Actor = p.Id, .Action = "appointment.book", .Subject = a.Id})
            Await _sms.SendAsync(p.Phone, $"Doc On The Go: your {a.Mode} appointment with {d.FullName} is requested for {a.ScheduledUtc:yyyy-MM-dd HH:mm} UTC.")
            Return Ok(a)
        End Function

        <HttpPatch("appointments/{id}")>
        Public Function UpdateStatus(id As String, req As UpdateAppointmentRequest) As IActionResult
            Dim a = _store.Appointments().FirstOrDefault(Function(x) x.Id = id)
            If a Is Nothing OrElse (a.PatientId <> UserId AndAlso a.DoctorId <> UserId) Then Return NotFound()
            If Not IsDoctor AndAlso req.Status <> AppointmentStatus.Cancelled Then Return Forbid()
            a.Status = req.Status
            _store.SaveAppointment(a)
            _store.AddAudit(New AuditEntry With {.Actor = UserId, .Action = "appointment." & req.Status.ToString().ToLowerInvariant(), .Subject = a.Id})
            Return Ok(a)
        End Function

        ' ------------------------------------------------------------ Messages

        <HttpGet("conversations")>
        Public Function MyConversations() As IActionResult
            Dim mine = _store.Conversations().Where(Function(c) If(IsDoctor, c.DoctorId, c.PatientId) = UserId)
            Return Ok(mine.OrderByDescending(Function(c) c.LastUtc))
        End Function

        <HttpPost("conversations")>
        Public Function StartConversation(req As StartConversationRequest) As IActionResult
            Dim patientId = If(IsDoctor, req.PatientId, UserId)
            Dim doctorId = If(IsDoctor, UserId, req.DoctorId)
            Dim p = _store.FindPatient(patientId)
            Dim d = _store.FindDoctor(doctorId)
            If p Is Nothing OrElse d Is Nothing Then Return BadRequest(New With {.errors = {"Unknown patient or doctor."}})
            Dim c = _store.Conversations().FirstOrDefault(Function(x) x.PatientId = p.Id AndAlso x.DoctorId = d.Id)
            If c Is Nothing Then
                c = New Conversation With {.Id = IdGenerator.NewId(), .PatientId = p.Id, .DoctorId = d.Id, .PatientName = p.FullName, .DoctorName = d.FullName}
                _store.SaveConversation(c)
            End If
            Return Ok(c)
        End Function

        <HttpGet("conversations/{id}/messages")>
        Public Function GetMessages(id As String) As IActionResult
            Dim c = OwnConversation(id)
            If c Is Nothing Then Return NotFound()
            Return Ok(_store.Messages(id).OrderBy(Function(m) m.SentUtc))
        End Function

        <HttpPost("conversations/{id}/messages")>
        Public Function Send(id As String, req As SendMessageRequest) As IActionResult
            Dim c = OwnConversation(id)
            If c Is Nothing Then Return NotFound()
            If String.IsNullOrWhiteSpace(req.Text) Then Return BadRequest()
            Dim m As New ChatMessage With {
                .Id = IdGenerator.NewId(), .ConversationId = id, .SenderId = UserId,
                .SenderName = User.FindFirstValue(ClaimTypes.Name), .Text = req.Text.Trim()}
            _store.AddMessage(m)
            c.LastMessage = m.Text
            c.LastUtc = m.SentUtc
            _store.SaveConversation(c)
            Return Ok(m)
        End Function

        Private Function OwnConversation(id As String) As Conversation
            Return _store.Conversations().FirstOrDefault(Function(x) x.Id = id AndAlso (x.PatientId = UserId OrElse x.DoctorId = UserId))
        End Function

        ' ------------------------------------------------------------ Prescriptions

        <HttpGet("prescriptions")>
        Public Function MyPrescriptions() As IActionResult
            Dim mine = _store.Prescriptions().Where(Function(p) If(IsDoctor, p.DoctorId, p.PatientId) = UserId)
            Return Ok(mine.OrderByDescending(Function(p) p.IssuedUtc))
        End Function

        ''' <summary>Digital prescription with an audit trail and an SMS token for the patient.</summary>
        <Authorize(Roles:="Doctor")>
        <HttpPost("prescriptions")>
        Public Async Function Issue(req As IssuePrescriptionRequest) As Task(Of IActionResult)
            Dim a = _store.Appointments().FirstOrDefault(Function(x) x.Id = req.AppointmentId AndAlso x.DoctorId = UserId)
            If a Is Nothing Then Return BadRequest(New With {.errors = {"Unknown appointment."}})
            If String.IsNullOrWhiteSpace(req.Medication) Then Return BadRequest(New With {.errors = {"Medication is required."}})
            Dim rx As New Prescription With {
                .Id = IdGenerator.NewId(), .AppointmentId = a.Id, .PatientId = a.PatientId, .DoctorId = UserId,
                .Medication = req.Medication, .Dosage = req.Dosage, .Instructions = req.Instructions, .FollowUp = req.FollowUp,
                .Token = IdGenerator.NewPrescriptionToken()}
            _store.SavePrescription(rx)
            _store.AddAudit(New AuditEntry With {.Actor = UserId, .Action = "prescription.issue", .Subject = rx.Token, .Details = rx.Medication})
            Dim p = _store.FindPatient(a.PatientId)
            If p IsNot Nothing Then Await _sms.SendAsync(p.Phone, $"Doc On The Go prescription {rx.Token}: {rx.Medication} {rx.Dosage}.")
            Return Ok(rx)
        End Function
    End Class

End Namespace
