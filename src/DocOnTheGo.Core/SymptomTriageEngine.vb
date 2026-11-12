Option Strict On
Imports System.Collections.Generic
Imports System.Linq
Imports System.Text.RegularExpressions
Imports DocOnTheGo.Core.Models

Namespace Services

    ''' <summary>
    ''' Built-in, offline symptom triage (Functional Guide section 4). It extracts symptom
    ''' entities with a keyword ontology, flags red-flag urgency, and scores specializations.
    ''' The API can layer an LLM provider on top; this engine is always the fallback, so the
    ''' assistant still works with no network or no AI key.
    ''' </summary>
    Public Class SymptomTriageEngine

        Private Class Rule
            Public Property Symptom As String = ""
            Public Property Keywords As String() = {}
            Public Property Weights As New Dictionary(Of Specialization, Double)
        End Class

        Private ReadOnly _rules As New List(Of Rule)

        ' Phrases that need emergency care rather than a booking.
        Private Shared ReadOnly EmergencyFlags As String() = {
            "chest pain", "crushing chest", "can't breathe", "cannot breathe", "difficulty breathing",
            "shortness of breath", "unconscious", "passed out", "seizure", "fitting", "stroke",
            "face drooping", "slurred speech", "severe bleeding", "bleeding heavily", "vomiting blood",
            "coughing blood", "suicid", "kill myself", "overdose", "anaphylaxis", "throat swelling",
            "stiff neck and fever", "baby not breathing", "blue lips"}

        Private Shared ReadOnly UrgentFlags As String() = {
            "high fever", "fever for 3 days", "fever for three days", "severe pain", "worst headache",
            "dehydrat", "pregnan", "blood in urine", "blood in stool", "fainting", "confus",
            "infant", "newborn", "broken", "fracture", "deep cut", "burn"}

        Private Shared ReadOnly ChildWords As String() = {
            "my child", "my son", "my daughter", "my baby", "toddler", "infant", "newborn", "kid ", "kids", "year old boy", "year old girl"}

        Public Sub New()
            Add("headache", {"headache", "migraine", "head pain", "head ache"},
                (Specialization.GeneralPractice, 0.8), (Specialization.InternalMedicine, 0.5), (Specialization.Neurology, 0.5))
            Add("fever", {"fever", "temperature", "chills", "feverish"},
                (Specialization.GeneralPractice, 0.9), (Specialization.InternalMedicine, 0.6))
            Add("cough", {"cough", "coughing", "phlegm", "sputum"},
                (Specialization.GeneralPractice, 0.8), (Specialization.Pulmonology, 0.6))
            Add("breathing difficulty", {"shortness of breath", "wheez", "breathless", "asthma"},
                (Specialization.Pulmonology, 0.9), (Specialization.InternalMedicine, 0.6))
            Add("sore throat", {"sore throat", "throat pain", "tonsil", "hoarse"},
                (Specialization.GeneralPractice, 0.8), (Specialization.ENT, 0.6))
            Add("ear problem", {"ear pain", "earache", "ear ache", "hearing", "ringing in"},
                (Specialization.ENT, 0.9), (Specialization.GeneralPractice, 0.5))
            Add("chest symptoms", {"chest pain", "palpitation", "heart racing", "irregular heartbeat", "chest tight"},
                (Specialization.Cardiology, 1.0), (Specialization.InternalMedicine, 0.6))
            Add("blood pressure", {"blood pressure", "hypertension", "bp is high"},
                (Specialization.Cardiology, 0.8), (Specialization.InternalMedicine, 0.7))
            Add("abdominal pain", {"stomach", "abdominal", "belly", "tummy"},
                (Specialization.Gastroenterology, 0.8), (Specialization.GeneralPractice, 0.6))
            Add("nausea / vomiting", {"nausea", "vomit", "throwing up", "diarrh", "constipat"},
                (Specialization.GeneralPractice, 0.7), (Specialization.Gastroenterology, 0.7))
            Add("skin problem", {"rash", "itch", "acne", "eczema", "skin", "mole", "hives"},
                (Specialization.Dermatology, 0.9), (Specialization.GeneralPractice, 0.4))
            Add("dizziness / numbness", {"dizz", "numb", "tingling", "vertigo", "weakness on one side"},
                (Specialization.Neurology, 0.8), (Specialization.InternalMedicine, 0.5))
            Add("joint / bone pain", {"joint", "back pain", "knee", "sprain", "fracture", "broken", "shoulder pain"},
                (Specialization.Orthopedics, 0.9), (Specialization.GeneralPractice, 0.4))
            Add("mental health", {"anxiety", "depress", "panic", "can't sleep", "insomnia", "stress", "suicid", "hopeless"},
                (Specialization.Psychiatry, 1.0), (Specialization.GeneralPractice, 0.4))
            Add("pregnancy / women's health", {"pregnan", "period", "menstrua", "vaginal", "pelvic"},
                (Specialization.ObstetricsGynecology, 1.0), (Specialization.GeneralPractice, 0.3))
            Add("diabetes symptoms", {"diabet", "thirsty all the time", "frequent urination", "blood sugar"},
                (Specialization.InternalMedicine, 0.9), (Specialization.GeneralPractice, 0.5))
            Add("fatigue", {"tired", "fatigue", "exhausted", "no energy", "weak"},
                (Specialization.GeneralPractice, 0.7), (Specialization.InternalMedicine, 0.6))
            Add("cold / flu", {"runny nose", "sneez", "flu", "congest", "body ache"},
                (Specialization.GeneralPractice, 0.9))
        End Sub

        Private Sub Add(symptom As String, keywords As String(), ParamArray weights As (Specialization, Double)())
            Dim r As New Rule With {.Symptom = symptom, .Keywords = keywords}
            For Each w In weights
                r.Weights(w.Item1) = w.Item2
            Next
            _rules.Add(r)
        End Sub

        Public Function Analyze(text As String) As TriageResult
            Dim input = " " & Regex.Replace(If(text, "").ToLowerInvariant(), "\s+", " ") & " "
            Dim result As New TriageResult()
            Dim scores As New Dictionary(Of Specialization, Double)

            For Each r In _rules
                If r.Keywords.Any(Function(k) input.Contains(k)) Then
                    result.ExtractedSymptoms.Add(r.Symptom)
                    For Each kv In r.Weights
                        Dim current As Double = 0
                        scores.TryGetValue(kv.Key, current)
                        scores(kv.Key) = current + kv.Value
                    Next
                End If
            Next

            Dim isChild = ChildWords.Any(Function(w) input.Contains(w)) OrElse Regex.IsMatch(input, "\b([0-9]|1[0-5]) ?(yo|y/o|year old|years old|month old|months old)\b")

            ' Duration / severity modifiers.
            Dim severe = Regex.IsMatch(input, "\b(severe|very bad|unbearable|worst|getting worse)\b")
            Dim persistent = Regex.IsMatch(input, "\b(persistent|weeks?|months?|chronic|keeps coming back|for days)\b")
            If persistent Then
                Dim current As Double = 0
                scores.TryGetValue(Specialization.InternalMedicine, current)
                scores(Specialization.InternalMedicine) = current + 0.4
            End If

            If EmergencyFlags.Any(Function(f) input.Contains(f)) Then
                result.Urgency = Urgency.Emergency
            ElseIf UrgentFlags.Any(Function(f) input.Contains(f)) OrElse severe Then
                result.Urgency = Urgency.Urgent
            ElseIf persistent OrElse result.ExtractedSymptoms.Count >= 3 Then
                result.Urgency = Urgency.Soon
            Else
                result.Urgency = Urgency.Routine
            End If

            If scores.Count = 0 Then scores(Specialization.GeneralPractice) = 1.0

            ' Children are routed to paediatrics first, ahead of any adult specialty.
            If isChild Then scores(Specialization.Pediatrics) = scores.Values.Max() + 0.5

            Dim top = scores.Values.Max()
            result.Recommended = scores.OrderByDescending(Function(kv) kv.Value).
                Take(3).
                Where(Function(kv) kv.Value >= top * 0.5).
                Select(Function(kv) New SpecializationScore With {
                    .Specialization = kv.Key,
                    .Score = Math.Round(kv.Value / top, 2),
                    .DisplayName = DisplayName(kv.Key)}).
                ToList()

            result.Summary = BuildSummary(result)
            result.Advice = BuildAdvice(result.Urgency)
            Return result
        End Function

        Private Shared Function BuildSummary(r As TriageResult) As String
            Dim names = r.Recommended.Select(Function(s) s.DisplayName).ToList()
            Dim specs As String
            If names.Count = 1 Then
                specs = names(0)
            Else
                specs = String.Join(", ", names.Take(names.Count - 1)) & " or " & names.Last()
            End If
            If r.ExtractedSymptoms.Count = 0 Then
                Return $"I couldn't match specific symptoms, so I recommend starting with a {specs}."
            End If
            Return $"Based on your symptoms ({String.Join(", ", r.ExtractedSymptoms)}), I recommend consulting a {specs} specialist."
        End Function

        Public Shared Function BuildAdvice(u As Urgency) As String
            Select Case u
                Case Urgency.Emergency
                    Return "Your symptoms may need emergency care. Call your local emergency number or go to the nearest clinic now. Do not wait for an online appointment."
                Case Urgency.Urgent
                    Return "Please see a doctor today. Book the earliest available slot or visit a clinic."
                Case Urgency.Soon
                    Return "Book a consultation within the next few days. Seek care sooner if things get worse."
                Case Else
                    Return "A routine consultation should be fine. Rest, stay hydrated, and book if symptoms continue."
            End Select
        End Function
    End Class

End Namespace
