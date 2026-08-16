using Microsoft.Maui.Controls.Shapes;
using PCMonitor.Application.Services.Storage;

namespace PCMonitor.Application.Services.Sync;

public sealed class ForegroundHistorySyncCoordinator(
    HistorySyncService historySync,
    IAppSettingsService settings,
    AppConnectionService connection)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task SynchronizeAsync(Window window)
    {
        if (!await _gate.WaitAsync(0)) return;
        SyncProgressPage? popup = null;
        try
        {
            if (string.IsNullOrWhiteSpace(await settings.GetApiBaseUrlAsync()) || window.Page is not AppShell)
                return;

            popup = new SyncProgressPage();
            await MainThread.InvokeOnMainThreadAsync(() => window.Page.Navigation.PushModalAsync(popup, false));
            var progress = new Progress<HistorySyncProgress>(update => popup.Update(update));
            await historySync.SyncAsync(progress);
            await connection.StartAsync();
            popup.Complete("Latest history is ready.");
            await Task.Delay(650);
        }
        catch (Exception)
        {
            if (popup is not null)
            {
                popup.Complete("PC unavailable — saved data remains available.");
                await Task.Delay(1200);
            }
        }
        finally
        {
            if (popup is not null)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (window.Page?.Navigation.ModalStack.Contains(popup) == true)
                        await window.Page.Navigation.PopModalAsync(false);
                });
            }
            _gate.Release();
        }
    }
}

internal sealed class SyncProgressPage : ContentPage
{
    private readonly Label _message = new() { Text = "Getting the latest history…", FontSize = 14,
        HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.WordWrap };
    private readonly ProgressBar _progress = new() { Progress = 0.03, HeightRequest = 6,
        ProgressColor = Color.FromArgb("#512BD4") };

    public SyncProgressPage()
    {
        BackgroundColor = Color.FromArgb("#99000000");
        var card = new Border
        {
            Padding = 22, Margin = 28, StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Center,
            Content = new VerticalStackLayout { Spacing = 14, Children =
            {
                new Label { Text = "Synchronizing", FontSize = 20, FontAttributes = FontAttributes.Bold,
                    HorizontalTextAlignment = TextAlignment.Center }, _message, _progress
            }}
        };
        card.SetAppThemeColor(BackgroundColorProperty, Colors.White, Color.FromArgb("#242424"));
        card.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#D8DEE9"), Color.FromArgb("#484848"));
        Content = card;
    }

    public void Update(HistorySyncProgress progress) => MainThread.BeginInvokeOnMainThread(() =>
    {
        _message.Text = progress.Message;
        _progress.Progress = progress.BarProgress;
    });

    public void Complete(string message) => MainThread.BeginInvokeOnMainThread(() =>
    {
        _message.Text = message;
        _progress.Progress = 1;
    });
}
