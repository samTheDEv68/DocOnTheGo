using DocOnTheGo.Core.Models;
using DocOnTheGo.Core.Services;
using Microsoft.Maui.Controls.Shapes;

namespace DocOnTheGo.Mobile.Controls;

/// <summary>Colours and building blocks matching the Doc On The Go mockups (teal gradient, white cards, tag pills).</summary>
public static class Ui
{
    public static readonly Color Teal = Color.FromArgb("#1E6F78");
    public static readonly Color TealDark = Color.FromArgb("#134B52");
    public static readonly Color TealLight = Color.FromArgb("#3FB8AF");
    public static readonly Color Mint = Color.FromArgb("#E3F4F2");
    public static readonly Color Page = Color.FromArgb("#F2F7F8");
    public static readonly Color Ink = Color.FromArgb("#1B2B2E");
    public static readonly Color Muted = Color.FromArgb("#5F7377");
    public static readonly Color Star = Color.FromArgb("#F2B01E");
    public static readonly Color Danger = Color.FromArgb("#C0392B");
    public static readonly Color Verified = Color.FromArgb("#1E88E5");

    public static Brush HeaderGradient => new LinearGradientBrush(
        new GradientStopCollection { new GradientStop(TealDark, 0f), new GradientStop(Teal, 0.55f), new GradientStop(TealLight, 1f) },
        new Point(0, 0), new Point(1, 1));

    public static Label Title(string text, double size = 26, Color? color = null) => new()
    {
        Text = text, FontSize = size, FontFamily = "OpenSansSemibold", TextColor = color ?? Colors.White,
        HorizontalTextAlignment = TextAlignment.Center
    };

    public static Label Text(string text, double size = 14, Color? color = null, bool bold = false) => new()
    {
        Text = text, FontSize = size, TextColor = color ?? Ink, FontFamily = bold ? "OpenSansSemibold" : "OpenSansRegular",
        LineBreakMode = LineBreakMode.WordWrap
    };

    public static Label FieldLabel(string text, bool onDark = true) =>
        new() { Text = text, FontSize = 13, TextColor = onDark ? Colors.White : Muted, Margin = new Thickness(2, 8, 0, 2) };

    public static Border Field(View input) => new()
    {
        Content = input, BackgroundColor = Colors.White, Padding = new Thickness(10, 0),
        StrokeShape = new RoundRectangle { CornerRadius = 8 }, Stroke = Color.FromArgb("#D5E3E5"), StrokeThickness = 1
    };

    public static Entry Entry(string placeholder, Keyboard? keyboard = null) => new()
    {
        Placeholder = placeholder, Keyboard = keyboard ?? Keyboard.Default, TextColor = Ink,
        PlaceholderColor = Color.FromArgb("#9AA9AC"), FontSize = 15, HeightRequest = 46
    };

    public static Button Primary(string text, EventHandler onClick, bool onDark = false)
    {
        var b = new Button
        {
            Text = text, FontFamily = "OpenSansSemibold", FontSize = 15, CornerRadius = 10, HeightRequest = 48,
            BackgroundColor = onDark ? TealLight : Teal, TextColor = Colors.White
        };
        b.Clicked += onClick;
        return b;
    }

    public static Button Secondary(string text, EventHandler onClick)
    {
        var b = new Button
        {
            Text = text, FontSize = 14, CornerRadius = 10, HeightRequest = 44, BorderWidth = 1,
            BorderColor = Teal, BackgroundColor = Colors.White, TextColor = Teal
        };
        b.Clicked += onClick;
        return b;
    }

    public static Border Card(View content, double padding = 14) => new()
    {
        Content = content, BackgroundColor = Colors.White, Padding = padding,
        StrokeShape = new RoundRectangle { CornerRadius = 14 }, Stroke = Color.FromArgb("#E2ECEE"), StrokeThickness = 1,
        Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.06f, Radius = 8, Offset = new Point(0, 2) }
    };

    public static Border Pill(string text, Color bg, Color fg) => new()
    {
        Content = new Label { Text = text, FontSize = 11, TextColor = fg, FontFamily = "OpenSansSemibold" },
        BackgroundColor = bg, Padding = new Thickness(8, 3), StrokeThickness = 0,
        StrokeShape = new RoundRectangle { CornerRadius = 10 }, HorizontalOptions = LayoutOptions.Start
    };

    public static Border TagPill(string tag) => tag switch
    {
        DoctorTagging.TagChildSpecialist => Pill("Tag: " + tag, Color.FromArgb("#FFF1D6"), Color.FromArgb("#8A5A00")),
        DoctorTagging.TagSpecialist => Pill("Tag: " + tag, Color.FromArgb("#FFF1D6"), Color.FromArgb("#8A5A00")),
        DoctorTagging.TagMobileUnit => Pill(tag, Color.FromArgb("#E3ECFB"), Color.FromArgb("#1F4E9A")),
        _ => Pill("Tag: " + tag, Mint, TealDark)
    };

    public static View Avatar(string initials, double size = 56)
    {
        var grid = new Grid { WidthRequest = size, HeightRequest = size };
        grid.Add(new Ellipse { Fill = new LinearGradientBrush(new GradientStopCollection { new(Teal, 0), new(TealLight, 1) }, new Point(0, 0), new Point(1, 1)) });
        grid.Add(new Label { Text = initials, TextColor = Colors.White, FontFamily = "OpenSansSemibold", FontSize = size * 0.36,
                             HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center });
        return grid;
    }

    public static View Stars(double rating, int count)
    {
        var full = (int)Math.Round(rating);
        return new HorizontalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Label { Text = new string('★', full) + new string('☆', Math.Max(0, 5 - full)), TextColor = Star, FontSize = 13 },
                new Label { Text = rating > 0 ? rating.ToString("0.0") : "New", FontSize = 12, TextColor = Muted },
            }
        };
    }

    public static Grid GradientHeader(View content, double height = -1)
    {
        var g = new Grid { Background = HeaderGradient, Padding = new Thickness(20, 28, 20, 22) };
        if (height > 0) g.HeightRequest = height;
        g.Add(content);
        return g;
    }

    /// <summary>Doctor card used on Home, AI assistant results and search lists.</summary>
    public static Border DoctorCard(Doctor d, double? distanceKm, Func<Doctor, Task> onBook, bool compact = false)
    {
        var nameRow = new HorizontalStackLayout { Spacing = 4, HorizontalOptions = LayoutOptions.Center };
        nameRow.Add(Text(d.FullName, 14, Ink, true));
        if (d.IsVerified) nameRow.Add(new Label { Text = "✔", TextColor = Verified, FontSize = 12, VerticalOptions = LayoutOptions.Center });

        var stack = new VerticalStackLayout { Spacing = 5 };
        stack.Add(new ContentView { Content = Avatar(d.AvatarInitials, compact ? 48 : 60), HorizontalOptions = LayoutOptions.Center });
        stack.Add(nameRow);
        stack.Add(new Label { Text = SpecializationInfo.DisplayName(d.PrimarySpecialization), FontSize = 12, TextColor = Muted, HorizontalTextAlignment = TextAlignment.Center });
        stack.Add(new ContentView { Content = Stars(d.Rating, d.RatingCount), HorizontalOptions = LayoutOptions.Center });
        if (distanceKm is double km)
            stack.Add(new Label { Text = km < 1 ? "Under 1 km away" : $"{km:0} km away", FontSize = 11, TextColor = Muted, HorizontalTextAlignment = TextAlignment.Center });
        var tags = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Center };
        foreach (var t in d.Tags.Take(2)) { var p = TagPill(t); p.Margin = new Thickness(2); tags.Add(p); }
        stack.Add(tags);
        var book = new Button { Text = "Book Appointment", FontSize = 12, HeightRequest = 36, CornerRadius = 8, BackgroundColor = Teal, TextColor = Colors.White, Padding = new Thickness(6, 0) };
        book.Clicked += async (_, _) => await onBook(d);
        stack.Add(book);
        return Card(stack, 10);
    }

    /// <summary>Two-column grid of doctor cards.</summary>
    public static Grid DoctorGrid(IEnumerable<DoctorMatch> matches, Func<Doctor, Task> onBook)
    {
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 10, RowSpacing = 10 };
        var i = 0;
        foreach (var m in matches)
        {
            if (i % 2 == 0) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.Add(DoctorCard(m.Doctor, m.DistanceKm, onBook), i % 2, i / 2);
            i++;
        }
        return grid;
    }

    public static string UrgencyText(Urgency u) => u switch
    {
        Urgency.Emergency => "Emergency",
        Urgency.Urgent => "See a doctor today",
        Urgency.Soon => "Book within a few days",
        _ => "Routine"
    };

    public static Color UrgencyColor(Urgency u) => u switch
    {
        Urgency.Emergency => Danger,
        Urgency.Urgent => Color.FromArgb("#E67E22"),
        Urgency.Soon => Color.FromArgb("#B7950B"),
        _ => Teal
    };

    public static async Task<bool> RunAsync(Page page, Func<Task> work)
    {
        try
        {
            await work();
            return true;
        }
        catch (Services.BackendException ex)
        {
            await page.DisplayAlert("Doc On The Go", ex.Message, "OK");
        }
        catch (Exception ex)
        {
            await page.DisplayAlert("Something went wrong", ex.Message, "OK");
        }
        return false;
    }
}
