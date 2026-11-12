using DocOnTheGo.Core.Models;
using DocOnTheGo.Core.Services;
using DocOnTheGo.Mobile.Controls;
using DocOnTheGo.Mobile.Services;
using Microsoft.Maui.Controls.Shapes;

namespace DocOnTheGo.Mobile.Pages;

/// <summary>Upcoming and past consultations with status updates.</summary>
public class AppointmentsPage : ContentPage
{
    private readonly VerticalStackLayout _list = new() { Spacing = 10, Padding = 16 };

    public AppointmentsPage()
    {
        Title = "Appointments";
        BackgroundColor = Ui.Page;
        Content = new RefreshView { Content = new ScrollView { Content = _list }, Command = new Command(async () => await LoadAsync()), RefreshColor = Ui.Teal };
        Appearing += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (Content is RefreshView rv) rv.IsRefreshing = false;
        await Ui.RunAsync(this, async () =>
        {
            var items = await Session.Backend.GetAppointmentsAsync();
            _list.Children.Clear();
            var now = DateTime.UtcNow;
            var upcoming = items.Where(a => a.ScheduledUtc >= now.AddHours(-1) && a.Status is AppointmentStatus.Requested or AppointmentStatus.Confirmed).OrderBy(a => a.ScheduledUtc).ToList();
            var past = items.Except(upcoming).OrderByDescending(a => a.ScheduledUtc).ToList();
            _list.Children.Add(Ui.Text("Upcoming", 17, Ui.Ink, true));
            if (upcoming.Count == 0) _list.Children.Add(Ui.Text(Session.IsDoctor ? "No upcoming consultations." : "No upcoming appointments. Use the AI Health Assistant on Home to find a doctor.", 13, Ui.Muted));
            foreach (var a in upcoming) _list.Children.Add(Row(a, canCancel: true));
            _list.Children.Add(Ui.Text("Past", 17, Ui.Ink, true));
            if (past.Count == 0) _list.Children.Add(Ui.Text("Nothing yet.", 13, Ui.Muted));
            foreach (var a in past) _list.Children.Add(Row(a, canCancel: false));
        });
    }

    private View Row(Appointment a, bool canCancel)
    {
        var who = Session.IsDoctor ? a.PatientName : a.DoctorName;
        var statusColor = a.Status switch
        {
            AppointmentStatus.Confirmed => Ui.Teal,
            AppointmentStatus.Cancelled => Ui.Danger,
            AppointmentStatus.Completed => Ui.Muted,
            _ => Color.FromArgb("#B7950B")
        };
        var stack = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                           Children = { Ui.Text(who, 15, Ui.Ink, true) } },
                Ui.Text($"{a.ScheduledUtc.ToLocalTime():ddd d MMM yyyy, HH:mm} · {(a.Mode == ConsultationMode.Online ? "Online" : "In person")}", 13, Ui.Muted),
            }
        };
        ((Grid)stack.Children[0]).Add(Ui.Pill(a.Status.ToString(), Colors.White, statusColor), 1);
        if (!string.IsNullOrWhiteSpace(a.Reason)) stack.Children.Add(Ui.Text(a.Reason, 13, Ui.Ink));
        if (canCancel)
        {
            var row = new HorizontalStackLayout { Spacing = 8 };
            if (Session.IsDoctor && a.Status == AppointmentStatus.Requested)
                row.Add(SmallButton("Confirm", Ui.Teal, async () => await SetStatusAsync(a, AppointmentStatus.Confirmed)));
            if (Session.IsDoctor)
                row.Add(SmallButton("Mark completed", Ui.TealLight, async () => await SetStatusAsync(a, AppointmentStatus.Completed)));
            row.Add(SmallButton("Cancel", Ui.Danger, async () =>
            {
                if (await DisplayAlert("Cancel appointment", $"Cancel with {who}?", "Yes, cancel", "No"))
                    await SetStatusAsync(a, AppointmentStatus.Cancelled);
            }));
            stack.Children.Add(row);
        }
        return Ui.Card(stack);
    }

    private static Button SmallButton(string text, Color color, Func<Task> onClick)
    {
        var b = new Button { Text = text, FontSize = 12, HeightRequest = 34, CornerRadius = 8, BackgroundColor = color, TextColor = Colors.White, Padding = new Thickness(10, 0) };
        b.Clicked += async (_, _) => await onClick();
        return b;
    }

    private async Task SetStatusAsync(Appointment a, AppointmentStatus status)
    {
        await Ui.RunAsync(this, async () => { await Session.Backend.SetAppointmentStatusAsync(a.Id, status); });
        await LoadAsync();
    }
}

/// <summary>Secure chat channel list between patients and verified doctors.</summary>
public class MessagesPage : ContentPage
{
    private readonly VerticalStackLayout _list = new() { Spacing = 8, Padding = 16 };

    public MessagesPage()
    {
        Title = "Messages";
        BackgroundColor = Ui.Page;
        Content = new RefreshView { Content = new ScrollView { Content = _list }, Command = new Command(async () => await LoadAsync()), RefreshColor = Ui.Teal };
        Appearing += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (Content is RefreshView rv) rv.IsRefreshing = false;
        await Ui.RunAsync(this, async () =>
        {
            var convs = await Session.Backend.GetConversationsAsync();
            _list.Children.Clear();
            if (convs.Count == 0)
                _list.Children.Add(Ui.Text(Session.IsDoctor ? "Patients who message you will appear here." : "No conversations yet. Open a doctor's profile and tap Message.", 13, Ui.Muted));
            foreach (var c in convs)
            {
                var other = Session.IsDoctor ? c.PatientName : c.DoctorName;
                var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 12 };
                row.Add(Ui.Avatar(IdGenerator.InitialsOf(other), 44));
                row.Add(new VerticalStackLayout { Children = { Ui.Text(other, 15, Ui.Ink, true), new Label { Text = string.IsNullOrEmpty(c.LastMessage) ? "Say hello" : c.LastMessage, TextColor = Ui.Muted, FontSize = 13, MaxLines = 1, LineBreakMode = LineBreakMode.TailTruncation } } }, 1);
                var card = Ui.Card(row, 10);
                card.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await Navigation.PushAsync(new ChatPage(c))) });
                _list.Children.Add(card);
            }
        });
    }
}

public class ChatPage : ContentPage
{
    private readonly Conversation _c;
    private readonly VerticalStackLayout _thread = new() { Spacing = 8, Padding = 12 };
    private readonly ScrollView _scroll;
    private readonly Entry _input = new() { Placeholder = "Message", TextColor = Ui.Ink, ReturnType = ReturnType.Send };
    private IDispatcherTimer? _timer;

    public ChatPage(Conversation c)
    {
        _c = c;
        Title = Session.IsDoctor ? c.PatientName : c.DoctorName;
        BackgroundColor = Ui.Mint;
        _scroll = new ScrollView { Content = _thread };
        _input.Completed += async (_, _) => await SendAsync();
        var send = new Button { Text = "Send", BackgroundColor = Ui.Teal, TextColor = Colors.White, CornerRadius = 10 };
        send.Clicked += async (_, _) => await SendAsync();
        var bar = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 8, Padding = 10, BackgroundColor = Colors.White };
        bar.Add(Ui.Field(_input));
        bar.Add(send, 1);
        var grid = new Grid { RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        grid.Add(_scroll, 0, 0);
        grid.Add(bar, 0, 1);
        Content = grid;

        Appearing += async (_, _) =>
        {
            await LoadAsync();
            _timer = Dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(10); // light polling suits low-bandwidth links
            _timer.Tick += async (_, _) => await LoadAsync(quiet: true);
            _timer.Start();
        };
        Disappearing += (_, _) => _timer?.Stop();
    }

    private async Task LoadAsync(bool quiet = false)
    {
        try
        {
            var msgs = await Session.Backend.GetMessagesAsync(_c.Id);
            if (quiet && msgs.Count == _thread.Children.Count) return;
            _thread.Children.Clear();
            foreach (var m in msgs) AddBubble(m);
            await Task.Delay(30);
            await _scroll.ScrollToAsync(0, _thread.Height, false);
        }
        catch (Exception) when (quiet) { }
        catch (BackendException ex) { await DisplayAlert("Messages", ex.Message, "OK"); }
    }

    private void AddBubble(ChatMessage m)
    {
        var mine = m.SenderId == Session.UserId;
        _thread.Children.Add(new Border
        {
            Content = new VerticalStackLayout { Children = {
                new Label { Text = m.Text, TextColor = Ui.Ink, FontSize = 14 },
                new Label { Text = m.SentUtc.ToLocalTime().ToString("HH:mm"), TextColor = Ui.Muted, FontSize = 10, HorizontalOptions = LayoutOptions.End } } },
            BackgroundColor = mine ? Color.FromArgb("#CDEBEA") : Colors.White,
            Padding = new Thickness(12, 8), StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 12 },
            HorizontalOptions = mine ? LayoutOptions.End : LayoutOptions.Start, MaximumWidthRequest = 300
        });
    }

    private async Task SendAsync()
    {
        var text = _input.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        _input.Text = "";
        await Ui.RunAsync(this, async () =>
        {
            var m = await Session.Backend.SendMessageAsync(_c.Id, text);
            AddBubble(m);
            await Task.Delay(30);
            await _scroll.ScrollToAsync(0, _thread.Height, true);
        });
    }
}

/// <summary>User settings, patient ID, medical tags and history, prescriptions, server settings.</summary>
public class ProfilePage : ContentPage
{
    private readonly VerticalStackLayout _body = new() { Spacing = 12, Padding = 16 };

    public ProfilePage()
    {
        Title = "Profile";
        BackgroundColor = Ui.Page;
        Content = new ScrollView { Content = _body };
        Appearing += async (_, _) => await LoadAsync();
    }

    private static View InfoRow(string label, string value) => new Grid
    {
        ColumnDefinitions = { new ColumnDefinition(new GridLength(110)), new ColumnDefinition(GridLength.Star) },
        Children = { Ui.Text(label, 13, Ui.Muted) }
    }.Also(g => g.Add(Ui.Text(value, 14, Ui.Ink), 1));

    private async Task LoadAsync()
    {
        _body.Children.Clear();
        _body.Children.Add(new HorizontalStackLayout
        {
            Spacing = 12,
            Children = { Ui.Avatar(IdGenerator.InitialsOf(Session.DisplayName), 64),
                         new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Children = {
                             Ui.Text(Session.DisplayName, 19, Ui.Ink, true),
                             Ui.Text(Session.IsDoctor ? "Doctor" : Session.PatientTag, 13, Ui.Teal, true) } } }
        });

        await Ui.RunAsync(this, async () =>
        {
            if (Session.IsDoctor)
            {
                var d = await Session.Backend.GetMyDoctorProfileAsync();
                if (d != null)
                {
                    var tags = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
                    foreach (var t in d.Tags) { var p = Ui.TagPill(t); p.Margin = new Thickness(0, 0, 6, 6); tags.Add(p); }
                    _body.Children.Add(Ui.Card(new VerticalStackLayout
                    {
                        Spacing = 6,
                        Children = {
                            InfoRow("Licence", d.LicenseNumber), InfoRow("Specialization", SpecializationInfo.DisplayName(d.PrimarySpecialization)),
                            InfoRow("Status", d.Status == VerificationStatus.Verified ? "Verified Professional" : d.Status.ToString()),
                            InfoRow("Email", d.Email), InfoRow("Phone", d.Phone), tags }
                    }));
                }
            }
            else
            {
                var p = await Session.Backend.GetMyPatientProfileAsync();
                if (p != null)
                {
                    _body.Children.Add(Ui.Card(new VerticalStackLayout
                    {
                        Spacing = 6,
                        Children = {
                            InfoRow("Patient ID", p.PatientTag), InfoRow("Date of birth", p.DateOfBirth.ToString("d MMM yyyy")),
                            InfoRow("Gender", p.Gender.ToString()), InfoRow("Email", p.Email), InfoRow("Phone", p.Phone) }
                    }));
                    var history = new VerticalStackLayout { Spacing = 4, Children = { Ui.Text("Medical history", 15, Ui.Ink, true) } };
                    if (p.MedicalHistory.Count == 0) history.Add(Ui.Text("No records yet. Doctors add notes after consultations.", 13, Ui.Muted));
                    foreach (var h in p.MedicalHistory) history.Add(Ui.Text("• " + h, 13));
                    _body.Children.Add(Ui.Card(history));
                }
            }

            var rx = await Session.Backend.GetPrescriptionsAsync();
            var rxStack = new VerticalStackLayout { Spacing = 6, Children = { Ui.Text("Prescriptions", 15, Ui.Ink, true) } };
            if (rx.Count == 0) rxStack.Add(Ui.Text("No prescriptions yet.", 13, Ui.Muted));
            foreach (var r in rx)
                rxStack.Add(Ui.Text($"{r.Token} · {r.Medication} {r.Dosage}\n{r.Instructions}{(string.IsNullOrEmpty(r.FollowUp) ? "" : "\nFollow-up: " + r.FollowUp)}", 13));
            _body.Children.Add(Ui.Card(rxStack));
        });

        _body.Children.Add(Ui.Card(new VerticalStackLayout
        {
            Spacing = 6,
            Children = {
                Ui.Text("Server", 15, Ui.Ink, true),
                Ui.Text(Session.Backend.IsDemo ? "Offline demo mode (data stays on this phone)" : Session.ServerUrl, 13, Ui.Muted),
                Ui.Secondary("Change server", async (_, _) => await Navigation.PushAsync(new ServerSettingsPage())) }
        }));
        _body.Children.Add(Ui.Secondary("Log out", async (_, _) =>
        {
            if (await DisplayAlert("Log out", "Log out of Doc On The Go?", "Log out", "Cancel")) App.SignOut();
        }));
        _body.Children.Add(new Label { Text = $"Doc On The Go {AppInfo.Current.VersionString}", FontSize = 11, TextColor = Ui.Muted, HorizontalTextAlignment = TextAlignment.Center });
    }
}

internal static class ViewExtensions
{
    public static T Also<T>(this T self, Action<T> action) { action(self); return self; }
}
