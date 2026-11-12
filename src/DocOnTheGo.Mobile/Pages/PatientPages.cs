using DocOnTheGo.Core.Models;
using DocOnTheGo.Core.Services;
using DocOnTheGo.Mobile.Controls;
using DocOnTheGo.Mobile.Services;
using Microsoft.Maui.Controls.Shapes;

namespace DocOnTheGo.Mobile.Pages;

/// <summary>Bottom tabs: Home, Appointments, Messages, Profile (Functional Guide section 6).</summary>
public static class MainTabs
{
    public static Page Create()
    {
        var tabs = new TabbedPage
        {
            BarBackgroundColor = Colors.White,
            SelectedTabColor = Ui.Teal,
            UnselectedTabColor = Ui.Muted
        };
        Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.TabbedPage.SetToolbarPlacement(
            tabs, Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.ToolbarPlacement.Bottom);

        Page home = Session.IsDoctor ? new DoctorVerificationPage() : new HomePage();
        tabs.Children.Add(Wrap(home, "Home", "🏠"));
        tabs.Children.Add(Wrap(new AppointmentsPage(), "Appointments", "📅"));
        tabs.Children.Add(Wrap(new MessagesPage(), "Messages", "💬"));
        tabs.Children.Add(Wrap(new ProfilePage(), "Profile", "👤"));
        return tabs;
    }

    private static NavigationPage Wrap(Page p, string title, string glyph)
    {
        var nav = new NavigationPage(p) { Title = title, BarBackgroundColor = Ui.TealDark, BarTextColor = Colors.White };
        nav.IconImageSource = new FontImageSource { Glyph = glyph, Size = 20, Color = Ui.Teal };
        return nav;
    }
}

/// <summary>Patient home screen: welcome header, search, AI assistant prompt, featured doctors.</summary>
public class HomePage : ContentPage
{
    private readonly SearchBar _search = new() { Placeholder = "Search for specialists or symptoms", BackgroundColor = Colors.White, TextColor = Ui.Ink, PlaceholderColor = Ui.Muted };
    private readonly VerticalStackLayout _featured = new() { Spacing = 10 };
    private readonly Label _status = new() { TextColor = Ui.Muted, FontSize = 12 };

    public HomePage()
    {
        NavigationPage.SetHasNavigationBar(this, false);
        BackgroundColor = Ui.Page;
        var firstName = Session.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

        var header = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(new Label { Text = $"Welcome, {firstName}!", TextColor = Colors.White, FontSize = 24, FontFamily = "OpenSansSemibold", VerticalOptions = LayoutOptions.Center });
        var who = new HorizontalStackLayout
        {
            Spacing = 6,
            Children =
            {
                Ui.Avatar(IdGenerator.InitialsOf(Session.DisplayName), 36),
                new VerticalStackLayout { Children = { new Label { Text = "Patient", TextColor = Colors.White, FontSize = 12, FontFamily = "OpenSansSemibold" }, new Label { Text = Session.PatientTag, TextColor = Color.FromArgb("#D9F1EE"), FontSize = 10 } } }
            }
        };
        header.Add(who, 1);

        _search.SearchButtonPressed += async (_, _) => await SearchAsync();

        var assistant = Ui.Card(new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 12,
            Children =
            {
                new Label { Text = "🩺", FontSize = 30, VerticalOptions = LayoutOptions.Center },
            }
        });
        var assistantText = new VerticalStackLayout
        {
            Children =
            {
                Ui.Text("AI Health Assistant", 16, Ui.Teal, true),
                Ui.Text("Describe how you feel and we'll suggest the right kind of doctor.", 13, Ui.Muted)
            }
        };
        ((Grid)assistant.Content!).Add(assistantText, 1);
        assistant.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await Navigation.PushAsync(new AssistantPage())) });

        var top = Ui.GradientHeader(new VerticalStackLayout { Spacing = 12, Children = { header, new Border { Content = _search, StrokeShape = new RoundRectangle { CornerRadius = 10 }, StrokeThickness = 0, BackgroundColor = Colors.White } } });

        var refresh = new RefreshView
        {
            Command = new Command(async () => { await LoadAsync(); }),
            RefreshColor = Ui.Teal,
            Content = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    Children =
                    {
                        top,
                        new VerticalStackLayout
                        {
                            Padding = 16, Spacing = 14,
                            Children = { assistant, Ui.Text("Featured Doctors", 18, Ui.Ink, true), _status, _featured }
                        }
                    }
                }
            }
        };
        refresh.Refreshing += (_, _) => { };
        Content = refresh;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (Content is RefreshView rv) rv.IsRefreshing = false;
        _status.Text = "Loading doctors…";
        await Ui.RunAsync(this, async () =>
        {
            var loc = await Session.TryGetLocationAsync();
            var docs = await Session.Backend.SearchDoctorsAsync(null, loc);
            _featured.Children.Clear();
            _featured.Children.Add(Ui.DoctorGrid(docs.Take(6), d => BookingPage.OpenAsync(Navigation, d)));
            _status.Text = docs.Count == 0 ? "No verified doctors yet." : (loc == null ? "Turn on location to see doctors closest to you." : "Closest verified doctors first.");
        });
    }

    private async Task SearchAsync()
    {
        var q = _search.Text?.Trim();
        if (string.IsNullOrEmpty(q)) return;
        await Ui.RunAsync(this, async () =>
        {
            var docs = await Session.Backend.SearchDoctorsAsync(q, Session.LastLocation);
            await Navigation.PushAsync(new DoctorListPage($"Results for \"{q}\"", docs));
        });
    }
}

/// <summary>AI Health Assistant chat (Functional Guide section 4).</summary>
public class AssistantPage : ContentPage
{
    private readonly VerticalStackLayout _thread = new() { Spacing = 10, Padding = new Thickness(14) };
    private readonly ScrollView _scroll;
    private readonly Entry _input = new() { Placeholder = "e.g. I have a persistent headache and mild fever", TextColor = Ui.Ink, PlaceholderColor = Ui.Muted, ReturnType = ReturnType.Send };
    private readonly ActivityIndicator _busy = new() { Color = Ui.Teal, HeightRequest = 24 };
    private List<DoctorMatch> _lastDoctors = new();

    public AssistantPage()
    {
        Title = "AI Health Assistant";
        BackgroundColor = Ui.Mint;
        var first = Session.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";
        AddBubble($"Hello {first}!\nPlease describe your symptoms.", fromUser: false);

        _scroll = new ScrollView { Content = _thread };
        _input.Completed += async (_, _) => await SendAsync();
        var send = new Button { Text = "Send", BackgroundColor = Ui.Teal, TextColor = Colors.White, CornerRadius = 10, HeightRequest = 44 };
        send.Clicked += async (_, _) => await SendAsync();

        var bar = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 8, Padding = new Thickness(10), BackgroundColor = Colors.White
        };
        bar.Add(Ui.Field(_input));
        bar.Add(send, 1);

        var disclaimer = new Label
        {
            Text = "Guidance only, not a diagnosis. In an emergency call your local emergency number.",
            FontSize = 11, TextColor = Ui.Muted, HorizontalTextAlignment = TextAlignment.Center, Padding = new Thickness(10, 4), BackgroundColor = Colors.White
        };

        var grid = new Grid { RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) } };
        grid.Add(_scroll, 0, 0);
        grid.Add(_busy, 0, 1);
        grid.Add(disclaimer, 0, 2);
        grid.Add(bar, 0, 3);
        Content = grid;
    }

    private void AddBubble(string text, bool fromUser, Color? accent = null)
    {
        var b = new Border
        {
            Content = new Label { Text = text, TextColor = fromUser ? Ui.TealDark : Ui.Ink, FontSize = 14 },
            BackgroundColor = fromUser ? Color.FromArgb("#CDEBEA") : Colors.White,
            Padding = new Thickness(12, 10),
            StrokeThickness = accent != null ? 2 : 0,
            Stroke = accent ?? Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            HorizontalOptions = fromUser ? LayoutOptions.End : LayoutOptions.Start,
            MaximumWidthRequest = 320
        };
        _thread.Children.Add(b);
    }

    private async Task SendAsync()
    {
        var text = _input.Text?.Trim();
        if (string.IsNullOrEmpty(text) || _busy.IsRunning) return;
        _input.Text = "";
        AddBubble(text, fromUser: true);
        _busy.IsRunning = true;
        await Ui.RunAsync(this, async () =>
        {
            var r = await Session.Backend.TriageAsync(text, Session.LastLocation ?? await Session.TryGetLocationAsync());
            var t = r.Triage;
            AddBubble(t.Summary, fromUser: false);
            AddBubble($"Urgency: {Ui.UrgencyText(t.Urgency)}\n{t.Advice}", fromUser: false, accent: Ui.UrgencyColor(t.Urgency));
            if (t.Urgency == Urgency.Emergency)
            {
                var call = Ui.Primary("Call emergency services", async (_, _) =>
                {
                    try { PhoneDialer.Default.Open("112"); } catch { await DisplayAlert("Emergency", "Call your local emergency number now.", "OK"); }
                });
                call.BackgroundColor = Ui.Danger;
                _thread.Children.Add(call);
            }
            _lastDoctors = r.Doctors;
            if (r.Doctors.Count > 0)
            {
                _thread.Children.Add(Ui.Card(new VerticalStackLayout
                {
                    Spacing = 10,
                    Children =
                    {
                        Ui.Text("Recommended doctors", 15, Ui.Teal, true),
                        Ui.DoctorGrid(r.Doctors.Take(2), d => BookingPage.OpenAsync(Navigation, d, text)),
                        Ui.Primary("View Recommended Doctors", async (_, _) =>
                            await Navigation.PushAsync(new DoctorListPage("Recommended doctors", _lastDoctors, text)))
                    }
                }, 10));
            }
            else
            {
                AddBubble("No verified doctors match yet in your area. Try again later or search all doctors from Home.", fromUser: false);
            }
            if (t.Engine != "rules") _thread.Children.Add(new Label { Text = "Answered by AI model " + t.Engine, FontSize = 10, TextColor = Ui.Muted });
        });
        _busy.IsRunning = false;
        await Task.Delay(50);
        await _scroll.ScrollToAsync(0, _thread.Height, true);
    }
}

public class DoctorListPage : ContentPage
{
    public DoctorListPage(string title, List<DoctorMatch> doctors, string? reason = null)
    {
        Title = title;
        BackgroundColor = Ui.Page;
        View body = doctors.Count == 0
            ? Ui.Text("No verified doctors found. Try a different search.", 14, Ui.Muted)
            : Ui.DoctorGrid(doctors, d => BookingPage.OpenAsync(Navigation, d, reason));
        Content = new ScrollView { Content = new VerticalStackLayout { Padding = 16, Children = { body } } };
    }
}

/// <summary>Doctor profile + Book Appointment (online consultation or in-person visit).</summary>
public class BookingPage : ContentPage
{
    private readonly Doctor _doctor;
    private readonly Picker _mode = new() { Title = "Consultation type", TextColor = Ui.Ink, HeightRequest = 46 };
    private readonly DatePicker _date = new() { MinimumDate = DateTime.Today, Date = DateTime.Today.AddDays(1), TextColor = Ui.Ink, HeightRequest = 46, Format = "ddd dd MMM yyyy" };
    private readonly TimePicker _time = new() { Time = new TimeSpan(10, 0, 0), TextColor = Ui.Ink, HeightRequest = 46 };
    private readonly Editor _reason = new() { Placeholder = "Reason for visit", HeightRequest = 90, TextColor = Ui.Ink };

    public static Task OpenAsync(INavigation nav, Doctor d, string? reason = null) => nav.PushAsync(new BookingPage(d, reason));

    public BookingPage(Doctor d, string? reason)
    {
        _doctor = d;
        Title = d.FullName;
        BackgroundColor = Ui.Page;
        _reason.Text = reason ?? "";
        var modes = new List<string>();
        if (d.OffersOnline) modes.Add("Online consultation");
        modes.Add(d.IsMobileUnit ? "In-person visit (mobile unit comes to you)" : "In-person visit");
        _mode.ItemsSource = modes;
        _mode.SelectedIndex = 0;

        var tags = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var t in d.Tags) { var p = Ui.TagPill(t); p.Margin = new Thickness(0, 0, 6, 6); tags.Add(p); }

        var profile = Ui.Card(new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                new HorizontalStackLayout { Spacing = 12, Children = {
                    Ui.Avatar(d.AvatarInitials, 64),
                    new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Children = {
                        Ui.Text(d.FullName + (d.IsVerified ? "  ✔" : ""), 17, Ui.Ink, true),
                        Ui.Text(SpecializationInfo.DisplayName(d.PrimarySpecialization), 13, Ui.Muted),
                        Ui.Stars(d.Rating, d.RatingCount) } } } },
                tags,
                Ui.Text(d.IsVerified ? "Verified Professional · licence " + d.LicenseNumber : "Verification in progress", 12, Ui.Muted)
            }
        });

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 16, Spacing = 6,
                Children =
                {
                    profile,
                    Ui.FieldLabel("Consultation", false), Ui.Field(_mode),
                    Ui.FieldLabel("Date", false), Ui.Field(_date),
                    Ui.FieldLabel("Time", false), Ui.Field(_time),
                    Ui.FieldLabel("Reason", false), Ui.Field(_reason),
                    new BoxView { HeightRequest = 8, Color = Colors.Transparent },
                    Ui.Primary("Book Appointment", async (_, _) => await BookAsync()),
                    Ui.Secondary("Message " + d.FullName, async (_, _) => await MessageAsync())
                }
            }
        };
    }

    private async Task BookAsync()
    {
        if (Session.IsDoctor) { await DisplayAlert("Booking", "Log in as a patient to book appointments.", "OK"); return; }
        await Ui.RunAsync(this, async () =>
        {
            var local = _date.Date.Date + _time.Time;
            var mode = _doctor.OffersOnline && _mode.SelectedIndex == 0 ? ConsultationMode.Online : ConsultationMode.InPerson;
            var a = await Session.Backend.BookAppointmentAsync(_doctor.Id, local.ToUniversalTime(), mode, _reason.Text ?? "");
            await DisplayAlert("Appointment requested", $"{a.DoctorName}\n{local:dddd d MMM, HH:mm}\n{(mode == ConsultationMode.Online ? "Online consultation" : "In-person visit")}", "OK");
            await Navigation.PopToRootAsync();
        });
    }

    private async Task MessageAsync()
    {
        if (Session.IsDoctor) return;
        await Ui.RunAsync(this, async () =>
        {
            var c = await Session.Backend.StartConversationAsync(_doctor.Id);
            await Navigation.PushAsync(new ChatPage(c));
        });
    }
}
