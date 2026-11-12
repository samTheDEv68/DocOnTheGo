using DocOnTheGo.Core.Models;

namespace DocOnTheGo.Mobile.Services;

/// <summary>Signed-in user and app settings. The token lives in Android's secure storage.</summary>
public static class Session
{
    private const string TokenKey = "auth_token";
    private const string ServerUrlKey = "server_url";

    public static string Role { get; private set; } = "";
    public static string UserId { get; private set; } = "";
    public static string DisplayName { get; private set; } = "";
    public static string PatientTag { get; private set; } = "";
    public static string Token { get; private set; } = "";
    public static GeoPoint? LastLocation { get; set; }

    public static bool IsSignedIn => !string.IsNullOrEmpty(UserId);
    public static bool IsDoctor => Role == "Doctor";

    /// <summary>Empty means offline demo mode.</summary>
    public static string ServerUrl
    {
        get => Preferences.Default.Get(ServerUrlKey, "");
        set => Preferences.Default.Set(ServerUrlKey, (value ?? "").Trim().TrimEnd('/'));
    }

    private static IBackend? _backend;

    public static IBackend Backend => _backend ??= CreateBackend();

    public static void ResetBackend() => _backend = null;

    private static IBackend CreateBackend() =>
        string.IsNullOrWhiteSpace(ServerUrl) ? new DemoBackend() : new ApiBackend(ServerUrl);

    public static async Task SignInAsync(AuthResult auth)
    {
        Role = auth.Role;
        UserId = auth.UserId;
        DisplayName = auth.DisplayName;
        PatientTag = auth.PatientTag ?? "";
        Token = auth.Token;
        Preferences.Default.Set("role", Role);
        Preferences.Default.Set("user_id", UserId);
        Preferences.Default.Set("display_name", DisplayName);
        Preferences.Default.Set("patient_tag", PatientTag);
        try { await SecureStorage.Default.SetAsync(TokenKey, Token); }
        catch { /* secure storage unavailable on some emulators; token stays in memory */ }
    }

    public static async Task<bool> RestoreAsync()
    {
        UserId = Preferences.Default.Get("user_id", "");
        if (string.IsNullOrEmpty(UserId)) return false;
        Role = Preferences.Default.Get("role", "");
        DisplayName = Preferences.Default.Get("display_name", "");
        PatientTag = Preferences.Default.Get("patient_tag", "");
        try { Token = await SecureStorage.Default.GetAsync(TokenKey) ?? ""; }
        catch { Token = ""; }
        return true;
    }

    public static void SignOut()
    {
        Role = UserId = DisplayName = PatientTag = Token = "";
        foreach (var k in new[] { "role", "user_id", "display_name", "patient_tag" }) Preferences.Default.Remove(k);
        try { SecureStorage.Default.Remove(TokenKey); } catch { }
    }

    public static async Task<GeoPoint?> TryGetLocationAsync()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted) return LastLocation;

            var loc = await Geolocation.Default.GetLastKnownLocationAsync()
                      ?? await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Low, TimeSpan.FromSeconds(8)));
            if (loc != null) LastLocation = new GeoPoint(loc.Latitude, loc.Longitude);
        }
        catch
        {
            // Location is optional: matching still works without it.
        }
        return LastLocation;
    }
}
