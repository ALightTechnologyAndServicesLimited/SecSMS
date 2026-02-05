using Microsoft.Maui.Storage;

namespace SecSMS.Desktop;

public partial class SettingsPage : ContentPage
{
    const int DefaultClipboardClearSeconds = 30;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        var seconds = Preferences.Get("ClipboardClearSeconds", DefaultClipboardClearSeconds);
        var showCountdown = Preferences.Get("ShowCountdown", true);

        TimeoutEntry.Text = seconds.ToString();
        ShowCountdownSwitch.IsToggled = showCountdown;
        SettingsStatusLabel.Text = string.Empty;
    }

    void OnSaveClicked(object? sender, EventArgs e)
    {
        if (!int.TryParse(TimeoutEntry.Text, out var seconds) || seconds <= 0)
        {
            SettingsStatusLabel.Text = "Please enter a timeout of at least 1 second.";
            return;
        }

        Preferences.Set("ClipboardClearSeconds", seconds);
        Preferences.Set("ShowCountdown", ShowCountdownSwitch.IsToggled);

        SettingsStatusLabel.Text = "Settings saved.";
    }
}
