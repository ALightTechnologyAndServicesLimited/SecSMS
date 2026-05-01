using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;

namespace SecSMS.Maui;

public static class AppPermissions
{
    public static async Task<bool> EnsureSmsReadPermissionAsync()
    {
#if ANDROID
        var status = await Permissions.CheckStatusAsync<SmsReadPermission>();
        if (status == PermissionStatus.Granted)
        {
            return true;
        }

        status = await Permissions.RequestAsync<SmsReadPermission>();
        return status == PermissionStatus.Granted;
#else
        await Task.CompletedTask;
        return true;
#endif
    }

    public static async Task<bool> EnsureBluetoothPermissionsAsync()
    {
#if ANDROID
        if (global::Android.OS.Build.VERSION.SdkInt < global::Android.OS.BuildVersionCodes.S)
        {
            return true;
        }

        var status = await Permissions.CheckStatusAsync<BluetoothRuntimePermission>();
        if (status == PermissionStatus.Granted)
        {
            return true;
        }

        status = await Permissions.RequestAsync<BluetoothRuntimePermission>();
        return status == PermissionStatus.Granted;
#else
        await Task.CompletedTask;
        return true;
#endif
    }

#if ANDROID
    sealed class SmsReadPermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        [
            (global::Android.Manifest.Permission.ReadSms, true)
        ];
    }

    sealed class BluetoothRuntimePermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        [
            (global::Android.Manifest.Permission.BluetoothConnect, true),
            (global::Android.Manifest.Permission.BluetoothScan, true)
        ];
    }
#endif
}
