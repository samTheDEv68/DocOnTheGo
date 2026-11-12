Option Strict On
Imports System.Collections.Generic
Imports System.Linq
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Net.Http.Json
Imports System.Text
Imports System.Text.Json
Imports System.Text.Json.Nodes
Imports System.Threading.Tasks
Imports DocOnTheGo.Core.Models
Imports DocOnTheGo.Core.Services
Imports Microsoft.Extensions.Configuration
Imports Microsoft.Extensions.Logging

Namespace Infrastructure

    ' ---------------------------------------------------------------- SMS / notifications

    Public Interface ISmsSender
        Function SendAsync(toNumber As String, message As String) As Task(Of Boolean)
    End Interface

    ''' <summary>Development sender: writes the SMS to the server log instead of sending it.</summary>
    Public Class ConsoleSmsSender
        Implements ISmsSender
        Private ReadOnly _log As ILogger(Of ConsoleSmsSender)

        Public Sub New(log As ILogger(Of ConsoleSmsSender))
            _log = log
        End Sub

        Public Function SendAsync(toNumber As String, message As String) As Task(Of Boolean) Implements ISmsSender.SendAsync
            _log.LogInformation("[SMS to {To}] {Message}", toNumber, message)
            Return Task.FromResult(True)
        End Function
    End Class

    ''' <summary>Twilio Programmable Messaging over REST. Needs Sms:TwilioAccountSid, TwilioAuthToken, TwilioFromNumber.</summary>
    Public Class TwilioSmsSender
        Implements ISmsSender
        Private ReadOnly _http As HttpClient
        Private ReadOnly _cfg As IConfiguration
        Private ReadOnly _log As ILogger(Of TwilioSmsSender)

        Public Sub New(http As HttpClient, cfg As IConfiguration, log As ILogger(Of TwilioSmsSender))
            _http = http
            _cfg = cfg
            _log = log
        End Sub

        Public Async Function SendAsync(toNumber As String, message As String) As Task(Of Boolean) Implements ISmsSender.SendAsync
            Dim sid = _cfg("Sms:TwilioAccountSid")
            Dim token = _cfg("Sms:TwilioAuthToken")
            Dim from = _cfg("Sms:TwilioFromNumber")
            Dim req As New HttpRequestMessage(HttpMethod.Post, $"https://api.twilio.com/2010-04-01/Accounts/{sid}/Messages.json")
            req.Headers.Authorization = New AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{sid}:{token}")))
            req.Content = New FormUrlEncodedContent(New Dictionary(Of String, String) From {{"To", toNumber}, {"From", from}, {"Body", message}})
            Dim res = Await _http.SendAsync(req)
            If Not res.IsSuccessStatusCode Then
                _log.LogWarning("Twilio SMS failed: {Status} {Body}", res.StatusCode, Await res.Content.ReadAsStringAsync())
            End If
            Return res.IsSuccessStatusCode
        End Function
    End Class

    ' ---------------------------------------------------------------- Credential verification

    Public Class VerificationOutcome
        Public Property Status As VerificationStatus
        Public Property Notes As String = ""
    End Class

    ''' <summary>
    ''' Primary-source verification of a doctor's licence and credentials (Architecture section 4).
    ''' </summary>
    Public Interface ICredentialVerifier
        Function VerifyAsync(d As Doctor) As Task(Of VerificationOutcome)
    End Interface

    ''' <summary>
    ''' Default: runs automated file checks, then leaves the doctor "Verification in Progress"
    ''' for a human reviewer in the VB.NET admin console.
    ''' </summary>
    Public Class ManualReviewVerifier
        Implements ICredentialVerifier

        Public Function VerifyAsync(d As Doctor) As Task(Of VerificationOutcome) Implements ICredentialVerifier.VerifyAsync
            Dim hasDegree = d.Credentials.Any(Function(c) c.Kind = CredentialKind.MedicalDegree AndAlso c.AutoCheckPassed.GetValueOrDefault())
            Dim hasBoard = d.Credentials.Any(Function(c) c.Kind = CredentialKind.BoardCertification AndAlso c.AutoCheckPassed.GetValueOrDefault())
            Dim notes As New List(Of String)
            If Not hasDegree Then notes.Add("Medical degree missing or unreadable.")
            If Not hasBoard Then notes.Add("Board certification missing or unreadable.")
            If notes.Count = 0 Then notes.Add("Automated file checks passed. Awaiting reviewer.")
            Return Task.FromResult(New VerificationOutcome With {.Status = VerificationStatus.InProgress, .Notes = String.Join(" ", notes)})
        End Function
    End Class

    ''' <summary>
    ''' Calls an external medical-board registry. The request/response contract below is a
    ''' placeholder: replace it with the real registry's API once one is chosen.
    ''' </summary>
    Public Class RegistryCredentialVerifier
        Implements ICredentialVerifier
        Private ReadOnly _http As HttpClient
        Private ReadOnly _cfg As IConfiguration
        Private ReadOnly _fallback As New ManualReviewVerifier()

        Public Sub New(http As HttpClient, cfg As IConfiguration)
            _http = http
            _cfg = cfg
        End Sub

        Public Async Function VerifyAsync(d As Doctor) As Task(Of VerificationOutcome) Implements ICredentialVerifier.VerifyAsync
            Dim baseUrl = _cfg("CredentialRegistry:BaseUrl")
            If String.IsNullOrWhiteSpace(baseUrl) Then Return Await _fallback.VerifyAsync(d)
            Dim req As New HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd("/"c) & "/verify")
            req.Headers.Add("X-Api-Key", _cfg("CredentialRegistry:ApiKey"))
            req.Content = JsonContent.Create(New With {.name = d.FullName, .licenseNumber = d.LicenseNumber, .specialty = d.PrimarySpecialization.ToString()})
            Try
                Dim res = Await _http.SendAsync(req)
                If Not res.IsSuccessStatusCode Then Return Await _fallback.VerifyAsync(d)
                Dim body = JsonNode.Parse(Await res.Content.ReadAsStringAsync())
                Dim valid = body?("valid")?.GetValue(Of Boolean)()
                If valid.GetValueOrDefault() Then
                    Return New VerificationOutcome With {.Status = VerificationStatus.Verified, .Notes = "Confirmed by registry."}
                End If
                Return New VerificationOutcome With {.Status = VerificationStatus.InProgress, .Notes = "Registry could not confirm. Sent to manual review."}
            Catch ex As HttpRequestException
                ' Registry unreachable: fall through to manual review.
            End Try
            Return Await _fallback.VerifyAsync(d)
        End Function
    End Class

    ' ---------------------------------------------------------------- AI triage

    Public Interface ITriageService
        Function AnalyzeAsync(text As String) As Task(Of TriageResult)
    End Interface

    ''' <summary>
    ''' Uses Claude (Anthropic Messages API, raw HTTP since VB.NET has no official SDK) when
    ''' Ai:Provider = "anthropic" and a key is configured. Falls back to the built-in rules engine
    ''' on any error, refusal, or when no key is set.
    ''' </summary>
    Public Class TriageService
        Implements ITriageService

        Private ReadOnly _http As HttpClient
        Private ReadOnly _cfg As IConfiguration
        Private ReadOnly _log As ILogger(Of TriageService)
        Private ReadOnly _rules As New SymptomTriageEngine()

        Private Const SystemPrompt As String =
            "You are the triage assistant for Doc On The Go, a telehealth app serving remote and underserved regions. " &
            "Read the patient's description, extract the symptoms, judge urgency, and recommend which doctor specializations " &
            "they should see. You do not diagnose. If anything suggests a life-threatening emergency, set urgency to Emergency " &
            "and tell them to seek emergency care immediately. Use only specialization values from the schema. Keep summary " &
            "and advice to one or two plain sentences each, written for a patient."

        Public Sub New(http As HttpClient, cfg As IConfiguration, log As ILogger(Of TriageService))
            _http = http
            _cfg = cfg
            _log = log
        End Sub

        Public Async Function AnalyzeAsync(text As String) As Task(Of TriageResult) Implements ITriageService.AnalyzeAsync
            Dim rules = _rules.Analyze(text)
            Dim key = _cfg("Ai:AnthropicApiKey")
            If Not String.Equals(_cfg("Ai:Provider"), "anthropic", StringComparison.OrdinalIgnoreCase) OrElse String.IsNullOrWhiteSpace(key) Then
                Return rules
            End If
            Try
                Dim ai = Await CallClaudeAsync(text, key)
                If ai Is Nothing Then Return rules
                ' Never let the model lower urgency below what the red-flag rules detected.
                If rules.Urgency > ai.Urgency Then
                    ai.Urgency = rules.Urgency
                    ai.Advice = SymptomTriageEngine.BuildAdvice(rules.Urgency)
                End If
                Return ai
            Catch ex As Exception
                _log.LogWarning(ex, "AI triage failed; using rules engine.")
                Return rules
            End Try
        End Function

        Private Async Function CallClaudeAsync(text As String, apiKey As String) As Task(Of TriageResult)
            Dim model = If(_cfg("Ai:Model"), "claude-opus-5-5")
            Dim specNames = New JsonArray(SpecializationInfo.AllSpecializations().Select(Function(s) CType(JsonValue.Create(s.ToString()), JsonNode)).ToArray())
            Dim urgencyNames = New JsonArray([Enum].GetNames(GetType(Urgency)).Select(Function(s) CType(JsonValue.Create(s), JsonNode)).ToArray())

            Dim schema As New JsonObject From {
                {"type", "object"},
                {"additionalProperties", False},
                {"required", New JsonArray("urgency", "summary", "advice", "symptoms", "specializations")},
                {"properties", New JsonObject From {
                    {"urgency", New JsonObject From {{"type", "string"}, {"enum", urgencyNames}}},
                    {"summary", New JsonObject From {{"type", "string"}}},
                    {"advice", New JsonObject From {{"type", "string"}}},
                    {"symptoms", New JsonObject From {{"type", "array"}, {"items", New JsonObject From {{"type", "string"}}}}},
                    {"specializations", New JsonObject From {
                        {"type", "array"},
                        {"items", New JsonObject From {
                            {"type", "object"},
                            {"additionalProperties", False},
                            {"required", New JsonArray("name", "score")},
                            {"properties", New JsonObject From {
                                {"name", New JsonObject From {{"type", "string"}, {"enum", specNames}}},
                                {"score", New JsonObject From {{"type", "number"}}}
                            }}
                        }}
                    }}
                }}
            }

            Dim body As New JsonObject From {
                {"model", model},
                {"max_tokens", 4000},
                {"system", SystemPrompt},
                {"output_config", New JsonObject From {
                    {"effort", "low"},
                    {"format", New JsonObject From {{"type", "json_schema"}, {"schema", schema}}}
                }},
                {"fallbacks", "default"},
                {"messages", New JsonArray(New JsonObject From {{"role", "user"}, {"content", text}})}
            }

            Dim req As New HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
            req.Headers.Add("x-api-key", apiKey)
            req.Headers.Add("anthropic-version", "2026-10-21")
            req.Headers.Add("anthropic-beta", "server-side-fallback-2026-07-01")
            req.Content = New StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")

            Dim res = Await _http.SendAsync(req)
            Dim raw = Await res.Content.ReadAsStringAsync()
            If Not res.IsSuccessStatusCode Then
                _log.LogWarning("Anthropic API returned {Status}: {Body}", CInt(res.StatusCode), raw)
                Return Nothing
            End If

            Dim msg = JsonNode.Parse(raw)
            Dim stopReason = msg?("stop_reason")?.GetValue(Of String)()
            If stopReason = "refusal" OrElse stopReason = "max_tokens" Then Return Nothing

            Dim jsonText = msg("content").AsArray().
                Where(Function(b) b?("type")?.GetValue(Of String)() = "text").
                Select(Function(b) b("text").GetValue(Of String)()).
                FirstOrDefault()
            If String.IsNullOrWhiteSpace(jsonText) Then Return Nothing

            Dim parsed = JsonNode.Parse(jsonText)
            Dim result As New TriageResult With {
                .Engine = If(msg("model")?.GetValue(Of String)(), model),
                .Summary = parsed("summary").GetValue(Of String)(),
                .Advice = parsed("advice").GetValue(Of String)()
            }
            Dim urg As Urgency
            If [Enum].TryParse(parsed("urgency").GetValue(Of String)(), urg) Then result.Urgency = urg
            For Each s In parsed("symptoms").AsArray()
                result.ExtractedSymptoms.Add(s.GetValue(Of String)())
            Next
            For Each s In parsed("specializations").AsArray()
                Dim sp As Specialization
                If [Enum].TryParse(s("name").GetValue(Of String)(), sp) Then
                    result.Recommended.Add(New SpecializationScore With {
                        .Specialization = sp,
                        .Score = Math.Clamp(s("score").GetValue(Of Double)(), 0, 1),
                        .DisplayName = SpecializationInfo.DisplayName(sp)})
                End If
            Next
            If result.Recommended.Count = 0 Then Return Nothing
            result.Recommended = result.Recommended.OrderByDescending(Function(x) x.Score).Take(3).ToList()
            Return result
        End Function
    End Class

End Namespace
