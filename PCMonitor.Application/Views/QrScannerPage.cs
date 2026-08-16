using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace PCMonitor.Application.Views;

public sealed class QrScannerPage : ContentPage
{
    private readonly CameraBarcodeReaderView _camera;
    private readonly Func<string, Task> _onScanned;
    private int _completed;

    public QrScannerPage(Func<string, Task> onScanned)
    {
        _onScanned = onScanned;
        Title = "Scan PC setup code";
        BackgroundColor = Colors.Black;

        _camera = new CameraBarcodeReaderView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Options = new BarcodeReaderOptions
            {
                Formats = BarcodeFormats.TwoDimensional,
                AutoRotate = true,
                Multiple = false
            }
        };
        _camera.BarcodesDetected += OnBarcodesDetected;

        var close = new Button
        {
            Text = "Cancel",
            Margin = 20,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.End
        };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();

        Content = new Grid
        {
            Children =
            {
                _camera,
                new Border
                {
                    Margin = new Thickness(36, 100),
                    Stroke = Colors.White,
                    StrokeThickness = 3,
                    BackgroundColor = Colors.Transparent,
                    HorizontalOptions = LayoutOptions.Fill,
                    VerticalOptions = LayoutOptions.Fill,
                    InputTransparent = true
                },
                new Label
                {
                    Text = "Point the camera at the QR code on the PC setup page",
                    TextColor = Colors.White,
                    BackgroundColor = Color.FromArgb("#99000000"),
                    Padding = 16,
                    Margin = new Thickness(20, 24),
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalOptions = LayoutOptions.Start,
                    InputTransparent = true
                },
                close
            }
        };
    }

    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs args)
    {
        var value = args.Results.FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(value) || Interlocked.Exchange(ref _completed, 1) != 0) return;
        _camera.IsDetecting = false;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Navigation.PopModalAsync();
            await _onScanned(value);
        });
    }

    protected override void OnDisappearing()
    {
        _camera.IsDetecting = false;
        _camera.BarcodesDetected -= OnBarcodesDetected;
        base.OnDisappearing();
    }
}
