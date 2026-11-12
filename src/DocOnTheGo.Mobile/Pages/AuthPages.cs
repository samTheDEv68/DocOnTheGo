using DocOnTheGo.Core.Models;
using DocOnTheGo.Core.Services;
using DocOnTheGo.Mobile.Controls;
using DocOnTheGo.Mobile.Services;

namespace DocOnTheGo.Mobile.Pages;

/// <summary>"Create Account - Doc On The Go" (patient registration mockup).</summary>
public class RegisterPage : ContentPage
{
    private readonly Entry _name = Ui.Entry("Full Name");
    private readonly Entry _email = Ui.Entry("Email", Keyboard.Email);
    private readonly Entry _phone = Ui.Entry("Phone", Keyboard.Telephone);
    private readonly DatePicker _dob = new() { MaximumDate = DateTime.Today, Date = DateTime.Today.AddYears(-25), TextColor = Ui.Ink, HeightRequest = 46, Format = "dd MMM yyyy" };
    private readonly Picker _gender = new() { Title = "Gender", TextColor = Ui.Ink, HeightRequest = 46, ItemsSource = new[] { "Female", "Male", "Other", "Prefer not to say" } };
    private readonly ActivityIndicator _busy = new() { Color = Colors.White };

    public RegisterPage()
    {
        NavigationPage.SetHasNavigationBar(this, false);
        Title = "Create Account";
        Background = Ui.HeaderGradient;

        var login = new Label
        {
            FormattedText = new FormattedString
            {
                Spans =
                {
                    new Span { Text = "Already have an account? ", TextColor = Colors.White },
                    new Span { Text = "Log in", TextColor = Colors.White, FontFamily = "OpenSansSemibold", TextDecorations = TextDecorations.Underline }
                }
            },
            HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6)
        };
        login.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await Navigation.PushAsync(new LoginPage())) });

        var doctor = new Label { Text = "Are you a doctor? Register here", TextColor = Colors.White, TextDecorations = TextDecorations.Underline, HorizontalTextAlignment = TextAlignment.Center };
        doctor.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await Navigation.PushAsync(new DoctorRegisterPage())) });

        var badge = new HorizontalStackLayout
        {
            Spacing = 10, Margin = new Thickness(0, 18, 0, 0),
            Children = { Ui.Avatar("👤", 40), new Label { Text = "Patient\nRegistration", TextColor = Colors.White, FontSize = 13, VerticalOptions = LayoutOptions.Center } }
        };

        var settings = new Label { Text = Session.Backend.IsDemo ? "Offline demo mode · Server settings" : "Server: " + Session.ServerUrl, TextColor = Color.FromArgb("#CFE9E6"), FontSize = 11, HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        settings.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await Navigation.PushAsync(new ServerSettingsPage())) });

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(24, 40, 24, 24),
                Spacing = 2,
                Children =
                {
                    Ui.Title("Create Account\n- Doc On The Go", 26),
                    new BoxView { HeightRequest = 18, Color = Colors.Transparent },
                    Ui.FieldLabel("Full Name"), Ui.Field(_name),
                    Ui.FieldLabel("Email"), Ui.Field(_email),
                    Ui.FieldLabel("Phone"), Ui.Field(_phone),
                    Ui.FieldLabel("Date of Birth"), Ui.Field(_dob),
                    Ui.FieldLabel("Gender"), Ui.Field(_gender),
                    new BoxView { HeightRequest = 16, Color = Colors.Transparent },
                    Ui.Primary("Register Now", async (_, _) => await RegisterAsync(), onDark: true),
                    _busy,
                    login,
                    doctor,
                    badge,
                    settings
                }
            }
        };
    }

    private async Task RegisterAsync()
    {
        _busy.IsRunning = true;
        await Ui.RunAsync(this, async () =>
        {
            var loc = await Session.TryGetLocationAsync();
            var gender = _gender.SelectedIndex switch { 0 => Gender.Female, 1 => Gender.Male, 2 => Gender.Other, _ => Gender.Unspecified };
            var auth = await Session.Backend.RegisterPatientAsync(new RegisterPatient
            {
                FullName = _name.Text ?? "", Email = _email.Text ?? "", Phone = _phone.Text ?? "",
                DateOfBirth = _dob.Date, Gender = gender, Latitude = loc?.Latitude, Longitude = loc?.Longitude
            });
            await DisplayAlert("Welcome to Doc On The Go", $"Your patient ID is {auth.PatientTag}. It keeps your medical history linked to you.", "Continue");
            await App.SignInAndGoHomeAsync(auth);
        });
        _busy.IsRunning = false;
    }
}

/// <summary>Passwordless log in with a one-time code sent by SMS.</summary>
public class LoginPage : ContentPage
{
    private readonly Entry _contact = Ui.Entry("Email or phone number", Keyboard.Email);
    private readonly Entry _code = Ui.Entry("6-digit code", Keyboard.Numeric);
    private readonly View _codeField;
    private readonly Button _verify;
    private readonly Label _hint = new() { TextColor = Colors.White, FontSize = 13, HorizontalTextAlignment = TextAlignment.Center };

    public LoginPage()
    {
        Title = "Log in";
        Background = Ui.HeaderGradient;
        _code.MaxLength = 6;
        _codeField = Ui.Field(_code);
        _codeField.IsVisible = false;
        _verify = Ui.Primary("Log in", async (_, _) => await VerifyAsync(), onDark: true);
        _verify.IsVisible = false;

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(24, 30),
                Spacing = 8,
                Children =
                {
                    Ui.Title("Welcome back", 26),
                    new Label { Text = "We'll text you a code. No password needed.", TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center },
                    Ui.FieldLabel("Email or phone"), Ui.Field(_contact),
                    Ui.Primary("Send code", async (_, _) => await SendAsync(), onDark: true),
                    _hint,
                    _codeField,
                    _verify
                }
            }
        };
    }

    private async Task SendAsync()
    {
        await Ui.RunAsync(this, async () =>
        {
            var r = await Session.Backend.RequestOtpAsync(_contact.Text ?? "");
            _codeField.IsVisible = _verify.IsVisible = true;
            _hint.Text = r.DevOtp != null
                ? $"Test mode: your code is {r.DevOtp}"
                : "Code sent. It expires in 5 minutes.";
            _code.Focus();
        });
    }

    private async Task VerifyAsync()
    {
        await Ui.RunAsync(this, async () =>
        {
            var auth = await Session.Backend.VerifyOtpAsync(_contact.Text ?? "", _code.Text ?? "");
            await App.SignInAndGoHomeAsync(auth);
        });
    }
}

/// <summary>Doctor profile setup: name, licence number, specialization (Functional Guide section 2).</summary>
public class DoctorRegisterPage : ContentPage
{
    private readonly Entry _name = Ui.Entry("Name");
    private readonly Entry _license = Ui.Entry("Medical License Number");
    private readonly Entry _email = Ui.Entry("Email", Keyboard.Email);
    private readonly Entry _phone = Ui.Entry("Phone", Keyboard.Telephone);
    private readonly Picker _spec = new() { Title = "Specialization", TextColor = Ui.Ink, HeightRequest = 46 };
    private readonly Switch _mobile = new() { OnColor = Ui.TealLight };
    private readonly Entry _radius = Ui.Entry("Service radius (km)", Keyboard.Numeric);
    private readonly IReadOnlyList<Specialization> _specs = SpecializationInfo.AllSpecializations();

    public DoctorRegisterPage()
    {
        Title = "Doctor Registration";
        Background = Ui.HeaderGradient;
        _spec.ItemsSource = _specs.Select(SpecializationInfo.DisplayName).ToList();
        _spec.SelectedIndex = 0;
        _radius.Text = "50";

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(24, 20),
                Spacing = 2,
                Children =
                {
                    Ui.Title("Doctor Registration\n& Certification", 24),
                    Ui.FieldLabel("Name"), Ui.Field(_name),
                    Ui.FieldLabel("Medical License Number"), Ui.Field(_license),
                    Ui.FieldLabel("Email"), Ui.Field(_email),
                    Ui.FieldLabel("Phone"), Ui.Field(_phone),
                    Ui.FieldLabel("Specialization"), Ui.Field(_spec),
                    new HorizontalStackLayout { Spacing = 10, Margin = new Thickness(0, 10, 0, 0), Children = { _mobile, new Label { Text = "I run a mobile / travelling medical unit", TextColor = Colors.White, VerticalOptions = LayoutOptions.Center } } },
                    Ui.FieldLabel("How far will you travel? (km)"), Ui.Field(_radius),
                    new BoxView { HeightRequest = 14, Color = Colors.Transparent },
                    Ui.Primary("Continue to credential upload", async (_, _) => await RegisterAsync(), onDark: true),
                    new Label { Text = "Next you'll upload your medical degree and board certification (JPEG, PNG or PDF).", TextColor = Colors.White, FontSize = 12, HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(0, 8) }
                }
            }
        };
    }

    private async Task RegisterAsync()
    {
        await Ui.RunAsync(this, async () =>
        {
            var loc = await Session.TryGetLocationAsync();
            double.TryParse(_radius.Text, out var radius);
            var auth = await Session.Backend.RegisterDoctorAsync(new RegisterDoctor
            {
                FullName = _name.Text ?? "", LicenseNumber = _license.Text ?? "", Email = _email.Text ?? "", Phone = _phone.Text ?? "",
                PrimarySpecialization = _specs[Math.Max(0, _spec.SelectedIndex)], IsMobileUnit = _mobile.IsToggled,
                ServiceRadiusKm = radius > 0 ? radius : 50, Latitude = loc?.Latitude, Longitude = loc?.Longitude
            });
            await App.SignInAndGoHomeAsync(auth);
        });
    }
}

/// <summary>Choose between offline demo mode and a live Doc On The Go API server.</summary>
public class ServerSettingsPage : ContentPage
{
    public ServerSettingsPage()
    {
        Title = "Server settings";
        BackgroundColor = Ui.Page;
        var url = Ui.Entry("https://api.example.com", Keyboard.Url);
        url.Text = Session.ServerUrl;
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 20,
                Spacing = 10,
                Children =
                {
                    Ui.Text("API server address", 16, Ui.Ink, true),
                    Ui.Text("Leave this empty to use offline demo mode. Demo data stays on this phone and uses sample doctors.", 13, Ui.Muted),
                    Ui.Field(url),
                    Ui.Text("For the Android emulator talking to an API on your computer, use http://10.0.2.2:5080", 12, Ui.Muted),
                    Ui.Primary("Save", async (_, _) =>
                    {
                        var value = (url.Text ?? "").Trim();
                        if (value.Length > 0 && !Uri.TryCreate(value, UriKind.Absolute, out _))
                        {
                            await DisplayAlert("Server settings", "Enter a full address starting with https://", "OK");
                            return;
                        }
                        Session.ServerUrl = value;
                        Session.ResetBackend();
                        await DisplayAlert("Server settings", value.Length == 0 ? "Offline demo mode is on." : "Saved. Please log in again on the new server.", "OK");
                        App.SignOut();
                    })
                }
            }
        };
    }
}
