Option Strict On
Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Linq
Imports DocOnTheGo.Core.Models

Namespace Infrastructure

    ''' <summary>
    ''' Persistence boundary. The in-memory implementation lets the API run with no database;
    ''' production should swap in a PostgreSQL + PostGIS implementation (see docs/Required-Inputs.pdf).
    ''' </summary>
    Public Interface IDataStore
        Function Patients() As IEnumerable(Of Patient)
        Function Doctors() As IEnumerable(Of Doctor)
        Function FindPatient(id As String) As Patient
        Function FindDoctor(id As String) As Doctor
        Function FindPatientByContact(emailOrPhone As String) As Patient
        Function FindDoctorByContact(emailOrPhone As String) As Doctor
        Sub SavePatient(p As Patient)
        Sub SaveDoctor(d As Doctor)
        Function Appointments() As IEnumerable(Of Appointment)
        Sub SaveAppointment(a As Appointment)
        Function Conversations() As IEnumerable(Of Conversation)
        Sub SaveConversation(c As Conversation)
        Function Messages(conversationId As String) As IEnumerable(Of ChatMessage)
        Sub AddMessage(m As ChatMessage)
        Function Prescriptions() As IEnumerable(Of Prescription)
        Sub SavePrescription(p As Prescription)
        Sub AddAudit(entry As AuditEntry)
        Function AuditLog() As IEnumerable(Of AuditEntry)
    End Interface

    Public Class AuditEntry
        Public Property Utc As DateTime = DateTime.UtcNow
        Public Property Actor As String = ""
        Public Property Action As String = ""
        Public Property Subject As String = ""
        Public Property Details As String = ""
    End Class

    Public Class InMemoryDataStore
        Implements IDataStore

        Private ReadOnly _patients As New ConcurrentDictionary(Of String, Patient)
        Private ReadOnly _doctors As New ConcurrentDictionary(Of String, Doctor)
        Private ReadOnly _appointments As New ConcurrentDictionary(Of String, Appointment)
        Private ReadOnly _conversations As New ConcurrentDictionary(Of String, Conversation)
        Private ReadOnly _messages As New ConcurrentDictionary(Of String, ConcurrentQueue(Of ChatMessage))
        Private ReadOnly _prescriptions As New ConcurrentDictionary(Of String, Prescription)
        Private ReadOnly _audit As New ConcurrentQueue(Of AuditEntry)

        Private Shared Function SameContact(a As String, b As String) As Boolean
            If String.IsNullOrWhiteSpace(a) OrElse String.IsNullOrWhiteSpace(b) Then Return False
            If String.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase) Then Return True
            Dim na = Core.Services.Validation.NormalizePhone(a)
            Dim nb = Core.Services.Validation.NormalizePhone(b)
            Return na.Length >= 7 AndAlso na = nb
        End Function

        Public Function Patients() As IEnumerable(Of Patient) Implements IDataStore.Patients
            Return _patients.Values.ToList()
        End Function

        Public Function Doctors() As IEnumerable(Of Doctor) Implements IDataStore.Doctors
            Return _doctors.Values.ToList()
        End Function

        Public Function FindPatient(id As String) As Patient Implements IDataStore.FindPatient
            Dim p As Patient = Nothing
            If id IsNot Nothing Then _patients.TryGetValue(id, p)
            Return p
        End Function

        Public Function FindDoctor(id As String) As Doctor Implements IDataStore.FindDoctor
            Dim d As Doctor = Nothing
            If id IsNot Nothing Then _doctors.TryGetValue(id, d)
            Return d
        End Function

        Public Function FindPatientByContact(emailOrPhone As String) As Patient Implements IDataStore.FindPatientByContact
            Return _patients.Values.FirstOrDefault(Function(p) SameContact(p.Email, emailOrPhone) OrElse SameContact(p.Phone, emailOrPhone))
        End Function

        Public Function FindDoctorByContact(emailOrPhone As String) As Doctor Implements IDataStore.FindDoctorByContact
            Return _doctors.Values.FirstOrDefault(Function(d) SameContact(d.Email, emailOrPhone) OrElse SameContact(d.Phone, emailOrPhone))
        End Function

        Public Sub SavePatient(p As Patient) Implements IDataStore.SavePatient
            _patients(p.Id) = p
        End Sub

        Public Sub SaveDoctor(d As Doctor) Implements IDataStore.SaveDoctor
            _doctors(d.Id) = d
        End Sub

        Public Function Appointments() As IEnumerable(Of Appointment) Implements IDataStore.Appointments
            Return _appointments.Values.ToList()
        End Function

        Public Sub SaveAppointment(a As Appointment) Implements IDataStore.SaveAppointment
            _appointments(a.Id) = a
        End Sub

        Public Function Conversations() As IEnumerable(Of Conversation) Implements IDataStore.Conversations
            Return _conversations.Values.ToList()
        End Function

        Public Sub SaveConversation(c As Conversation) Implements IDataStore.SaveConversation
            _conversations(c.Id) = c
        End Sub

        Public Function Messages(conversationId As String) As IEnumerable(Of ChatMessage) Implements IDataStore.Messages
            Dim q As ConcurrentQueue(Of ChatMessage) = Nothing
            If _messages.TryGetValue(conversationId, q) Then Return q.ToList()
            Return New List(Of ChatMessage)()
        End Function

        Public Sub AddMessage(m As ChatMessage) Implements IDataStore.AddMessage
            _messages.GetOrAdd(m.ConversationId, Function(k) New ConcurrentQueue(Of ChatMessage)()).Enqueue(m)
        End Sub

        Public Function Prescriptions() As IEnumerable(Of Prescription) Implements IDataStore.Prescriptions
            Return _prescriptions.Values.ToList()
        End Function

        Public Sub SavePrescription(p As Prescription) Implements IDataStore.SavePrescription
            _prescriptions(p.Id) = p
        End Sub

        Public Sub AddAudit(entry As AuditEntry) Implements IDataStore.AddAudit
            _audit.Enqueue(entry)
        End Sub

        Public Function AuditLog() As IEnumerable(Of AuditEntry) Implements IDataStore.AuditLog
            Return _audit.ToList()
        End Function
    End Class

End Namespace
