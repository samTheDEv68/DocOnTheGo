using DocOnTheGo.Core.Models;
using DocOnTheGo.Core.Services;
using DocOnTheGo.Mobile.Controls;
using DocOnTheGo.Mobile.Services;
using Microsoft.Maui.Controls.Shapes;

namespace DocOnTheGo.Mobile.Pages;

/// <summary>
/// Doctor profile &amp; credential upload (mockup "Doctor Profile"): upload degree and board
/// certification, see auto-checks, submit for verification, and see assigned tags once verified.
/// </summary>
public class DoctorVerificationPage : ContentPage
{
    private readonly VerticalStackLayout _body = new() { Spacing = 12, Padding = 16 };
    private Doctor? _doctor;

    public DoctorVerificationPage()
    {
        Title = "Doctor Profile";
        BackgroundColor = Ui.Page;
        Content = new RefreshView { Content = new ScrollView { Content = _body }, Command = new Command(async () => await LoadAsync()), RefreshColor = Ui.Teal };
        Appearing += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (Content is RefreshView rv) rv.IsRefreshing = false;
        await Ui.RunAsync(this, async () =>
        {
            _doctor = await Session.Backend.GetMyDoctorProfileAsync();
            Render();
        });
    }

    private void Render()
    {
        _body.Children.Clear();
        var d = _doctor;
        if (d == null) { _body.Children.Add(Ui.Text("Profile not found. Please log in again.", 14, Ui.Muted)); return; }

        var header = Ui.GradientHeader(new VerticalStackLayout
        {
            Spacing = 4,
            Children = {
                new Label { Text = d.FullName, TextColor = Colors.White, FontSize = 22, FontFamily = "OpenSansSemibold" },
                new Label { Text = $"{SpecializationInfo.DisplayName(d.PrimarySpecialization)} · Licence {d.LicenseNumber}", TextColor = Color.FromArgb("#D9F1EE"), FontSize = 13 } }
        });
        header.Margin = new Thickness(-16, -16, -16, 0);
        _body.Children.Add(header);

        // Upload tiles
        var tiles = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 10 };
        tiles.Add(UploadTile("Upload Degree", CredentialKind.MedicalDegree, d), 0);
        tiles.Add(UploadTile("Upload Board Certification", CredentialKind.BoardCertification, d), 1);

        var uploaded = new[] { CredentialKind.MedicalDegree, CredentialKind.BoardCertification }.Count(k => d.Credentials.Any(c => c.Kind == k));
        double progress = d.Status switch
        {
            VerificationStatus.Verified => 1.0,
            VerificationStatus.InProgress => 0.75,
            _ => uploaded * 0.25
        };

        var statusText = d.Status switch
        {
            VerificationStatus.Verified => "Verified Professional",
            VerificationStatus.InProgress => "Authentication in Progress",
            VerificationStatus.Rejected => "Verification not approved",
            _ => uploaded == 2 ? "Ready to submit" : "Upload both documents to continue"
        };
        var statusColor = d.Status switch
        {
            VerificationStatus.Verified => Ui.Teal,
            VerificationStatus.Rejected => Ui.Danger,
            _ => Color.FromArgb("#B7950B")
        };

        var card = new VerticalStackLayout
        {
            Spacing = 10,
            Children = {
                Ui.Text("Upload Certifications", 16, Ui.Ink, true),
                Ui.Text("JPEG, PNG or PDF, up to 10 MB each. Files are checked automatically, then reviewed against the medical board registry.", 12, Ui.Muted),
                tiles,
                new ProgressBar { Progress = progress, ProgressColor = Ui.TealLight, HeightRequest = 8 },
                Ui.Text(statusText, 15, statusColor, true)
            }
        };
        if (!string.IsNullOrWhiteSpace(d.StatusNotes)) card.Children.Add(Ui.Text(d.StatusNotes, 12, Ui.Muted));

        if (d.Status is VerificationStatus.NotSubmitted or VerificationStatus.Rejected)
        {
            var submit = Ui.Primary("Submit for Verification", async (_, _) => await SubmitAsync());
            submit.IsEnabled = uploaded == 2;
            card.Children.Add(submit);
        }
        if (d.Status == VerificationStatus.InProgress && Session.Backend is DemoBackend demo)
        {
            card.Children.Add(Ui.Secondary("Approve (demo)", async (_, _) =>
            {
                await Ui.RunAsync(this, async () => { _doctor = await demo.DemoApproveMeAsync(); Render(); });
            }));
        }
        _body.Children.Add(Ui.Card(card));

        // Tags
        var tagBox = new VerticalStackLayout { Spacing = 8, Children = { Ui.Text("Your profile tags", 16, Ui.Ink, true) } };
        if (d.Tags.Count == 0)
        {
            tagBox.Children.Add(Ui.Text("Tags are assigned automatically once you are verified. Based on your specialization you will get:", 12, Ui.Muted));
            var preview = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
            foreach (var t in DoctorTagging.DisplayTagsFor(d.PrimarySpecialization)) { var p = Ui.TagPill(t); p.Margin = new Thickness(0, 0, 6, 6); p.Opacity = 0.5; preview.Add(p); }
            tagBox.Children.Add(preview);
        }
        else
        {
            var flex = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
            foreach (var t in d.Tags) { var p = Ui.TagPill(t); p.Margin = new Thickness(0, 0, 6, 6); flex.Add(p); }
            tagBox.Children.Add(flex);
            tagBox.Children.Add(Ui.Text("Patients can now find and book you. Your profile card:", 12, Ui.Muted));
            tagBox.Children.Add(new ContentView { Content = Ui.DoctorCard(d, null, _ => Task.CompletedTask), WidthRequest = 190, HorizontalOptions = LayoutOptions.Start });
        }
        _body.Children.Add(Ui.Card(tagBox));
    }

    private View UploadTile(string title, CredentialKind kind, Doctor d)
    {
        var existing = d.Credentials.FirstOrDefault(c => c.Kind == kind);
        var icon = new Label { Text = existing == null ? "📄" : (existing.AutoCheckPassed == true ? "✅" : "⚠️"), FontSize = 28, HorizontalOptions = LayoutOptions.Center };
        var stack = new VerticalStackLayout
        {
            Spacing = 4,
            Children = {
                Ui.Text(title, 13, Ui.Ink, true), icon,
                new Label { Text = existing?.FileName ?? "Tap to choose file", FontSize = 11, TextColor = Ui.Muted, HorizontalTextAlignment = TextAlignment.Center, MaxLines = 2, LineBreakMode = LineBreakMode.MiddleTruncation }
            }
        };
        var tile = new Border
        {
            Content = stack, Padding = 10, BackgroundColor = Ui.Mint, StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Stroke = existing == null ? Color.FromArgb("#B9D9D6") : Ui.TealLight, StrokeDashArray = existing == null ? new DoubleCollection { 4, 3 } : null, StrokeThickness = 1.5
        };
        var locked = d.Status is VerificationStatus.InProgress or VerificationStatus.Verified;
        if (!locked)
            tile.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await PickAndUploadAsync(kind)) });
        return tile;
    }

    private async Task PickAndUploadAsync(CredentialKind kind)
    {
        var choice = await DisplayActionSheet("Add document", "Cancel", null, "Choose file (PDF or image)", "Take photo");
        FileResult? file = null;
        try
        {
            if (choice == "Take photo")
            {
                if (!MediaPicker.Default.IsCaptureSupported) { await DisplayAlert("Camera", "This device has no camera available.", "OK"); return; }
                file = await MediaPicker.Default.CapturePhotoAsync();
            }
            else if (choice == "Choose file (PDF or image)")
            {
                file = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "Choose your certificate",
                    FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                    {
                        [DevicePlatform.Android] = new[] { "application/pdf", "image/jpeg", "image/png" }
                    })
                });
            }
        }
        catch (PermissionException)
        {
            await DisplayAlert("Permission needed", "Allow access so you can upload your certificate.", "OK");
            return;
        }
        if (file == null) return;

        await Ui.RunAsync(this, async () =>
        {
            await using var stream = await file.OpenReadAsync();
            await Session.Backend.UploadCredentialAsync(kind, file.FileName, stream);
            _doctor = await Session.Backend.GetMyDoctorProfileAsync();
            Render();
        });
    }

    private async Task SubmitAsync()
    {
        await Ui.RunAsync(this, async () =>
        {
            _doctor = await Session.Backend.SubmitForVerificationAsync();
            Render();
        });
    }
}
