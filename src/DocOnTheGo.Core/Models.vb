Option Strict On
Imports System.Collections.Generic

Namespace Models

    Public Enum Gender
        Unspecified = 0
        Female = 1
        Male = 2
        Other = 3
    End Enum

    ''' <summary>Primary field of expertise a doctor registers with.</summary>
    Public Enum Specialization
        GeneralPractice = 0
        InternalMedicine = 1
        Cardiology = 2
        Pediatrics = 3
        Dermatology = 4
        Neurology = 5
        Gastroenterology = 6
        Pulmonology = 7
        ObstetricsGynecology = 8
        Psychiatry = 9
        Orthopedics = 10
        ENT = 11
    End Enum

    Public Enum VerificationStatus
        NotSubmitted = 0
        InProgress = 1
        Verified = 2
        Rejected = 3
    End Enum

    Public Enum CredentialKind
        MedicalDegree = 0
        BoardCertification = 1
        PracticeLicense = 2
    End Enum

    Public Enum AppointmentStatus
        Requested = 0
        Confirmed = 1
        Completed = 2
        Cancelled = 3
    End Enum

    Public Enum ConsultationMode
        Online = 0
        InPerson = 1
    End Enum

    Public Enum Urgency
        Routine = 0
        Soon = 1
        Urgent = 2
        Emergency = 3
    End Enum

    Public Enum UserRole
        Patient = 0
        Doctor = 1
        Admin = 2
    End Enum

    Public Class GeoPoint
        Public Property Latitude As Double
        Public Property Longitude As Double

        Public Sub New()
        End Sub

        Public Sub New(lat As Double, lon As Double)
            Latitude = lat
            Longitude = lon
        End Sub
    End Class

    Public Class Patient
        Public Property Id As String = ""
        ''' <summary>Human-readable profile tag, e.g. PatientID-#8492.</summary>
        Public Property PatientTag As String = ""
        Public Property FullName As String = ""
        Public Property Email As String = ""
        Public Property Phone As String = ""
        Public Property DateOfBirth As Date
        Public Property Gender As Gender
        Public Property Location As GeoPoint
        Public Property MedicalHistory As New List(Of String)
        Public Property CreatedUtc As DateTime = DateTime.UtcNow
    End Class

    Public Class CredentialDocument
        Public Property Id As String = ""
        Public Property Kind As CredentialKind
        Public Property FileName As String = ""
        Public Property ContentType As String = ""
        Public Property SizeBytes As Long
        ''' <summary>Where the encrypted file is stored (local path or S3 key).</summary>
        Public Property StorageKey As String = ""
        Public Property UploadedUtc As DateTime = DateTime.UtcNow
        Public Property AutoCheckPassed As Boolean?
        Public Property AutoCheckNotes As String = ""
    End Class

    Public Class Doctor
        Public Property Id As String = ""
        Public Property FullName As String = ""
        Public Property Email As String = ""
        Public Property Phone As String = ""
        Public Property LicenseNumber As String = ""
        Public Property PrimarySpecialization As Specialization
        Public Property SubSpecialties As New List(Of String)
        Public Property Status As VerificationStatus = VerificationStatus.NotSubmitted
        Public Property StatusNotes As String = ""
        Public Property Credentials As New List(Of CredentialDocument)
        ''' <summary>Display tags such as "Generalist / GP" (assigned only after verification).</summary>
        Public Property Tags As New List(Of String)
        ''' <summary>Routing tags such as TagGP, TagCardio (assigned only after verification).</summary>
        Public Property SystemTags As New List(Of String)
        Public Property Rating As Double
        Public Property RatingCount As Integer
        Public Property Location As GeoPoint
        ''' <summary>Radius in km a mobile doctor / travelling unit covers.</summary>
        Public Property ServiceRadiusKm As Double = 50
        Public Property IsMobileUnit As Boolean
        Public Property OffersOnline As Boolean = True
        Public Property AvatarInitials As String = ""
        Public Property CreatedUtc As DateTime = DateTime.UtcNow

        Public ReadOnly Property IsVerified As Boolean
            Get
                Return Status = VerificationStatus.Verified
            End Get
        End Property
    End Class

    Public Class Appointment
        Public Property Id As String = ""
        Public Property PatientId As String = ""
        Public Property DoctorId As String = ""
        Public Property DoctorName As String = ""
        Public Property PatientName As String = ""
        Public Property ScheduledUtc As DateTime
        Public Property Mode As ConsultationMode
        Public Property Status As AppointmentStatus = AppointmentStatus.Requested
        Public Property Reason As String = ""
        Public Property CreatedUtc As DateTime = DateTime.UtcNow
    End Class

    Public Class ChatMessage
        Public Property Id As String = ""
        Public Property ConversationId As String = ""
        Public Property SenderId As String = ""
        Public Property SenderName As String = ""
        Public Property Text As String = ""
        Public Property SentUtc As DateTime = DateTime.UtcNow
    End Class

    Public Class Conversation
        Public Property Id As String = ""
        Public Property PatientId As String = ""
        Public Property DoctorId As String = ""
        Public Property PatientName As String = ""
        Public Property DoctorName As String = ""
        Public Property LastMessage As String = ""
        Public Property LastUtc As DateTime = DateTime.UtcNow
    End Class

    Public Class Prescription
        Public Property Id As String = ""
        Public Property AppointmentId As String = ""
        Public Property PatientId As String = ""
        Public Property DoctorId As String = ""
        Public Property Medication As String = ""
        Public Property Dosage As String = ""
        Public Property Instructions As String = ""
        Public Property FollowUp As String = ""
        ''' <summary>Short token the patient can show a pharmacy (also delivered by SMS).</summary>
        Public Property Token As String = ""
        Public Property IssuedUtc As DateTime = DateTime.UtcNow
    End Class

    Public Class SpecializationScore
        Public Property Specialization As Specialization
        Public Property Score As Double
        Public Property DisplayName As String = ""
    End Class

    Public Class TriageResult
        Public Property Urgency As Urgency
        Public Property Summary As String = ""
        Public Property Advice As String = ""
        Public Property Recommended As New List(Of SpecializationScore)
        Public Property ExtractedSymptoms As New List(Of String)
        ''' <summary>"rules" for the built-in engine, or the LLM model id when an AI provider answered.</summary>
        Public Property Engine As String = "rules"
        Public Property Disclaimer As String =
            "This assistant gives guidance only and is not a diagnosis. In an emergency, call your local emergency number."
    End Class

    Public Class DoctorMatch
        Public Property Doctor As Doctor
        Public Property DistanceKm As Double?
        Public Property MatchScore As Double
    End Class

End Namespace
