using DocOnTheGo.Mobile.Controls;
using DocOnTheGo.Mobile.Pages;
using DocOnTheGo.Mobile.Services;

namespace DocOnTheGo.Mobile;

public class App : Application
{
    public App()
    {
        UserAppTheme = AppTheme.Light;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new ContentPage { BackgroundColor = Ui.Teal });
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var restored = await Session.RestoreAsync();
            window.Page = restored ? MainTabs.Create() : SignedOutRoot();
        });
        return window;
    }

    public static Page SignedOutRoot() => new NavigationPage(new RegisterPage())
    {
        BarBackgroundColor = Ui.TealDark, BarTextColor = Colors.White
    };

    public static void SetRoot(Page page)
    {
        var window = Current?.Windows.FirstOrDefault();
        if (window != null) window.Page = page;
    }

    public static async Task SignInAndGoHomeAsync(AuthResult auth)
    {
        await Session.SignInAsync(auth);
        SetRoot(MainTabs.Create());
    }

    public static void SignOut()
    {
        Session.SignOut();
        SetRoot(SignedOutRoot());
    }
}

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });
        return builder.Build();
    }
}
