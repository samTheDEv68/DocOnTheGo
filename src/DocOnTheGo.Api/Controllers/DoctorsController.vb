Option Strict On
Imports System.IO
Imports System.Linq
Imports System.Security.Claims
Imports System.Threading.Tasks
Imports DocOnTheGo.Api.Contracts
Imports DocOnTheGo.Api.Infrastructure
Imports DocOnTheGo.Core.Models
Imports DocOnTheGo.Core.Services
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Http
Imports Microsoft.AspNetCore.Mvc

Namespace Controllers

    <ApiController>
    <Route("api/doctors")>
    Public Class DoctorsController
        Inherits ControllerBase

        Private ReadOnly _store As IDataStore
        Private ReadOnly _files As IFileStore
        Private ReadOnly _verifier As ICredentialVerifier

        Public Sub New(store As IDataStore, files As IFileStore, verifier As ICredentialVerifier)
            _store = store
            _files = files
            _verifier = verifier
        End Sub

        ''' <summary>Verified doctors, optionally filtered by specialization or a search term, nearest first when a location is given.</summary>
        <HttpGet>
        Public Function List(<FromQuery> specialization As Specialization?, <FromQuery> q As String,
                             <FromQuery> lat As Double?, <FromQuery> lon As Double?) As IActionResult
            Dim docs = _store.Doctors().Where(Function(d) d.IsVerified)
            If specialization.HasValue Then docs = docs.Where(Function(d) d.PrimarySpecialization = specialization.Value)
            If Not String.IsNullOrWhiteSpace(q) Then
                Dim term = q.Trim()
                Dim triage = New SymptomTriageEngine().Analyze(term)
                Dim wanted = triage.Recommended.Select(Function(r) r.Specialization).ToHashSet()
                docs = docs.Where(Function(d) d.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) OrElse
                                              SpecializationInfo.DisplayName(d.PrimarySpecialization).Contains(term, StringComparison.OrdinalIgnoreCase) OrElse
                                              d.Tags.Any(Function(t) t.Contains(term, StringComparison.OrdinalIgnoreCase)) OrElse
                                              (triage.ExtractedSymptoms.Count > 0 AndAlso wanted.Contains(d.PrimarySpecialization)))
            End If
            Dim here = If(lat.HasValue AndAlso lon.HasValue, New GeoPoint(lat.Value, lon.Value), Nothing)
            Dim result = DoctorMatcher.Match(docs, Nothing, here, 50)
            Return Ok(PublicViews.ForPatients(result))
        End Function

        <HttpGet("{id}")>
        Public Function [Get](id As String) As IActionResult
            Dim d = _store.FindDoctor(id)
            If d Is Nothing OrElse Not d.IsVerified Then Return NotFound()
            Return Ok(PublicViews.ForPatients(d))
        End Function

        <Authorize(Roles:="Doctor")>
        <HttpGet("me")>
        Public Function CurrentDoctor() As IActionResult
            Dim d = _store.FindDoctor(User.FindFirstValue(ClaimTypes.NameIdentifier))
            If d Is Nothing Then Return NotFound()
            Return Ok(d)
        End Function

        ''' <summary>Credential upload (JPEG, PNG or PDF; max 10 MB). Stored encrypted.</summary>
        <Authorize(Roles:="Doctor")>
        <HttpPost("me/credentials")>
        <RequestSizeLimit(11 * 1024 * 1024)>
        Public Async Function UploadCredential(<FromForm> kind As CredentialKind, file As IFormFile) As Task(Of IActionResult)
            Dim d = _store.FindDoctor(User.FindFirstValue(ClaimTypes.NameIdentifier))
            If d Is Nothing Then Return NotFound()
            If file Is Nothing Then Return BadRequest(New With {.errors = {"No file was uploaded."}})
            Dim v = Validation.ValidateCredentialFile(file.FileName, file.Length)
            If Not v.IsValid Then Return BadRequest(New With {.errors = v.Errors})

            Dim data As Byte()
            Using ms As New MemoryStream()
                Await file.CopyToAsync(ms)
                data = ms.ToArray()
            End Using
            Dim sigOk = Validation.HasAllowedSignature(data.Take(8).ToArray())

            d.Credentials.RemoveAll(Function(c) c.Kind = kind)
            Dim doc As New CredentialDocument With {
                .Id = IdGenerator.NewId(),
                .Kind = kind,
                .FileName = Path.GetFileName(file.FileName),
                .ContentType = Validation.ContentTypeFor(file.FileName),
                .SizeBytes = data.LongLength,
                .StorageKey = _files.Save(data, file.FileName),
                .AutoCheckPassed = sigOk,
                .AutoCheckNotes = If(sigOk, "File type confirmed.", "File content does not match a JPEG, PNG or PDF.")
            }
            d.Credentials.Add(doc)
            _store.SaveDoctor(d)
            _store.AddAudit(New AuditEntry With {.Actor = d.Id, .Action = "credential.upload", .Subject = kind.ToString(), .Details = doc.FileName})
            Return Ok(doc)
        End Function

        ''' <summary>Submit for verification: status moves to "Verification in Progress".</summary>
        <Authorize(Roles:="Doctor")>
        <HttpPost("me/submit")>
        Public Async Function Submit() As Task(Of IActionResult)
            Dim d = _store.FindDoctor(User.FindFirstValue(ClaimTypes.NameIdentifier))
            If d Is Nothing Then Return NotFound()
            If Not d.Credentials.Any(Function(c) c.Kind = CredentialKind.MedicalDegree) OrElse
               Not d.Credentials.Any(Function(c) c.Kind = CredentialKind.BoardCertification) Then
                Return BadRequest(New With {.errors = {"Upload both your medical degree and board certification first."}})
            End If
            Dim outcome = Await _verifier.VerifyAsync(d)
            d.Status = outcome.Status
            d.StatusNotes = outcome.Notes
            DoctorTagging.ApplyTags(d)
            _store.SaveDoctor(d)
            _store.AddAudit(New AuditEntry With {.Actor = d.Id, .Action = "verification.submit", .Subject = d.LicenseNumber, .Details = outcome.Notes})
            Return Ok(d)
        End Function
    End Class

    <ApiController>
    <Route("api/triage")>
    Public Class TriageController
        Inherits ControllerBase

        Private ReadOnly _store As IDataStore
        Private ReadOnly _triage As ITriageService

        Public Sub New(store As IDataStore, triage As ITriageService)
            _store = store
            _triage = triage
        End Sub

        ''' <summary>AI Health Assistant: symptoms in plain language in, recommended specializations and nearby verified doctors out.</summary>
        <HttpPost>
        Public Async Function Analyze(req As TriageRequest) As Task(Of ActionResult(Of TriageResponse))
            If String.IsNullOrWhiteSpace(req.Text) Then Return BadRequest(New With {.errors = {"Please describe your symptoms."}})
            If req.Text.Length > 4000 Then Return BadRequest(New With {.errors = {"Please keep the description under 4000 characters."}})
            Dim result = Await _triage.AnalyzeAsync(req.Text)
            Dim here = If(req.Latitude.HasValue AndAlso req.Longitude.HasValue, New GeoPoint(req.Latitude.Value, req.Longitude.Value), Nothing)
            Return New TriageResponse With {.Triage = result, .Doctors = PublicViews.ForPatients(DoctorMatcher.Match(_store.Doctors(), result, here, 10))}
        End Function
    End Class

End Namespace
