Option Strict On
Imports System.Collections.Generic
Imports DocOnTheGo.Core.Models

''' <summary>Request/response shapes shared with the mobile app (mirrored in DocOnTheGo.Mobile/Services/ApiModels.cs).</summary>
Namespace Contracts

    Public Class RegisterPatientRequest
        Public Property FullName As String = ""
        Public Property Email As String = ""
        Public Property Phone As String = ""
        Public Property DateOfBirth As Date
        Public Property Gender As Gender
        Public Property Latitude As Double?
        Public Property Longitude As Double?
    End Class

    Public Class RegisterDoctorRequest
        Public Property FullName As String = ""
        Public Property Email As String = ""
        Public Property Phone As String = ""
        Public Property LicenseNumber As String = ""
        Public Property PrimarySpecialization As Specialization
        Public Property SubSpecialties As New List(Of String)
        Public Property IsMobileUnit As Boolean
        Public Property ServiceRadiusKm As Double = 50
        Public Property Latitude As Double?
        Public Property Longitude As Double?
    End Class

    Public Class OtpRequest
        Public Property Contact As String = ""
    End Class

    Public Class OtpVerifyRequest
        Public Property Contact As String = ""
        Public Property Code As String = ""
    End Class

    Public Class AuthResponse
        Public Property Token As String = ""
        Public Property Role As String = ""
        Public Property UserId As String = ""
        Public Property DisplayName As String = ""
        Public Property PatientTag As String = ""
        ''' <summary>Only returned when DocOnTheGo:ReturnOtpInResponse is true (development).</summary>
        Public Property DevOtp As String
    End Class

    Public Class TriageRequest
        Public Property Text As String = ""
        Public Property Latitude As Double?
        Public Property Longitude As Double?
    End Class

    Public Class TriageResponse
        Public Property Triage As TriageResult
        Public Property Doctors As New List(Of DoctorMatch)
    End Class

    Public Class BookAppointmentRequest
        Public Property DoctorId As String = ""
        Public Property ScheduledUtc As DateTime
        Public Property Mode As ConsultationMode
        Public Property Reason As String = ""
    End Class

    Public Class UpdateAppointmentRequest
        Public Property Status As AppointmentStatus
    End Class

    Public Class SendMessageRequest
        Public Property Text As String = ""
    End Class

    Public Class StartConversationRequest
        Public Property DoctorId As String = ""
        Public Property PatientId As String = ""
    End Class

    Public Class IssuePrescriptionRequest
        Public Property AppointmentId As String = ""
        Public Property Medication As String = ""
        Public Property Dosage As String = ""
        Public Property Instructions As String = ""
        Public Property FollowUp As String = ""
    End Class

    Public Class ReviewDecisionRequest
        Public Property Approve As Boolean
        Public Property Notes As String = ""
        Public Property Reviewer As String = ""
    End Class

End Namespace
