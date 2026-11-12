Option Strict On
Imports System.Collections.Generic
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text.RegularExpressions
Imports DocOnTheGo.Core.Models

Namespace Services

    Public Module SpecializationInfo
        Public Function DisplayName(s As Specialization) As String
            Select Case s
                Case Specialization.GeneralPractice : Return "General Practitioner"
                Case Specialization.InternalMedicine : Return "Internal Medicine"
                Case Specialization.Cardiology : Return "Cardiology"
                Case Specialization.Pediatrics : Return "Pediatrics"
                Case Specialization.Dermatology : Return "Dermatology"
                Case Specialization.Neurology : Return "Neurology"
                Case Specialization.Gastroenterology : Return "Gastroenterology"
                Case Specialization.Pulmonology : Return "Pulmonology"
                Case Specialization.ObstetricsGynecology : Return "Obstetrics & Gynecology"
                Case Specialization.Psychiatry : Return "Psychiatry"
                Case Specialization.Orthopedics : Return "Orthopedics"
                Case Specialization.ENT : Return "Ear, Nose & Throat"
                Case Else : Return s.ToString()
            End Select
        End Function

        Public Function AllSpecializations() As IReadOnlyList(Of Specialization)
            Return CType([Enum].GetValues(GetType(Specialization)), Specialization()).ToList()
        End Function
    End Module

    ''' <summary>
    ''' Specialized tagging system (Functional Guide section 3, Architecture section 3).
    ''' Tags are only issued once a doctor is a Verified Professional.
    ''' </summary>
    Public Module DoctorTagging
        Public Const TagGeneralist As String = "Generalist / GP"
        Public Const TagSpecialist As String = "Specialist"
        Public Const TagChildSpecialist As String = "Child Specialist"
        Public Const TagPrimaryCare As String = "Primary Care"
        Public Const TagMobileUnit As String = "Mobile Unit"

        Public Function DisplayTagsFor(s As Specialization) As List(Of String)
            Select Case s
                Case Specialization.GeneralPractice
                    Return New List(Of String) From {TagGeneralist, TagPrimaryCare}
                Case Specialization.Pediatrics
                    Return New List(Of String) From {TagChildSpecialist}
                Case Else
                    Return New List(Of String) From {TagSpecialist}
            End Select
        End Function

        Public Function SystemTagFor(s As Specialization) As String
            Select Case s
                Case Specialization.GeneralPractice : Return "TagGP"
                Case Specialization.InternalMedicine : Return "TagInternal"
                Case Specialization.Cardiology : Return "TagCardio"
                Case Specialization.Pediatrics : Return "TagPeds"
                Case Specialization.Dermatology : Return "TagDerm"
                Case Specialization.Neurology : Return "TagNeuro"
                Case Specialization.Gastroenterology : Return "TagGastro"
                Case Specialization.Pulmonology : Return "TagPulmo"
                Case Specialization.ObstetricsGynecology : Return "TagObGyn"
                Case Specialization.Psychiatry : Return "TagPsych"
                Case Specialization.Orthopedics : Return "TagOrtho"
                Case Specialization.ENT : Return "TagENT"
                Case Else : Return "Tag" & s.ToString()
            End Select
        End Function

        ''' <summary>Recomputes a doctor's display and routing tags from status and qualifications.</summary>
        Public Sub ApplyTags(d As Doctor)
            d.Tags.Clear()
            d.SystemTags.Clear()
            If Not d.IsVerified Then Return
            d.Tags.AddRange(DisplayTagsFor(d.PrimarySpecialization))
            d.SystemTags.Add(SystemTagFor(d.PrimarySpecialization))
            If d.IsMobileUnit Then d.Tags.Add(TagMobileUnit)
            For Each subSpec In d.SubSpecialties
                Dim parsed As Specialization
                If [Enum].TryParse(subSpec, True, parsed) Then
                    Dim t = SystemTagFor(parsed)
                    If Not d.SystemTags.Contains(t) Then d.SystemTags.Add(t)
                End If
            Next
        End Sub
    End Module

    Public Module IdGenerator
        Public Function NewId() As String
            Return Guid.NewGuid().ToString("N")
        End Function

        ''' <summary>Unique patient profile tag in the form PatientID-#8492.</summary>
        Public Function NewPatientTag(existing As ICollection(Of String)) As String
            For attempt = 1 To 1000
                Dim digits = If(attempt < 200, RandomNumberGenerator.GetInt32(1000, 10000), RandomNumberGenerator.GetInt32(10000, 1000000))
                Dim tag = $"PatientID-#{digits}"
                If existing Is Nothing OrElse Not existing.Contains(tag) Then Return tag
            Next
            Return $"PatientID-#{NewId().Substring(0, 8).ToUpperInvariant()}"
        End Function

        Public Function NewOtp() As String
            Return RandomNumberGenerator.GetInt32(0, 1000000).ToString("D6")
        End Function

        Public Function NewPrescriptionToken() As String
            Const alphabet As String = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"
            Dim chars(7) As Char
            For i = 0 To chars.Length - 1
                chars(i) = alphabet(RandomNumberGenerator.GetInt32(alphabet.Length))
            Next
            Return "RX-" & New String(chars)
        End Function

        Public Function InitialsOf(name As String) As String
            Dim parts = (If(name, "")).Replace("Dr.", "").Split({" "c}, StringSplitOptions.RemoveEmptyEntries)
            If parts.Length = 0 Then Return "?"
            If parts.Length = 1 Then Return parts(0).Substring(0, 1).ToUpperInvariant()
            Return (parts(0).Substring(0, 1) & parts(parts.Length - 1).Substring(0, 1)).ToUpperInvariant()
        End Function
    End Module

    Public Class ValidationResult
        Public ReadOnly Property Errors As New List(Of String)
        Public ReadOnly Property IsValid As Boolean
            Get
                Return Errors.Count = 0
            End Get
        End Property
    End Class

    Public Module Validation
        Private ReadOnly EmailRx As New Regex("^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled)
        Private ReadOnly PhoneRx As New Regex("^\+?[0-9 ()\-]{7,20}$", RegexOptions.Compiled)
        Private ReadOnly LicenseRx As New Regex("^[A-Za-z0-9\-/]{4,30}$", RegexOptions.Compiled)

        Public ReadOnly AllowedCredentialExtensions As String() = {".jpg", ".jpeg", ".png", ".pdf"}
        Public Const MaxCredentialBytes As Long = 10L * 1024 * 1024

        Public Function IsEmail(v As String) As Boolean
            Return Not String.IsNullOrWhiteSpace(v) AndAlso EmailRx.IsMatch(v.Trim())
        End Function

        Public Function IsPhone(v As String) As Boolean
            Return Not String.IsNullOrWhiteSpace(v) AndAlso PhoneRx.IsMatch(v.Trim())
        End Function

        Public Function NormalizePhone(v As String) As String
            If v Is Nothing Then Return ""
            Dim plus = v.Trim().StartsWith("+")
            Dim digits = New String(v.Where(Function(c) Char.IsDigit(c)).ToArray())
            Return If(plus, "+", "") & digits
        End Function

        Public Function ValidatePatient(fullName As String, email As String, phone As String, dob As Date, today As Date) As ValidationResult
            Dim r As New ValidationResult()
            If String.IsNullOrWhiteSpace(fullName) OrElse fullName.Trim().Length < 2 Then r.Errors.Add("Full name is required.")
            If Not IsEmail(email) Then r.Errors.Add("A valid email address is required.")
            If Not IsPhone(phone) Then r.Errors.Add("A valid phone number is required.")
            If dob > today Then r.Errors.Add("Date of birth cannot be in the future.")
            If dob < today.AddYears(-130) Then r.Errors.Add("Date of birth is not valid.")
            Return r
        End Function

        Public Function ValidateDoctor(fullName As String, licenseNumber As String, email As String, phone As String) As ValidationResult
            Dim r As New ValidationResult()
            If String.IsNullOrWhiteSpace(fullName) OrElse fullName.Trim().Length < 2 Then r.Errors.Add("Professional name is required.")
            If String.IsNullOrWhiteSpace(licenseNumber) OrElse Not LicenseRx.IsMatch(licenseNumber.Trim()) Then
                r.Errors.Add("Medical license number must be 4 to 30 letters, digits, '-' or '/'.")
            End If
            If Not IsEmail(email) Then r.Errors.Add("A valid email address is required.")
            If Not String.IsNullOrWhiteSpace(phone) AndAlso Not IsPhone(phone) Then r.Errors.Add("Phone number is not valid.")
            Return r
        End Function

        Public Function ValidateCredentialFile(fileName As String, sizeBytes As Long) As ValidationResult
            Dim r As New ValidationResult()
            Dim ext = IO.Path.GetExtension(If(fileName, "")).ToLowerInvariant()
            If Not AllowedCredentialExtensions.Contains(ext) Then r.Errors.Add("Credentials must be JPEG, PNG or PDF files.")
            If sizeBytes <= 0 Then r.Errors.Add("The file is empty.")
            If sizeBytes > MaxCredentialBytes Then r.Errors.Add("Files must be 10 MB or smaller.")
            Return r
        End Function

        ''' <summary>Checks the file's leading bytes match a JPEG, PNG or PDF signature.</summary>
        Public Function HasAllowedSignature(header As Byte()) As Boolean
            If header Is Nothing OrElse header.Length < 4 Then Return False
            If header(0) = &HFF AndAlso header(1) = &HD8 AndAlso header(2) = &HFF Then Return True ' JPEG
            If header(0) = &H89 AndAlso header(1) = &H50 AndAlso header(2) = &H4E AndAlso header(3) = &H47 Then Return True ' PNG
            If header(0) = &H25 AndAlso header(1) = &H50 AndAlso header(2) = &H44 AndAlso header(3) = &H46 Then Return True ' %PDF
            Return False
        End Function

        Public Function ContentTypeFor(fileName As String) As String
            Select Case IO.Path.GetExtension(If(fileName, "")).ToLowerInvariant()
                Case ".jpg", ".jpeg" : Return "image/jpeg"
                Case ".png" : Return "image/png"
                Case ".pdf" : Return "application/pdf"
                Case Else : Return "application/octet-stream"
            End Select
        End Function
    End Module

    Public Module Geo
        ''' <summary>Great-circle distance in kilometres.</summary>
        Public Function DistanceKm(a As GeoPoint, b As GeoPoint) As Double
            Const R As Double = 6371.0
            Dim dLat = ToRad(b.Latitude - a.Latitude)
            Dim dLon = ToRad(b.Longitude - a.Longitude)
            Dim h = Math.Sin(dLat / 2) ^ 2 + Math.Cos(ToRad(a.Latitude)) * Math.Cos(ToRad(b.Latitude)) * Math.Sin(dLon / 2) ^ 2
            Return 2 * R * Math.Asin(Math.Min(1.0, Math.Sqrt(h)))
        End Function

        Private Function ToRad(deg As Double) As Double
            Return deg * Math.PI / 180.0
        End Function
    End Module

    ''' <summary>
    ''' Geospatial proximity matching (Functional Guide section 5): verified doctors whose
    ''' specialization fits the triage result, within their service radius, best first.
    ''' </summary>
    Public Module PublicViews
        ''' <summary>Copy of a doctor safe to show patients: no contact details or credential files.</summary>
        Public Function ForPatients(d As Doctor) As Doctor
            Return New Doctor With {
                .Id = d.Id, .FullName = d.FullName, .LicenseNumber = d.LicenseNumber,
                .PrimarySpecialization = d.PrimarySpecialization, .SubSpecialties = New List(Of String)(d.SubSpecialties),
                .Status = d.Status, .Tags = New List(Of String)(d.Tags), .SystemTags = New List(Of String)(d.SystemTags),
                .Rating = d.Rating, .RatingCount = d.RatingCount, .Location = d.Location,
                .ServiceRadiusKm = d.ServiceRadiusKm, .IsMobileUnit = d.IsMobileUnit, .OffersOnline = d.OffersOnline,
                .AvatarInitials = d.AvatarInitials, .CreatedUtc = d.CreatedUtc}
        End Function

        Public Function ForPatients(matches As IEnumerable(Of DoctorMatch)) As List(Of DoctorMatch)
            Return matches.Select(Function(m) New DoctorMatch With {.Doctor = ForPatients(m.Doctor), .DistanceKm = m.DistanceKm, .MatchScore = m.MatchScore}).ToList()
        End Function
    End Module

    Public Module DoctorMatcher
        Public Function Match(doctors As IEnumerable(Of Doctor),
                              triage As TriageResult,
                              patientLocation As GeoPoint,
                              Optional maxResults As Integer = 10) As List(Of DoctorMatch)
            Dim wanted = If(triage?.Recommended, New List(Of SpecializationScore)())
            Dim results As New List(Of DoctorMatch)
            For Each d In doctors.Where(Function(x) x.IsVerified)
                Dim specScore = wanted.Where(Function(w) w.Specialization = d.PrimarySpecialization).Select(Function(w) w.Score).DefaultIfEmpty(0).Max()
                If wanted.Count > 0 AndAlso specScore <= 0 Then Continue For

                Dim distance As Double? = Nothing
                If patientLocation IsNot Nothing AndAlso d.Location IsNot Nothing Then
                    distance = DistanceKm(patientLocation, d.Location)
                    If distance.Value > d.ServiceRadiusKm AndAlso Not d.OffersOnline Then Continue For
                End If

                Dim proximity = If(distance.HasValue, Math.Max(0, 1 - distance.Value / Math.Max(1, d.ServiceRadiusKm)), 0.5)
                Dim rating = d.Rating / 5.0
                Dim score = specScore * 0.6 + proximity * 0.25 + rating * 0.15
                If wanted.Count = 0 Then score = proximity * 0.5 + rating * 0.5
                results.Add(New DoctorMatch With {.Doctor = d, .DistanceKm = distance, .MatchScore = Math.Round(score, 3)})
            Next
            Return results.OrderByDescending(Function(m) m.MatchScore).Take(maxResults).ToList()
        End Function
    End Module

End Namespace
