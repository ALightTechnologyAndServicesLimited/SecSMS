using System;

namespace SecSMS.Maui.Services;

/// <summary>
/// App-level lock state. Secrets (SMS OTPs, TOTP accounts) sit behind this lock.
/// Biometric (face/fingerprint) proof unlocks for the current foreground session only;
/// backgrounding the app re-locks. Nothing biometric is stored by this app.
/// </summary>
public interface IAppLockService
{
    /// <summary>Whether the user enabled biometric lock (persisted).</summary>
    bool IsLockEnabled { get; }

    /// <summary>Whether the current foreground session is unlocked.</summary>
    bool IsUnlocked { get; }

    /// <summary>Whether this device can do face/fingerprint auth right now.</summary>
    Task<bool> IsBiometricAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>User-friendly explanation of the current biometric state.</summary>
    Task<string> GetAvailabilityDetailsAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens the OS biometric enrollment screen. False if not possible.</summary>
    bool TryOpenBiometricSettings();

    /// <summary>Prompt face/fingerprint. Returns true only on successful auth.</summary>
    Task<bool> AuthenticateAsync(string reason, CancellationToken cancellationToken = default);

    void Lock();

    void SetLockEnabled(bool enabled);

    event EventHandler? LockStateChanged;
}
