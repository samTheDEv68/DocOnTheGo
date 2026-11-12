Option Strict On
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports DocOnTheGo.Api.Contracts
Imports DocOnTheGo.Api.Infrastructure
Imports DocOnTheGo.Core.Models
Imports DocOnTheGo.Core.Services
Imports Microsoft.AspNetCore.Mvc
Imports Microsoft.AspNetCore.Mvc.Filters
Imports Microsoft.Extensions.Configuration

Namespace Controllers

    ''' <summary>Requires the X-Admin-Key header to match DocOnTheGo:AdminApiKey.</summary>
    Public Class AdminKeyAttribute
        Inherits ActionFilterAttribute

        Public Overrides Sub OnActionExecuting(context As ActionExecutingContext)
            Dim cfg = CType(context.HttpContext.RequestServices.GetService(GetType(IConfiguration)), IConfiguration)
            Dim expected = cfg("DocOnTheGo:AdminApiKey")
            Dim given = context.HttpContext.Request.Headers("X-Admin-Key").ToString()
            If String.IsNullOrWhiteSpace(expected) OrElse
               Not CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(given)) Then
                context.Result = New UnauthorizedResult()
            End If
        End Sub
    End Class

    ''' <summary>Endpoints used by the VB.NET credential review console (DocOnTheGo.Admin).</summary>
    <ApiController>
    <AdminKey>
    <Route("api/admin")>
    Public Class AdminController
        Inherits ControllerBase

        Private ReadOnly _store As IDataStore
        Private ReadOnly _files As IFileStore

        Public Sub New(store As IDataStore, files As IFileStore)
            _store = store
            _files = files
        End Sub

        <HttpGet("doctors")>
        Public Function Doctors(<FromQuery> status As VerificationStatus?) As IActionResult
            Dim list = _store.Doctors()
            If status.HasValue Then list = list.Where(Function(d) d.Status = status.Value)
            Return Ok(list.OrderBy(Function(d) d.CreatedUtc))
        End Function

        <HttpGet("doctors/{id}/credentials/{credentialId}")>
        Public Function DownloadCredential(id As String, credentialId As String) As IActionResult
            Dim d = _store.FindDoctor(id)
            Dim c = d?.Credentials.FirstOrDefault(Function(x) x.Id = credentialId)
            If c Is Nothing Then Return NotFound()
            _store.AddAudit(New AuditEntry With {.Actor = "admin", .Action = "credential.view", .Subject = d.LicenseNumber, .Details = c.FileName})
            Return File(_files.Load(c.StorageKey), c.ContentType, c.FileName)
        End Function

        ''' <summary>Manual review override: approve (Verified Professional) or reject.</summary>
        <HttpPost("doctors/{id}/review")>
        Public Function Review(id As String, req As ReviewDecisionRequest) As IActionResult
            Dim d = _store.FindDoctor(id)
            If d Is Nothing Then Return NotFound()
            d.Status = If(req.Approve, VerificationStatus.Verified, VerificationStatus.Rejected)
            d.StatusNotes = If(String.IsNullOrWhiteSpace(req.Notes), If(req.Approve, "Approved by reviewer.", "Rejected by reviewer."), req.Notes)
            DoctorTagging.ApplyTags(d)
            _store.SaveDoctor(d)
            _store.AddAudit(New AuditEntry With {
                .Actor = If(String.IsNullOrWhiteSpace(req.Reviewer), "admin", req.Reviewer),
                .Action = If(req.Approve, "verification.approve", "verification.reject"),
                .Subject = d.LicenseNumber, .Details = d.StatusNotes})
            Return Ok(d)
        End Function

        <HttpGet("audit")>
        Public Function Audit() As IActionResult
            Return Ok(_store.AuditLog().OrderByDescending(Function(a) a.Utc).Take(500))
        End Function
    End Class

End Namespace
