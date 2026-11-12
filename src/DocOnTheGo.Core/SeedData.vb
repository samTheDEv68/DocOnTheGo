Option Strict On
Imports System.Collections.Generic
Imports DocOnTheGo.Core.Models

Namespace Services

    ''' <summary>
    ''' Sample verified doctors used by the API in development and by the app's offline demo mode.
    ''' These are fictional placeholder profiles and must be removed before go-live.
    ''' </summary>
    Public Module SeedData
        Public Function SampleDoctors() As List(Of Doctor)
            Dim list As New List(Of Doctor) From {
                Make("Dr. A. Khan", "MDC-10234", Specialization.GeneralPractice, 4.9, 212, -1.2921, 36.8219, True),
                Make("Dr. B. Lee", "MDC-20871", Specialization.InternalMedicine, 4.8, 154, -1.3032, 36.7073, False),
                Make("Dr. S. Patel", "MDC-33410", Specialization.Pediatrics, 4.9, 301, -1.2833, 36.8167, True),
                Make("Dr. M. Okafor", "MDC-41922", Specialization.Cardiology, 4.7, 98, -1.2655, 36.8025, False),
                Make("Dr. L. Mensah", "MDC-55017", Specialization.GeneralPractice, 4.6, 76, -0.0917, 34.768, True),
                Make("Dr. R. Silva", "MDC-60333", Specialization.Dermatology, 4.5, 64, -1.3, 36.85, False),
                Make("Dr. N. Wanjiru", "MDC-71880", Specialization.ObstetricsGynecology, 4.8, 120, -1.31, 36.79, True),
                Make("Dr. T. Haddad", "MDC-82004", Specialization.Psychiatry, 4.7, 88, -1.27, 36.83, False)
            }
            Return list
        End Function

        Private Function Make(name As String, license As String, spec As Specialization, rating As Double, count As Integer,
                              lat As Double, lon As Double, mobile As Boolean) As Doctor
            Dim d As New Doctor With {
                .Id = IdGenerator.NewId(),
                .FullName = name,
                .Email = name.ToLowerInvariant().Replace("dr. ", "").Replace(". ", ".").Replace(" ", ".") & "@example.org",
                .LicenseNumber = license,
                .PrimarySpecialization = spec,
                .Status = VerificationStatus.Verified,
                .StatusNotes = "Seed profile (development only)",
                .Rating = rating,
                .RatingCount = count,
                .Location = New GeoPoint(lat, lon),
                .IsMobileUnit = mobile,
                .ServiceRadiusKm = If(mobile, 120, 40),
                .AvatarInitials = InitialsOf(name)
            }
            DoctorTagging.ApplyTags(d)
            Return d
        End Function
    End Module

End Namespace
