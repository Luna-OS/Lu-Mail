using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LuMail.App.Views;

/// <summary>Kleine, im Code gebaute Dialoge (Datum/Uhrzeit, Rückfrage).</summary>
public static class Dialogs
{
    /// <summary>Datum und Uhrzeit wählen (Später erinnern / Später senden). Null = abgebrochen.</summary>
    public static async Task<DateTimeOffset?> PickDateTimeAsync(XamlRoot root, string title, DateTimeOffset initial)
    {
        var date = new CalendarDatePicker
        {
            Header = "Datum",
            Date = initial,
            MinDate = DateTimeOffset.Now.Date,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var time = new TimePicker
        {
            Header = "Uhrzeit",
            Time = initial.TimeOfDay,
            ClockIdentifier = "24HourClock",
            MinuteIncrement = 5,
        };
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
        var panel = new StackPanel { Spacing = 12, MinWidth = 300 };
        panel.Children.Add(date);
        panel.Children.Add(time);
        panel.Children.Add(hint);
        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = "OK",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root,
        };
        while (true)
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || date.Date is null) return null;
            var local = date.Date.Value.Date + time.Time;
            var result = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
            if (result > DateTimeOffset.Now) return result;
            hint.Text = "Bitte einen Zeitpunkt in der Zukunft wählen.";
        }
    }

    /// <summary>Ja/Nein-Rückfrage.</summary>
    public static async Task<bool> ConfirmAsync(XamlRoot root, string title, string message, string yes, string no = "Abbrechen")
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = yes,
            CloseButtonText = no,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
