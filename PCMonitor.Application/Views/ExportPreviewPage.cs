using Microsoft.Maui.Controls.Shapes;
using PCMonitor.Application.Services.Export;

namespace PCMonitor.Application.Views;

public sealed class ExportPreviewPage : ContentPage
{
    private readonly GraphImageExportResult _export;
    private readonly GraphImageExportService _service;

    public ExportPreviewPage(GraphImageExportResult export, GraphImageExportService service)
    {
        _export = export;
        _service = service;
        Title = "Export Preview";
        this.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F5F8FC"), Color.FromArgb("#071426"));

        var preview = new Image
        {
            Source = ImageSource.FromStream(() => new MemoryStream(export.ImageBytes, writable: false)),
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        var imageCard = new Border
        {
            Content = preview,
            Padding = 8,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        imageCard.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#EAF1F7"), Color.FromArgb("#0B1A2C"));
        imageCard.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#C4D2DF"), Color.FromArgb("#1D3248"));

        var saved = new Label
        {
            Text = $"Saved to {export.SavedLocation}",
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#2E7D32"),
            HorizontalTextAlignment = TextAlignment.Center
        };
        var optional = new Label
        {
            Text = "Your image is already saved. Sharing is optional.",
            FontSize = 13,
            Opacity = 0.75,
            HorizontalTextAlignment = TextAlignment.Center
        };
        var share = new Button { Text = "Share", HorizontalOptions = LayoutOptions.Fill };
        var open = new Button { Text = "Open / View", HorizontalOptions = LayoutOptions.Fill };
        var done = new Button { Text = "Done", HorizontalOptions = LayoutOptions.Fill };
        share.Clicked += async (_, _) => await RunActionAsync(() => _service.ShareAsync(_export), share);
        open.Clicked += async (_, _) => await RunActionAsync(() => _service.OpenAsync(_export), open);
        done.Clicked += async (_, _) => await Navigation.PopAsync();

        var actions = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star), new(GridLength.Star) },
            ColumnSpacing = 8,
            Children = { share, open, done }
        };
        actions.SetColumn(open, 1);
        actions.SetColumn(done, 2);
        var content = new Grid
        {
            RowDefinitions =
            {
                new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)
            },
            RowSpacing = 10,
            Padding = new Thickness(18, 14, 18, 18),
            Children = { saved, optional, imageCard, actions }
        };
        content.SetRow(optional, 1);
        content.SetRow(imageCard, 2);
        content.SetRow(actions, 3);
        Content = content;
    }

    private async Task RunActionAsync(Func<Task> action, Button button)
    {
        button.IsEnabled = false;
        try { await action(); }
        catch (Exception exception) { await DisplayAlertAsync("Could not open image", exception.Message, "OK"); }
        finally { button.IsEnabled = true; }
    }
}
