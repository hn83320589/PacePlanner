using System.Diagnostics;
using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PacePlanner.App.Services;
using PacePlanner.App.ViewModels;
using PacePlanner.Core.Charts;
using PacePlanner.Data;
using Windows.Foundation;
using Microsoft.Windows.Storage.Pickers;
using Windows.UI;

namespace PacePlanner.App;

public sealed partial class MainWindow : Window, IUiServices
{
    private static readonly SolidColorBrush AidRowBrush = new(Color.FromArgb(0x40, 0xE0, 0x7B, 0x00));
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);
    private static readonly SolidColorBrush LineBrush = new(Color.FromArgb(0xFF, 0x2A, 0x6B, 0xB0));
    private static readonly SolidColorBrush FillBrush = new(Color.FromArgb(0x50, 0x2A, 0x6B, 0xB0));
    private static readonly SolidColorBrush GridBrush = new(Color.FromArgb(0x50, 0x80, 0x80, 0x80));
    private static readonly SolidColorBrush AidBrush = new(Color.FromArgb(0xFF, 0xE0, 0x7B, 0x00));

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1500, 920));
        ViewModel = new MainViewModel(new PlanRepository(PlanRepository.DefaultDatabasePath), this);
        ViewModel.PlanChanged += (_, _) => DrawProfile();
        Closed += (_, _) => ViewModel.SaveIfDirty();
        ViewModel.Initialize();
    }

    public MainViewModel ViewModel { get; }

    public static Brush RowBackground(bool hasAidStation) => hasAidStation ? AidRowBrush : TransparentBrush;

    public static string Range(string startKm, string endKm) => $"{startKm} – {endKm}";

    // Windows App SDK pickers (not Windows.Storage.Pickers) also work for unpackaged and elevated processes.
    public async Task<string?> PickGpxFileAsync()
    {
        var picker = new FileOpenPicker(AppWindow.Id) { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".gpx");
        picker.FileTypeFilter.Add("*");
        var result = await picker.PickSingleFileAsync();
        return result?.Path;
    }

    public async Task<string?> PickSaveFileAsync(string suggestedName, string extension, string description)
    {
        var picker = new FileSavePicker(AppWindow.Id)
        {
            SuggestedFileName = suggestedName,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeChoices.Add(description, [extension]);
        var result = await picker.PickSaveFileAsync();
        return result?.Path;
    }

    public async Task<string?> PromptTextAsync(string title, string initialText)
    {
        var box = new TextBox { Text = initialText, SelectionStart = 0, SelectionLength = initialText.Length };
        var dialog = new ContentDialog
        {
            Title = title,
            Content = box,
            PrimaryButtonText = "確定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Root.XamlRoot,
        };
        box.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(box.Text);
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? box.Text.Trim() : null;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = confirmText,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Root.XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public void OpenWithShell(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    private void ProfileCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawProfile();

    /// <summary>Draws the elevation profile from the shared <see cref="ProfileChartLayout"/> geometry.</summary>
    private void DrawProfile()
    {
        ProfileCanvas.Children.Clear();
        var plan = ViewModel.CurrentPlan;
        var width = ProfileCanvas.ActualWidth;
        var height = ProfileCanvas.ActualHeight;
        if (plan is null || width < 100 || height < 60)
        {
            return;
        }

        var layout = ProfileChartLayout.Create(plan, width, height);
        var a = layout.PlotArea;

        foreach (var km in layout.KilometreLines)
        {
            ProfileCanvas.Children.Add(new Line { X1 = km.X, X2 = km.X, Y1 = a.Top, Y2 = a.Bottom, Stroke = GridBrush, StrokeThickness = 1 });
            if (km.ShowLabel)
            {
                AddText(km.Km.ToString("0", CultureInfo.InvariantCulture), km.X - 8, a.Bottom + 2);
            }
        }

        foreach (var tick in layout.ElevationTicks)
        {
            AddText($"{tick.ElevationMeters:0} m", 2, tick.Y - 8);
        }

        if (layout.HasElevation)
        {
            var fill = new Polygon { Fill = FillBrush };
            fill.Points.Add(new Point(a.Left, a.Bottom));
            foreach (var p in layout.Line)
            {
                fill.Points.Add(new Point(p.X, p.Y));
            }

            fill.Points.Add(new Point(a.Right, a.Bottom));
            ProfileCanvas.Children.Add(fill);

            var line = new Polyline { Stroke = LineBrush, StrokeThickness = 1.5 };
            foreach (var p in layout.Line)
            {
                line.Points.Add(new Point(p.X, p.Y));
            }

            ProfileCanvas.Children.Add(line);
        }
        else
        {
            AddText("此賽道沒有海拔資料（或尚未匯入 GPX）", a.Left + 12, a.Top + a.Height / 2 - 10);
        }

        foreach (var m in layout.AidMarkers)
        {
            ProfileCanvas.Children.Add(new Line
            {
                X1 = m.X, X2 = m.X, Y1 = a.Top, Y2 = a.Bottom,
                Stroke = AidBrush, StrokeThickness = 1, StrokeDashArray = [3, 3],
            });
            var dot = new Ellipse { Width = 9, Height = 9, Fill = AidBrush };
            Canvas.SetLeft(dot, m.X - 4.5);
            Canvas.SetTop(dot, m.Y - 4.5);
            ToolTipService.SetToolTip(dot, $"補給站 {m.Km:0.##} km");
            ProfileCanvas.Children.Add(dot);
        }

        ProfileCanvas.Children.Add(new Rectangle { Width = a.Width, Height = a.Height, Stroke = GridBrush, StrokeThickness = 1 });
        Canvas.SetLeft(ProfileCanvas.Children[^1], a.Left);
        Canvas.SetTop(ProfileCanvas.Children[^1], a.Top);
    }

    private void AddText(string text, double x, double y)
    {
        var block = new TextBlock { Text = text, FontSize = 11, Opacity = 0.75 };
        Canvas.SetLeft(block, x);
        Canvas.SetTop(block, y);
        ProfileCanvas.Children.Add(block);
    }
}
