Option Strict On
Imports System.Linq
Imports DocOnTheGo.Core.Models
Imports DocOnTheGo.Core.Services
Imports Xunit

Public Class TriageTests
    Private ReadOnly _engine As New SymptomTriageEngine()

    <Fact>
    Public Sub HeadacheAndFever_RecommendsGpThenInternalMedicine()
        Dim r = _engine.Analyze("I have a persistent headache and mild fever.")
        Assert.Equal(Specialization.GeneralPractice, r.Recommended(0).Specialization)
        Assert.Contains(r.Recommended, Function(x) x.Specialization = Specialization.InternalMedicine)
        Assert.Contains("headache", r.ExtractedSymptoms)
        Assert.Equal(Urgency.Soon, r.Urgency)
    End Sub

    <Theory>
    <InlineData("I have crushing chest pain")>
    <InlineData("my dad has slurred speech and face drooping")>
    <InlineData("I can't breathe")>
    Public Sub RedFlags_AreEmergency(text As String)
        Assert.Equal(Urgency.Emergency, _engine.Analyze(text).Urgency)
    End Sub

    <Fact>
    Public Sub Child_RoutesToPediatrics()
        Dim r = _engine.Analyze("My daughter has a cough and runny nose")
        Assert.Equal(Specialization.Pediatrics, r.Recommended(0).Specialization)
    End Sub

    <Fact>
    Public Sub UnknownText_FallsBackToGp()
        Dim r = _engine.Analyze("not feeling right")
        Assert.Equal(Specialization.GeneralPractice, r.Recommended(0).Specialization)
    End Sub
End Class

Public Class TaggingTests
    <Theory>
    <InlineData(Specialization.GeneralPractice, "Generalist / GP", "TagGP")>
    <InlineData(Specialization.Cardiology, "Specialist", "TagCardio")>
    <InlineData(Specialization.Pediatrics, "Child Specialist", "TagPeds")>
    Public Sub VerifiedDoctor_GetsTags(spec As Specialization, tag As String, systemTag As String)
        Dim d As New Doctor With {.PrimarySpecialization = spec, .Status = VerificationStatus.Verified}
        DoctorTagging.ApplyTags(d)
        Assert.Contains(tag, d.Tags)
        Assert.Contains(systemTag, d.SystemTags)
    End Sub

    <Fact>
    Public Sub UnverifiedDoctor_GetsNoTags()
        Dim d As New Doctor With {.PrimarySpecialization = Specialization.Cardiology, .Status = VerificationStatus.InProgress}
        DoctorTagging.ApplyTags(d)
        Assert.Empty(d.Tags)
    End Sub
End Class

Public Class ValidationAndMatchingTests
    <Fact>
    Public Sub PatientTag_HasExpectedFormat()
        Assert.Matches("^PatientID-#\d{4,6}$", IdGenerator.NewPatientTag(Nothing))
    End Sub

    <Fact>
    Public Sub CredentialFiles_OnlyJpegPngPdf()
        Assert.True(Validation.ValidateCredentialFile("degree.pdf", 1000).IsValid)
        Assert.True(Validation.ValidateCredentialFile("board.PNG", 1000).IsValid)
        Assert.False(Validation.ValidateCredentialFile("degree.docx", 1000).IsValid)
        Assert.False(Validation.ValidateCredentialFile("big.pdf", 50L * 1024 * 1024).IsValid)
        Assert.True(Validation.HasAllowedSignature(New Byte() {&H25, &H50, &H44, &H46, 0}))
        Assert.False(Validation.HasAllowedSignature(New Byte() {&H4D, &H5A, 0, 0}))
    End Sub

    <Fact>
    Public Sub Patient_RequiresValidFields()
        Dim r = Validation.ValidatePatient("", "bad", "12", Date.Today.AddDays(1), Date.Today)
        Assert.Equal(4, r.Errors.Count)
    End Sub

    <Fact>
    Public Sub Matcher_ReturnsOnlyVerifiedMatchingDoctors_NearestFirst()
        Dim docs = SeedData.SampleDoctors()
        Dim pending As New Doctor With {.FullName = "Dr. Pending", .PrimarySpecialization = Specialization.GeneralPractice, .Status = VerificationStatus.InProgress}
        docs.Add(pending)
        Dim triage = New SymptomTriageEngine().Analyze("headache and fever")
        Dim matches = DoctorMatcher.Match(docs, triage, New GeoPoint(-1.2921, 36.8219))
        Assert.DoesNotContain(matches, Function(m) m.Doctor Is pending)
        Assert.All(matches, Sub(m) Assert.Contains(m.Doctor.PrimarySpecialization, {Specialization.GeneralPractice, Specialization.InternalMedicine}))
        Assert.Equal("Dr. A. Khan", matches(0).Doctor.FullName)
    End Sub

    <Fact>
    Public Sub Distance_NairobiToKisumu_IsAbout265Km()
        Dim km = Geo.DistanceKm(New GeoPoint(-1.2921, 36.8219), New GeoPoint(-0.0917, 34.768))
        Assert.InRange(km, 255, 275)
    End Sub
End Class
