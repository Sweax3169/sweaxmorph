using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SweaxMorph.Core;
using SweaxMorph.Native;

namespace SweaxMorph.UI;

public partial class SettingsWindow : Window
{
    Settings S => App.Instance.Settings;
    bool _recording;

    public SettingsWindow()
    {
        InitializeComponent();
        Title = Loc.T("Settings.Title");
        Repo.RequestNavigate += (_, e) => Open(e.Uri.AbsoluteUri);
        SourceInitialized += (_, _) => Chrome();
        Loaded += (_, _) => { Morph.Mood = Mood.Full; Morph.Jump(0.6); };
        MouseMove += (_, e) => Morph.LookAt(e.GetPosition(Morph));
        Morph.MouseLeftButtonUp += (_, _) => { Morph.Jump(0.5); Morph.Sparkles(8); };
        Engines.Changed += OnEnginesChanged;
        Closed += (_, _) => { Engines.Changed -= OnEnginesChanged; if (_recording) App.Instance.StopRecordingHotkey(); };
        Fill();
    }

    void OnEnginesChanged() => Dispatcher.BeginInvoke(BuildEngines);

    /// <summary>The title bar takes the window's own colour, light or dark.</summary>
    void Chrome()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int dark = Theme.Dark ? 1 : 0;
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
        var c = Theme.Color("Haze");
        int bgr = c.R | (c.G << 8) | (c.B << 16);
        Win32.DwmSetWindowAttribute(hwnd, 35, ref bgr, 4); // caption colour
    }

    void Fill()
    {
        Tagline.Text = Loc.Turkish ? "Bırak, seç, bitti." : "Drop, pick, done.";
        VersionText.Text = Loc.T("S.Version", typeof(App).Assembly.GetName().Version?.ToString(3));
        HShortcut.Text = Loc.T("S.Shortcut");
        ShortcutHint.Text = Loc.T("S.ShortcutHint");
        RecordButton.Content = Loc.T("S.Record");
        MacroHint.Text = Loc.T("S.MacroHint");
        DragToggle.Content = Loc.T("S.DragTrigger");
        DragHint.Text = Loc.T("S.DragTriggerHint");
        HExplorer.Text = Loc.T("S.Explorer");
        MenuToggle.Content = Loc.T("S.ContextMenu");
        MenuHint.Text = Loc.T("S.ContextMenuHint");
        HOutput.Text = Loc.T("S.Output");
        OutBeside.Content = Loc.T("S.OutBeside");
        OutDesktop.Content = Loc.T("S.OutDesktop");
        OutCustom.Content = Loc.T("S.OutCustom");
        ChooseButton.Content = Loc.T("S.Choose");
        HQuality.Text = Loc.T("S.Quality");
        QualityHint.Text = Loc.T("S.QualityHint");
        QBest.Content = Loc.T("Quality.Best");
        QBalanced.Content = Loc.T("Quality.Balanced");
        QSmall.Content = Loc.T("Quality.Small");
        HEngines.Text = Loc.T("S.Engines");
        EnginesHint.Text = Loc.T("S.EnginesHint");
        HGeneral.Text = Loc.T("S.General");
        StartupToggle.Content = Loc.T("S.Startup");
        FolderToggle.Content = Loc.T("S.OpenFolder");
        LangLabel.Text = Loc.T("S.Language");
        LangAuto.Content = Loc.T("S.LangAuto");
        HPlace.Text = Loc.T("S.Place");
        PlaceHint.Text = Loc.T("S.PlaceHint");
        PlaceCursor.Content = Loc.T("S.PlaceCursor");
        PlaceCenter.Content = Loc.T("S.PlaceCenter");
        PlaceRemember.Content = Loc.T("S.PlaceRemember");
        AdminToggle.Content = Loc.T("S.Admin");
        AdminHint.Text = Loc.T("S.AdminHint");
        HLook.Text = Loc.T("S.Look");
        LookHint.Text = Loc.T("S.LookHint");
        ThemeSystem.Content = Loc.T("S.ThemeSystem");
        ThemeLight.Content = Loc.T("S.ThemeLight");
        ThemeDark.Content = Loc.T("S.ThemeDark");
        HFormats.Text = Loc.T("S.Formats");
        Sync();
        BuildEngines();
        BuildFormats();
        BuildPalettes();
    }

    void Sync()
    {
        Keys.ItemsSource = _recording ? new[] { "…" } : Hotkey.Parse(S.Hotkey).ToString().Split(" + ");
        RecordButton.Content = _recording ? Loc.T("S.Recording") : Loc.T("S.Record");
        DragToggle.IsChecked = S.DragTrigger;
        MenuToggle.IsChecked = S.ContextMenu;
        OutBeside.IsChecked = S.Output == OutputPlace.BesideOriginal;
        OutDesktop.IsChecked = S.Output == OutputPlace.Desktop;
        OutCustom.IsChecked = S.Output == OutputPlace.Custom;
        CustomRow.Visibility = S.Output == OutputPlace.Custom ? Visibility.Visible : Visibility.Collapsed;
        CustomPath.Text = S.OutputFolder ?? "";
        QBest.IsChecked = S.Quality == Quality.Best;
        QBalanced.IsChecked = S.Quality == Quality.Balanced;
        QSmall.IsChecked = S.Quality == Quality.Small;
        StartupToggle.IsChecked = S.StartWithWindows;
        FolderToggle.IsChecked = S.OpenFolderWhenDone;
        LangAuto.IsChecked = S.Language == "auto";
        LangEn.IsChecked = S.Language == "en";
        LangTr.IsChecked = S.Language == "tr";
        ThemeSystem.IsChecked = S.Theme == "system";
        ThemeLight.IsChecked = S.Theme == "light";
        ThemeDark.IsChecked = S.Theme == "dark";
        PlaceCursor.IsChecked = S.Placement == "cursor";
        PlaceCenter.IsChecked = S.Placement == "center";
        PlaceRemember.IsChecked = S.Placement == "remember";
        AdminToggle.IsChecked = S.AdminKeys;
    }

    // ── palettes ────────────────────────────────────────────────────────────

    /// <summary>One tile per palette: Morph itself in those colours, on that palette's cloud, in the current theme.</summary>
    void BuildPalettes()
    {
        PaletteTiles.Children.Clear();
        foreach (var p in Palettes.All)
        {
            var m = p.For(Theme.Dark);
            bool on = p.Key == Theme.Palette.Key;
            var tile = new Border
            {
                Width = 118, Height = 146, Margin = new Thickness(0, 0, 9, 9), CornerRadius = new CornerRadius(20),
                Background = Brush(m.Haze), BorderBrush = Brush(on ? m.Accent : m.Line), BorderThickness = new Thickness(on ? 2.5 : 1),
                Cursor = System.Windows.Input.Cursors.Hand, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(),
                ToolTip = p.Name, Tag = p.Key,
            };
            var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) };
            foreach (var hue in new[] { m.Accent, p.GelBottom, m.Accent2 })
                dots.Children.Add(new Ellipse { Width = 9, Height = 9, Margin = new Thickness(2.5, 0, 2.5, 0), Fill = Brush(hue) });
            tile.Child = new StackPanel
            {
                Children =
                {
                    new Image { Source = Swatch(p, Theme.Dark), Width = 104, Height = 96, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = p.Name, FontSize = 14, FontWeight = FontWeights.Black, Foreground = Brush(m.Ink), HorizontalAlignment = HorizontalAlignment.Center },
                    dots,
                },
            };
            // a springy hover, like everything else in Morph's world
            tile.MouseEnter += (_, _) => { var t = (ScaleTransform)tile.RenderTransform; Springs.To(t, ScaleTransform.ScaleXProperty, 1.05, 420, 0.55); Springs.To(t, ScaleTransform.ScaleYProperty, 1.05, 420, 0.55); };
            tile.MouseLeave += (_, _) => { var t = (ScaleTransform)tile.RenderTransform; Springs.To(t, ScaleTransform.ScaleXProperty, 1, 300, 0.7); Springs.To(t, ScaleTransform.ScaleYProperty, 1, 300, 0.7); };
            tile.MouseLeftButtonUp += (_, _) => UsePalette(p.Key);
            PaletteTiles.Children.Add(tile);
        }
    }

    void UsePalette(string key)
    {
        if (S.Palette == key) { Morph.Jump(0.4); return; }
        S.Palette = key;
        S.Save();
        Theme.Apply(S.Theme, key);
        Chrome();
        Fill();
        Morph.Jump(0.6);
        Morph.Sparkles(10);
    }

    static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush(Blob.Hex(hex));
        b.Freeze();
        return b;
    }

    static readonly Dictionary<(string, bool), ImageSource> Swatches = [];

    /// <summary>Morph drawn once, still, in a palette's gel, on a soft round cloud of that palette's haze.</summary>
    internal static ImageSource Swatch(Palette p, bool dark)
    {
        if (Swatches.TryGetValue((p.Key, dark), out var done)) return done;
        const double W = 104, H = 96;
        var host = new Canvas { Width = W, Height = H };
        var m = p.For(dark);
        var haze = Blob.Hex(m.Haze);
        var cloud = new Ellipse
        {
            Width = 100, Height = 78,
            Fill = new RadialGradientBrush(Blob.Mix(haze, Blob.Hex(p.GelTop), dark ? 0.16 : 0.34), Color.FromArgb(0, haze.R, haze.G, haze.B)),
        };
        Canvas.SetLeft(cloud, 2);
        Canvas.SetTop(cloud, 12);
        host.Children.Add(cloud);
        var blob = new Blob { Frozen = true, Still = true, Radius = 30, Width = 90, Height = 90 };
        blob.Tint(Blob.Hex(p.GelTop), Blob.Hex(p.GelBottom));
        blob.Ink = Blob.Hex(p.Face);
        Canvas.SetLeft(blob, 7);
        Canvas.SetTop(blob, 2);
        host.Children.Add(blob);
        host.Measure(new Size(W, H));
        host.Arrange(new Rect(0, 0, W, H));
        host.UpdateLayout();
        blob.SnapColours();
        blob.Step(0.016);
        var rtb = new RenderTargetBitmap((int)(W * 2), (int)(H * 2), 192, 192, PixelFormats.Pbgra32);
        rtb.Render(host);
        rtb.Freeze();
        Swatches[(p.Key, dark)] = rtb;
        return rtb;
    }

    // ── where Morph opens, administrator windows ────────────────────────────

    void Place_Click(object sender, RoutedEventArgs e)
    {
        S.Placement = sender == PlaceCenter ? "center" : sender == PlaceRemember ? "remember" : "cursor";
        S.Save();
        Sync();
    }

    async void Admin_Click(object sender, RoutedEventArgs e)
    {
        bool want = AdminToggle.IsChecked == true;
        AdminToggle.IsEnabled = false;
        AdminStatus.Visibility = Visibility.Visible;
        try
        {
            if (want)
            {
                AdminStatus.Text = Loc.T("S.AdminAsk");
                var (ok, refused) = await Elevation.Enable();
                S.AdminKeys = ok;
                AdminStatus.Text = ok ? Loc.T("S.AdminOn") : refused ? Loc.T("S.AdminNo") : Loc.T("S.AdminFail");
                if (ok) Morph.Jump(0.5);
            }
            else
            {
                await Elevation.Disable();
                S.AdminKeys = false;
                AdminStatus.Visibility = Visibility.Collapsed;
            }
            S.Save();
        }
        finally
        {
            AdminToggle.IsEnabled = true;
            Sync();
        }
    }

    void BuildEngines()
    {
        EngineList.Children.Clear();
        foreach (var e in Engines.All.Where(x => x != Engines.SystemTar))
        {
            var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var dot = new Ellipse { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Fill = e.Ready ? Theme.Brush("Good") : Theme.Brush("Line") };
            var name = new TextBlock { Text = e.Name, FontSize = 14.5, FontWeight = FontWeights.ExtraBold };
            var what = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Theme.Brush("Ink2"), Text = What(e), TextWrapping = TextWrapping.Wrap };
            var text = new StackPanel { Children = { name, what } };
            Grid.SetColumn(text, 1);
            row.Children.Add(dot);
            row.Children.Add(text);
            if (!e.Ready)
            {
                var b = new Button { Style = (Style)FindResource("Word"), Tag = e.Installable ? "" : "", Content = e.Installable ? $"{Loc.T("S.Get")} · {Loc.Size(e.ApproxBytes)}" : Loc.T("S.Install"), VerticalAlignment = VerticalAlignment.Center };
                b.Click += async (_, _) =>
                {
                    if (!e.Installable) { Open(e.Url); return; }
                    b.IsEnabled = false;
                    var p = new Progress<double>(v => b.Content = $"{Math.Round(v * 100)}%");
                    bool ok = await Engines.Install(e, p, CancellationToken.None);
                    if (!ok) { b.IsEnabled = true; b.Content = Loc.T("Err.Download", e.Name); }
                };
                Grid.SetColumn(b, 2);
                row.Children.Add(b);
            }
            EngineList.Children.Add(row);
        }
    }

    static string What(Engine e)
    {
        string state = e.Ready ? Loc.T("S.Ready") : Loc.T("S.NotFound");
        string role = e.Id switch
        {
            "ffmpeg" => Loc.Turkish ? "Video, ses ve altyazı" : "Video, audio and subtitles",
            "pandoc" => Loc.Turkish ? "Markdown, HTML, EPUB, LaTeX, Jupyter…" : "Markdown, HTML, EPUB, LaTeX, Jupyter…",
            "word" => Loc.Turkish ? "Word belgeleri, PDF → Word" : "Word documents, PDF → Word",
            "excel" => Loc.Turkish ? "Excel çalışma kitapları" : "Excel workbooks",
            "powerpoint" => Loc.Turkish ? "Sunular, slaytlar → görsel ve video" : "Decks, slides → pictures and video",
            "libreoffice" => Loc.Turkish ? "Pages, Numbers, Keynote, Visio, Publisher…" : "Pages, Numbers, Keynote, Visio, Publisher…",
            "calibre" => Loc.Turkish ? "Kindle: MOBI, AZW3" : "Kindle: MOBI, AZW3",
            "edge" => Loc.Turkish ? "HTML ve SVG → PDF" : "HTML and SVG → PDF",
            "7zip" => Loc.Turkish ? "7Z arşivleri" : "7Z archives",
            _ => "",
        };
        return $"{state} · {role}";
    }

    void BuildFormats()
    {
        int formats = Formats.All.Count(f => f.Kind != Kind.Folder);
        int routes = Formats.All.Sum(f => Planner.Targets(f.Id).Count);
        FormatsHint.Text = Loc.T("S.FormatsHint", formats, routes.ToString("N0", Loc.Turkish ? new System.Globalization.CultureInfo("tr-TR") : System.Globalization.CultureInfo.InvariantCulture));
        KindChips.Children.Clear();
        foreach (var group in Formats.All.Where(f => f.Kind != Kind.Folder).GroupBy(f => f.Kind))
        {
            var hue = Blob.Hex(Formats.Hue(group.Key));
            var chip = new Border
            {
                CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 6, 12, 7), Margin = new Thickness(0, 0, 8, 8),
                Background = new SolidColorBrush(Color.FromArgb(40, hue.R, hue.G, hue.B)),
                ToolTip = string.Join("  ", group.Select(f => f.Label)),
                Child = new TextBlock
                {
                    FontSize = 13, FontWeight = FontWeights.ExtraBold,
                    Inlines = { new System.Windows.Documents.Run(Formats.KindName(group.Key) + " ") { Foreground = Theme.Brush("Ink") }, new System.Windows.Documents.Run(group.Count().ToString()) { Foreground = new SolidColorBrush(Blob.Mix(hue, Colors.Black, Theme.Dark ? 0 : 0.25)) } },
                },
            };
            KindChips.Children.Add(chip);
        }
    }

    // ── changes ─────────────────────────────────────────────────────────────

    void Record_Click(object sender, RoutedEventArgs e)
    {
        if (_recording) { App.Instance.StopRecordingHotkey(); _recording = false; Sync(); return; }
        _recording = true;
        RecordStatus.Visibility = Visibility.Collapsed;
        Sync();
        App.Instance.RecordHotkey(h =>
        {
            _recording = false;
            // a bare key that is used for typing would be stolen everywhere: only F13–F24, Pause and the like may stand alone.
            // Say so: a macro key with nothing recorded on it sends a plain key (F1 on many keyboards), and a silent
            // refusal looked like the macro key did not work at all.
            if (h is { } hk && (hk.HasModifier || Hotkey.IsFree(hk.Key)))
            {
                S.Hotkey = hk.Spec;
                S.Save();
                App.Instance.ApplyHotkey();
                Morph.Jump(0.5);
            }
            else if (h is { } bare)
            {
                RecordStatus.Text = Loc.T("S.RecordBare", bare.ToString());
                RecordStatus.Visibility = Visibility.Visible;
            }
            Sync();
        });
    }

    void Toggle_Click(object sender, RoutedEventArgs e)
    {
        bool on = ((ToggleButton)sender).IsChecked == true;
        if (sender == DragToggle) { S.DragTrigger = on; App.Instance.ApplyHotkey(); }
        else if (sender == MenuToggle) { S.ContextMenu = on; Shell.ContextMenu(on); }
        else if (sender == StartupToggle) { S.StartWithWindows = on; Shell.AutoStart(on); }
        else if (sender == FolderToggle) S.OpenFolderWhenDone = on;
        S.Save();
        Sync();
    }

    void Output_Click(object sender, RoutedEventArgs e)
    {
        S.Output = sender == OutDesktop ? OutputPlace.Desktop : sender == OutCustom ? OutputPlace.Custom : OutputPlace.BesideOriginal;
        if (S.Output == OutputPlace.Custom && string.IsNullOrEmpty(S.OutputFolder)) Choose_Click(sender, e);
        S.Save();
        Sync();
    }

    void Choose_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("S.OutCustom") };
        if (dlg.ShowDialog(this) == true) S.OutputFolder = dlg.FolderName;
        if (string.IsNullOrEmpty(S.OutputFolder)) S.Output = OutputPlace.BesideOriginal;
        S.Save();
        Sync();
    }

    void Quality_Click(object sender, RoutedEventArgs e)
    {
        S.Quality = sender == QBest ? Quality.Best : sender == QSmall ? Quality.Small : Quality.Balanced;
        S.Save();
        Sync();
    }

    void Lang_Click(object sender, RoutedEventArgs e)
    {
        S.Language = sender == LangEn ? "en" : sender == LangTr ? "tr" : "auto";
        S.Save();
        Loc.Init(S.Language);
        if (S.ContextMenu) Shell.ContextMenu(true); // the menu text follows the language
        App.Instance.Relabel();
        Fill();
    }

    void Theme_Click(object sender, RoutedEventArgs e)
    {
        S.Theme = sender == ThemeLight ? "light" : sender == ThemeDark ? "dark" : "system";
        S.Save();
        Theme.Apply(S.Theme, S.Palette);
        Chrome();
        Fill();
    }

    static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
