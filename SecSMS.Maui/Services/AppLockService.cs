using System;
using Plugin.Maui.Biometric;
#if ANDROID
using Android.Content;
using Platform = Microsoft.Maui.ApplicationModel.Platform;
#endif

namespace SecSMS.Maui.Services;

public sealed class AppLockService : IAppLockService
{
    const string PrefsKey = "secsms.applock.enabled";

    readonly IBiometric _biometric;
    bool _unlocked;

    public AppLockService(IBiometric biometric)
    {
        _biometric = biometric ?? throw new ArgumentNullException(nameof(biometric));
    }

    public event EventHandler? LockStateChanged;

    public bool IsLockEnabled => Preferences.Default.Get(PrefsKey, false);

    public bool IsUnlocked => !IsLockEnabled || _unlocked;

    public async Task<bool> IsBiometricAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Strong (Class 3) only: fingerprint on most devices, face only
            // where the OEM certifies it Class 3 (e.g. Pixel).
            var status = await _biometric.GetAuthenticationStatusAsync(AuthenticatorStrength.Strong).ConfigureAwait(false);
            return status == BiometricHwStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> GetAvailabilityDetailsAsync(CancellationToken cancellationToken = default)
    {
        BiometricHwStatus status;
        try
        {
            status = await _biometric.GetAuthenticationStatusAsync(AuthenticatorStrength.Strong).ConfigureAwait(false);
        }
        catch
        {
            return "Biometrics are not available right now.";
        }

        if (status == BiometricHwStatus.Success)
        {
            try
            {
                var types = await _biometric.GetEnrolledBiometricTypesAsync().ConfigureAwait(false);
                var names = types?.Where(t => !string.Equals(t.ToString(), "None", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct()
                    .ToList();
                if (names is { Count: > 0 })
                {
                    return "Strong biometrics ready. Enrolled: " + string.Join(", ", names) + ".";
                }
            }
            catch
            {
                // Fall through to generic ready message.
            }

            return "Strong biometrics are ready.";
        }

        return status switch
        {
            BiometricHwStatus.NotEnrolled or BiometricHwStatus.PresentButNotEnrolled =>
                "No Strong biometric enrolled. Enroll a fingerprint (Class 3) in system settings, then try again.",
            BiometricHwStatus.NoHardware =>
                "This device has no fingerprint or face hardware.",
            BiometricHwStatus.LockedOut =>
                "Too many failed attempts. Wait a while and try again.",
            BiometricHwStatus.Unavailable =>
                "Biometric hardware is temporarily unavailable. Try again later.",
            BiometricHwStatus.Unsupported =>
                "Biometrics are not supported on this device.",
            _ => "Biometrics are not available right now.",
        };
    }

    public bool TryOpenBiometricSettings()
    {
#if ANDROID
        try
        {
            var activity = Platform.CurrentActivity;
            if (activity == null)
            {
                return false;
            }

            var action = OperatingSystem.IsAndroidVersionAtLeast(29)
                ? Android.Provider.Settings.ActionBiometricEnroll
                : Android.Provider.Settings.ActionSecuritySettings;
            activity.StartActivity(new Intent(action));
            return true;
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
    }

    public async Task<bool> AuthenticateAsync(string reason, CancellationToken cancellationToken = default)
    {
        AuthenticationResponse? response;
        try
        {
            // The OS prompt must be shown from the UI thread (see plugin sample).
            response = await MainThread.InvokeOnMainThreadAsync(() => _biometric.AuthenticateAsync(
                new AuthenticationRequest
                {
                    Title = "SecSMS",
                    Subtitle = "Strong biometric required",
                    Description = reason,
                    NegativeText = "Cancel",
                    AllowPasswordAuth = false,
                    AuthStrength = AuthenticatorStrength.Strong,
                },
                cancellationToken)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            return false;
        }

        var ok = response?.Status == BiometricResponseStatus.Success;
        if (ok)
        {
            _unlocked = true;
            LockStateChanged?.Invoke(this, EventArgs.Empty);
        }

        return ok;
    }

    public void Lock()
    {
        if (!IsLockEnabled)
        {
            return;
        }

        if (!_unlocked)
        {
            return;
        }

        _unlocked = false;
        LockStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetLockEnabled(bool enabled)
    {
        Preferences.Default.Set(PrefsKey, enabled);
        if (!enabled)
        {
            _unlocked = true;
        }
        else
        {
            _unlocked = false;
        }

        LockStateChanged?.Invoke(this, EventArgs.Empty);
    }
}
