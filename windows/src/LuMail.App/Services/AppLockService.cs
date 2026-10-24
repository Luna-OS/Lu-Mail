using Windows.Security.Credentials.UI;

namespace LuMail.App.Services;

/// <summary>App-Sperre mit Windows Hello / PIN (FUNKTIONEN §14).</summary>
public static class AppLockService
{
    /// <summary>True, wenn Windows Hello (oder PIN) eingerichtet ist.</summary>
    public static async Task<bool> IsAvailableAsync()
    {
        try
        {
            return await UserConsentVerifier.CheckAvailabilityAsync() == UserConsentVerifierAvailability.Available;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Fragt Windows Hello für dieses Fenster ab.</summary>
    public static async Task<bool> UnlockAsync(Microsoft.UI.Xaml.Window window)
    {
        if (!await IsAvailableAsync()) return true; // ohne Windows Hello kann nicht gesperrt werden
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var result = await UserConsentVerifierInterop.RequestVerificationForWindowAsync(hwnd, "Lu-Mail entsperren");
        return result == UserConsentVerificationResult.Verified;
    }
}
