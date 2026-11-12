Option Strict On
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Net.Http
Imports System.Net.Http.Json
Imports System.Text.Json
Imports System.Text.Json.Serialization
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports DocOnTheGo.Core.Models

''' <summary>
''' Doc On The Go credential review console (Architecture spec: "Admin &amp; Validator Tier",
''' Visual Basic .NET WinForms). Reviewers list doctors awaiting verification, open their uploaded
''' degree and board certificate, and approve or reject. Every action lands in the API audit log.
''' </summary>
Public Module Program
    <STAThread>
    Public Sub Main()
        Application.SetHighDpiMode(HighDpiMode.SystemAware)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Application.Run(New ReviewConsole())
    End Sub
End Module

Public Class ReviewConsole
    Inherits Form

    Private ReadOnly _json As New JsonSerializerOptions(JsonSerializerDefaults.Web)
    Private _http As HttpClient

    Private ReadOnly txtUrl As New TextBox With {.Text = "http://localhost:5080", .Width = 260}
    Private ReadOnly txtKey As New TextBox With {.UseSystemPasswordChar = True, .Width = 200}
    Private ReadOnly txtReviewer As New TextBox With {.Text = Environment.UserName, .Width = 140}
    Private ReadOnly cboStatus As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 140}
    Private ReadOnly btnLoad As New Button With {.Text = "Load", .AutoSize = True}
    Private ReadOnly grid As New DataGridView With {
        .Dock = DockStyle.Fill, .ReadOnly = True, .AllowUserToAddRows = False,
        .SelectionMode = DataGridViewSelectionMode.FullRowSelect, .MultiSelect = False,
        .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill}
    Private ReadOnly lstCreds As New ListBox With {.Dock = DockStyle.Fill}
    Private ReadOnly txtNotes As New TextBox With {.Multiline = True, .Dock = DockStyle.Fill, .ScrollBars = ScrollBars.Vertical}
    Private ReadOnly lblDetail As New Label With {.Dock = DockStyle.Top, .Height = 90, .AutoSize = False}
    Private ReadOnly btnOpen As New Button With {.Text = "Open document", .AutoSize = True}
    Private ReadOnly btnApprove As New Button With {.Text = "Approve (Verified Professional)", .AutoSize = True, .BackColor = Color.FromArgb(31, 122, 110), .ForeColor = Color.White}
    Private ReadOnly btnReject As New Button With {.Text = "Reject", .AutoSize = True}
    Private ReadOnly btnAudit As New Button With {.Text = "Audit log", .AutoSize = True}
    Private ReadOnly status As New StatusStrip()
    Private ReadOnly statusLabel As New ToolStripStatusLabel("Ready")

    Private _doctors As New List(Of Doctor)

    Public Sub New()
        Text = "Doc On The Go - Credential Review Console"
        Width = 1200
        Height = 760
        Font = New Font("Segoe UI", 9.5F)

        _json.Converters.Add(New JsonStringEnumConverter())
        cboStatus.Items.AddRange(New Object() {"InProgress", "Verified", "Rejected", "NotSubmitted", "All"})
        cboStatus.SelectedIndex = 0

        Dim top As New FlowLayoutPanel With {.Dock = DockStyle.Top, .Height = 40, .Padding = New Padding(6)}
        top.Controls.AddRange(New Control() {
            New Label With {.Text = "API URL", .AutoSize = True, .Padding = New Padding(0, 6, 0, 0)}, txtUrl,
            New Label With {.Text = "Admin key", .AutoSize = True, .Padding = New Padding(0, 6, 0, 0)}, txtKey,
            New Label With {.Text = "Reviewer", .AutoSize = True, .Padding = New Padding(0, 6, 0, 0)}, txtReviewer,
            cboStatus, btnLoad, btnAudit})

        Dim right As New TableLayoutPanel With {.Dock = DockStyle.Fill, .RowCount = 5, .ColumnCount = 1, .Padding = New Padding(6)}
        right.RowStyles.Add(New RowStyle(SizeType.Absolute, 95))
        right.RowStyles.Add(New RowStyle(SizeType.Percent, 50))
        right.RowStyles.Add(New RowStyle(SizeType.Absolute, 22))
        right.RowStyles.Add(New RowStyle(SizeType.Percent, 50))
        right.RowStyles.Add(New RowStyle(SizeType.Absolute, 44))
        right.Controls.Add(lblDetail, 0, 0)
        right.Controls.Add(lstCreds, 0, 1)
        right.Controls.Add(New Label With {.Text = "Reviewer notes (sent to the doctor and kept in the audit log):", .Dock = DockStyle.Fill}, 0, 2)
        right.Controls.Add(txtNotes, 0, 3)
        Dim actions As New FlowLayoutPanel With {.Dock = DockStyle.Fill}
        actions.Controls.AddRange(New Control() {btnOpen, btnApprove, btnReject})
        right.Controls.Add(actions, 0, 4)

        Dim split As New SplitContainer With {.Dock = DockStyle.Fill, .SplitterDistance = 640}
        split.Panel1.Controls.Add(grid)
        split.Panel2.Controls.Add(right)

        status.Items.Add(statusLabel)
        Controls.Add(split)
        Controls.Add(top)
        Controls.Add(status)

        AddHandler btnLoad.Click, Async Sub() Await LoadDoctorsAsync()
        AddHandler grid.SelectionChanged, Sub() ShowSelected()
        AddHandler btnOpen.Click, Async Sub() Await OpenDocumentAsync()
        AddHandler btnApprove.Click, Async Sub() Await ReviewAsync(True)
        AddHandler btnReject.Click, Async Sub() Await ReviewAsync(False)
        AddHandler btnAudit.Click, Async Sub() Await ShowAuditAsync()
    End Sub

    Private Function Client() As HttpClient
        If _http Is Nothing OrElse _http.BaseAddress Is Nothing OrElse _http.BaseAddress.ToString().TrimEnd("/"c) <> txtUrl.Text.TrimEnd("/"c) Then
            _http = New HttpClient With {.BaseAddress = New Uri(txtUrl.Text.TrimEnd("/"c) & "/"), .Timeout = TimeSpan.FromSeconds(30)}
        End If
        _http.DefaultRequestHeaders.Remove("X-Admin-Key")
        _http.DefaultRequestHeaders.Add("X-Admin-Key", txtKey.Text)
        Return _http
    End Function

    Private Function SelectedDoctor() As Doctor
        If grid.CurrentRow Is Nothing Then Return Nothing
        Dim id = CStr(grid.CurrentRow.Cells("Id").Value)
        Return _doctors.FirstOrDefault(Function(d) d.Id = id)
    End Function

    Private Async Function LoadDoctorsAsync() As Task
        Try
            SetStatus("Loading...")
            Dim filter = CStr(cboStatus.SelectedItem)
            Dim url = If(filter = "All", "api/admin/doctors", "api/admin/doctors?status=" & filter)
            _doctors = If(Await Client().GetFromJsonAsync(Of List(Of Doctor))(url, _json), New List(Of Doctor)())
            grid.DataSource = _doctors.Select(Function(d) New With {
                d.Id, .Name = d.FullName, .License = d.LicenseNumber,
                .Specialization = Core.Services.SpecializationInfo.DisplayName(d.PrimarySpecialization),
                .Status = d.Status.ToString(), .Documents = d.Credentials.Count, .Registered = d.CreatedUtc.ToLocalTime().ToString("g")}).ToList()
            If grid.Columns.Contains("Id") Then grid.Columns("Id").Visible = False
            SetStatus($"{_doctors.Count} doctor(s) loaded.")
        Catch ex As Exception
            ShowError(ex)
        End Try
    End Function

    Private Sub ShowSelected()
        Dim d = SelectedDoctor()
        lstCreds.Items.Clear()
        If d Is Nothing Then
            lblDetail.Text = ""
            Return
        End If
        lblDetail.Text = $"{d.FullName}   |   License {d.LicenseNumber}{Environment.NewLine}" &
                         $"{Core.Services.SpecializationInfo.DisplayName(d.PrimarySpecialization)}{If(d.IsMobileUnit, "   |   Mobile unit, radius " & d.ServiceRadiusKm & " km", "")}{Environment.NewLine}" &
                         $"{d.Email}   {d.Phone}{Environment.NewLine}Status: {d.Status}. {d.StatusNotes}"
        For Each c In d.Credentials
            Dim check = If(c.AutoCheckPassed.GetValueOrDefault(), "auto-check OK", "auto-check FAILED")
            lstCreds.Items.Add($"{c.Kind}: {c.FileName} ({c.SizeBytes \ 1024} KB, {check})")
        Next
    End Sub

    Private Async Function OpenDocumentAsync() As Task
        Dim d = SelectedDoctor()
        If d Is Nothing OrElse lstCreds.SelectedIndex < 0 Then
            MessageBox.Show(Me, "Select a doctor and a document first.")
            Return
        End If
        Try
            Dim c = d.Credentials(lstCreds.SelectedIndex)
            Dim bytes = Await Client().GetByteArrayAsync($"api/admin/doctors/{d.Id}/credentials/{c.Id}")
            Dim path = IO.Path.Combine(IO.Path.GetTempPath(), "dotg-" & c.Id & IO.Path.GetExtension(c.FileName))
            Await File.WriteAllBytesAsync(path, bytes)
            Process.Start(New ProcessStartInfo(path) With {.UseShellExecute = True})
            SetStatus("Opened " & c.FileName & ". Delete the temp copy when done: " & path)
        Catch ex As Exception
            ShowError(ex)
        End Try
    End Function

    Private Async Function ReviewAsync(approve As Boolean) As Task
        Dim d = SelectedDoctor()
        If d Is Nothing Then Return
        Dim verb = If(approve, "approve", "reject")
        If MessageBox.Show(Me, $"{verb.Substring(0, 1).ToUpper()}{verb.Substring(1)} {d.FullName} ({d.LicenseNumber})?", "Confirm", MessageBoxButtons.YesNo) <> DialogResult.Yes Then Return
        Try
            Dim res = Await Client().PostAsJsonAsync($"api/admin/doctors/{d.Id}/review",
                                                     New With {.approve = approve, .notes = txtNotes.Text, .reviewer = txtReviewer.Text}, _json)
            res.EnsureSuccessStatusCode()
            txtNotes.Clear()
            SetStatus($"{d.FullName} {verb}d.")
            Await LoadDoctorsAsync()
        Catch ex As Exception
            ShowError(ex)
        End Try
    End Function

    Private Async Function ShowAuditAsync() As Task
        Try
            Dim text = Await Client().GetStringAsync("api/admin/audit")
            Dim entries = JsonSerializer.Deserialize(Of List(Of Dictionary(Of String, JsonElement)))(text, _json)
            Dim lines = entries.Select(Function(e) $"{e("utc")}  {e("actor")}  {e("action")}  {e("subject")}  {e("details")}")
            Dim f As New Form With {.Text = "Audit log", .Width = 1000, .Height = 600}
            f.Controls.Add(New TextBox With {.Multiline = True, .ReadOnly = True, .Dock = DockStyle.Fill, .ScrollBars = ScrollBars.Both,
                                            .Font = New Font("Consolas", 9), .Text = String.Join(Environment.NewLine, lines)})
            f.Show(Me)
        Catch ex As Exception
            ShowError(ex)
        End Try
    End Function

    Private Sub SetStatus(msg As String)
        statusLabel.Text = msg
    End Sub

    Private Sub ShowError(ex As Exception)
        SetStatus("Error: " & ex.Message)
        MessageBox.Show(Me, ex.Message, "Doc On The Go", MessageBoxButtons.OK, MessageBoxIcon.Warning)
    End Sub
End Class
