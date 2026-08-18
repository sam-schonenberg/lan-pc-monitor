using System.Diagnostics;
using System.Reflection;

namespace LanPcMonitor.Tray;

internal sealed class AboutForm : Form
{
    private const string ProjectUrl = "https://github.com/sam-schonenberg/lan-pc-monitor";

    public AboutForm(Icon? applicationIcon)
    {
        var assembly = typeof(AboutForm).Assembly;
        var product = AttributeValue<AssemblyProductAttribute>(assembly, value => value.Product)
                      ?? "LAN PC Monitor";
        var description = AttributeValue<AssemblyDescriptionAttribute>(assembly, value => value.Description)
                          ?? "LAN-only PC hardware monitoring.";
        var copyright = AttributeValue<AssemblyCopyrightAttribute>(assembly, value => value.Copyright)
                        ?? string.Empty;
        var version = assembly.GetName().Version?.ToString(3) ?? "Unknown";

        Text = $"About {product}";
        Icon = applicationIcon;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(480, 265);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;

        var icon = new PictureBox
        {
            Image = applicationIcon?.ToBitmap() ?? SystemIcons.Information.ToBitmap(),
            Size = new Size(64, 64),
            SizeMode = PictureBoxSizeMode.StretchImage,
            Margin = new Padding(0, 4, 24, 0)
        };
        var title = new Label
        {
            AutoSize = true,
            Font = new Font(Font.FontFamily, 17, FontStyle.Bold),
            Text = product,
            Margin = new Padding(0)
        };
        var versionLabel = new Label
        {
            AutoSize = true,
            Text = $"Version {version}",
            Margin = new Padding(0, 7, 0, 0)
        };
        var descriptionLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(340, 0),
            Text = description,
            Margin = new Padding(0, 20, 0, 0)
        };
        var copyrightLabel = new Label
        {
            AutoSize = true,
            Text = copyright,
            Margin = new Padding(0, 17, 0, 0)
        };
        var projectLink = new LinkLabel
        {
            AutoSize = true,
            Text = "View project on GitHub",
            Margin = new Padding(0, 12, 0, 0)
        };
        projectLink.LinkClicked += (_, _) => OpenProjectPage();

        var details = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Controls = { title, versionLabel, descriptionLabel, copyrightLabel, projectLink }
        };
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(28, 26, 28, 18)
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.Controls.Add(icon, 0, 0);
        content.Controls.Add(details, 1, 0);

        var closeButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Padding = new Padding(18, 2, 18, 2),
            Anchor = AnchorStyles.Right
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 53,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 20, 8),
            Controls = { closeButton }
        };

        AcceptButton = closeButton;
        CancelButton = closeButton;
        Controls.Add(content);
        Controls.Add(buttons);
    }

    private static string? AttributeValue<T>(Assembly assembly, Func<T, string> selector) where T : Attribute =>
        assembly.GetCustomAttribute<T>() is { } attribute ? selector(attribute) : null;

    private static void OpenProjectPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ProjectUrl) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Could not open the project page: {exception.Message}", "LAN PC Monitor",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
