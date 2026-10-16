using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace DiskRadar;

public partial class MainWindow : Window
{
    sealed record Location(string Path, string Label) { public override string ToString() => Label; }

    static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskRadar");

    Node? root, current;
    List<ProjectRow> projects = new();
    CancellationTokenSource? cts;
    readonly DispatcherTimer progressTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly Stopwatch scanClock = new();
    long expectedBytes;   // 0 = unknown, so no remaining-time estimate

    public MainWindow()
    {
        var theme = LoadTheme();
        ApplyTheme(theme);
        InitializeComponent();
        ThemeBox.SelectedIndex = theme switch { "Açık" => 1, "Koyu" => 2, _ => 0 };

        foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
            DriveBox.Items.Add(new Location(d.RootDirectory.FullName,
                $"{d.RootDirectory.FullName}  {d.VolumeLabel}  –  {Fmt.Size(d.AvailableFreeSpace)} boş / {Fmt.Size(d.TotalSize)}"));
        DriveBox.SelectedIndex = 0;

        SetupGrid(ExplorerGrid, showPath: false);
        SetupGrid(BigGrid, showPath: true);
        SetupGrid(UnusedGrid, showPath: true);
        SetupGrid(SearchGrid, showPath: true);
        SetupGrid(CleanGrid, showPath: true);
        SetupGrid(DupGrid, showPath: true);
        DupGrid.Columns.Insert(0, Col("Kopya grubu", "Group", new DataGridLength(230)));
        SetupGrid(TypeFilesGrid, showPath: true);
        SetupGrid(ProjectPartsGrid, showPath: true);
        SetupTypesGrid();
        SetupProjectsGrid();
        SetupAppsGrid();
        SetupChangeGrid();
        Map.NodeClicked += n => { if (n.IsDir) Navigate(n); };

        BannerText.Text = (Scanner.AccessReliable
            ? "Windows dosyaların son açılma tarihini kaydediyor; 'Son kullanım' açılma ve değiştirilme tarihlerinin yenisidir."
            : "Bu bilgisayarda Windows 'son açılma' tarihini kaydetmiyor; 'Son kullanım' son DEĞİŞTİRİLME tarihidir (sadece okunan dosyalar eski görünebilir).")
            + "  Bazı klasörler yönetici izni olmadan okunamaz; tam sonuç için programı 'Yönetici olarak çalıştır'.";

        progressTimer.Tick += (_, _) => ShowScanProgress();
        Loaded += async (_, _) => await LoadLastScan();
    }

    // ---------- Fast start: last scan from cache ----------

    async Task LoadLastScan()
    {
        StatusText.Text = "Son tarama yükleniyor…";
        var cached = await Task.Run(ScanCache.Load);
        if (root != null) return;   // the user already started a new scan
        if (cached is not { } c)
        {
            StatusText.Text = "Başlamak için bir konum seçip 'Tara'ya basın.";
            return;
        }
        SelectLocation(c.Root.FullPath);
        ShowTree(c.Root, fresh: false);
        StatusText.Text = $"Son tarama gösteriliyor ({c.Date:dd.MM.yyyy HH:mm}, {c.Root.FullPath}). Güncel bilgi için 'Tara'ya basın.";
    }

    void SelectLocation(string path)
    {
        var loc = DriveBox.Items.OfType<Location>().FirstOrDefault(l => l.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (loc == null) { loc = new Location(path, path); DriveBox.Items.Add(loc); }
        DriveBox.SelectedItem = loc;
    }

    // ---------- Scanning ----------

    async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (DriveBox.SelectedItem is not Location loc) return;
        cts = new CancellationTokenSource();
        ScanBtn.IsEnabled = PickBtn.IsEnabled = DupBtn.IsEnabled = false;
        CancelBtn.IsEnabled = true;
        expectedBytes = ExpectedSize(loc.Path);
        ScanProgress.Visibility = Visibility.Visible;
        scanClock.Restart();
        progressTimer.Start();
        try
        {
            var token = cts.Token;
            var scanned = await Task.Run(() => Scanner.Scan(loc.Path, token));
            ShowTree(scanned, fresh: true);
            StatusText.Text = $"Tamamlandı: {Scanner.ItemCount:N0} öğe, {Fmt.Size(scanned.Size)}, süre {Clock(scanClock.Elapsed)}.";
        }
        catch (Exception ex) when (ex is OperationCanceledException || ex is AggregateException a && a.InnerExceptions.All(i => i is OperationCanceledException))
        {
            StatusText.Text = $"Tarama durduruldu ({Clock(scanClock.Elapsed)} sonra).";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Hata: " + ex.Message;
        }
        finally
        {
            progressTimer.Stop();
            scanClock.Stop();
            ScanProgress.Visibility = Visibility.Collapsed;
            ScanBtn.IsEnabled = PickBtn.IsEnabled = DupBtn.IsEnabled = true;
            CancelBtn.IsEnabled = false;
        }
    }

    /// <summary>Best guess of how many bytes the scan will find: the drive's used space, or this folder's last scan.</summary>
    static long ExpectedSize(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (string.Equals(Path.GetPathRoot(full), full, StringComparison.OrdinalIgnoreCase))
            {
                var d = new DriveInfo(full);
                return d.TotalSize - d.TotalFreeSpace;
            }
        }
        catch { }
        return History.LoadLatest(path)?.Total ?? 0;
    }

    void ShowScanProgress()
    {
        var elapsed = scanClock.Elapsed;
        long bytes = Interlocked.Read(ref Scanner.BytesScanned);
        string remaining, amount;
        if (expectedBytes > 0)
        {
            double f = Math.Min(0.99, (double)bytes / expectedBytes);
            ScanProgress.IsIndeterminate = false;
            ScanProgress.Value = f * 100;
            remaining = f > 0.02 && elapsed.TotalSeconds > 3
                ? "~" + Clock(TimeSpan.FromSeconds(elapsed.TotalSeconds * (1 - f) / f))
                : "hesaplanıyor…";
            amount = $"%{f * 100:0}  ·  {Fmt.Size(bytes)} / ~{Fmt.Size(expectedBytes)}";
        }
        else
        {
            ScanProgress.IsIndeterminate = true;
            remaining = "bilinmiyor (bu klasörün ilk taraması)";
            amount = Fmt.Size(bytes);
        }
        StatusText.Text = $"Taranıyor…  {amount}  ·  {Scanner.ItemCount:N0} öğe  ·  geçen {Clock(elapsed)}  ·  kalan {remaining}   —   {Scanner.Current}";
    }

    static string Clock(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");

    void ShowTree(Node scanned, bool fresh)
    {
        root = scanned;
        projects = Projects.Find(scanned);   // also marks bin/obj/node_modules… as cleanable
        DupGrid.ItemsSource = null;
        if (fresh)
        {
            History.Save(History.From(scanned));
            var date = DateTime.Now;
            Task.Run(() => ScanCache.Save(scanned, date));
        }
        ShowHistory(scanned.FullPath);
        Navigate(scanned);
        RefreshLists();
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => cts?.Cancel();

    void Pick_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Taranacak klasörü seçin" };
        if (dlg.ShowDialog(this) != true) return;
        SelectLocation(dlg.FolderName);
    }

    // ---------- Views ----------

    void Navigate(Node folder)
    {
        current = folder;
        var filter = CurrentFilter();
        ExplorerGrid.ItemsSource = folder.Children?.Where(filter).ToList();
        PathText.Text = $"{folder.FullPath}   —   {Fmt.Size(folder.Size)},  {folder.FileCount:N0} dosya";
        UpBtn.IsEnabled = folder.Parent != null;
        Map.Show(folder);
        Tabs.SelectedIndex = 0;
    }

    Func<Node, bool> CurrentFilter() => FilterBox.SelectedIndex switch
    {
        1 => n => n.Safety == Safety.Cleanable,
        2 => n => n.DaysUnused >= 365,
        3 => n => n.IsDir,
        4 => n => !n.IsDir,
        5 => n => n.Safety != Safety.System,
        _ => _ => true,
    };

    void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && current != null) Navigate(current);
    }

    void Up_Click(object sender, RoutedEventArgs e)
    {
        if (current?.Parent != null) Navigate(current.Parent);
    }

    void RefreshLists()
    {
        if (root == null) return;
        BigGrid.ItemsSource = Scanner.BiggestFiles(root, 200);
        var clean = Scanner.Cleanable(root);
        CleanGrid.ItemsSource = clean;
        CleanTotal.Text = $"Toplam temizlenebilir: {Fmt.Size(clean.Sum(n => n.Size))}";
        TypesGrid.ItemsSource = FileTypes.Summarize(root);
        TypeFilesGrid.ItemsSource = null;
        TypeFilesText.Text = "";
        ProjectsGrid.ItemsSource = projects;
        ProjectPartsGrid.ItemsSource = null;
        ProjectPartsText.Text = "";
        ProjectTotal.Text = $"{projects.Count} proje, yeniden oluşturulabilir: {Fmt.Size(projects.Sum(p => p.RebuildValue))}";
        AppsGrid.ItemsSource = InstalledApp.Load(root);
        RefreshUnused();
        if (SearchGrid.ItemsSource != null) RunSearch();
    }

    void RefreshUnused()
    {
        if (root == null || DaysBox.SelectedItem is not ComboBoxItem item) return;
        var list = Scanner.Unused(root, int.Parse((string)item.Tag), 50L * 1024 * 1024);
        UnusedGrid.ItemsSource = list;
        UnusedTotal.Text = $"Toplam: {Fmt.Size(list.Sum(n => n.Size))}";
    }

    void Unused_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) RefreshUnused();
    }

    void RunSearch()
    {
        if (root == null || string.IsNullOrWhiteSpace(SearchBox.Text)) return;
        SearchGrid.ItemsSource = Scanner.Search(root, SearchBox.Text.Trim(), 1000);
    }

    void Search_Click(object sender, RoutedEventArgs e) => RunSearch();
    void SearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) RunSearch(); }

    // ---------- Theme ----------

#pragma warning disable WPF0001 // ThemeMode is marked experimental in .NET 9
    static void ApplyTheme(string theme) => Application.Current.ThemeMode = theme switch
    {
        "Açık" => ThemeMode.Light,
        "Koyu" => ThemeMode.Dark,
        _ => ThemeMode.System,
    };
#pragma warning restore WPF0001

    static string LoadTheme()
    {
        try { return File.ReadAllText(Path.Combine(SettingsDir, "theme.txt")).Trim(); }
        catch { return "Sistem"; }
    }

    void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || ThemeBox.SelectedItem is not ComboBoxItem item) return;
        var theme = (string)item.Content;
        ApplyTheme(theme);
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(Path.Combine(SettingsDir, "theme.txt"), theme);
        }
        catch { }
    }

    // ---------- Grid setup ----------

    static void BasicGrid(DataGrid g)
    {
        g.AutoGenerateColumns = false;
        g.IsReadOnly = true;
        g.CanUserAddRows = false;
        g.SelectionMode = DataGridSelectionMode.Single;
        g.HeadersVisibility = DataGridHeadersVisibility.Column;
        g.GridLinesVisibility = DataGridGridLinesVisibility.None;
    }

    void SetupGrid(DataGrid g, bool showPath)
    {
        BasicGrid(g);

        g.Columns.Add(Col("Ad", "DisplayName", new DataGridLength(2, DataGridLengthUnitType.Star)));
        if (showPath) g.Columns.Add(Col("Konum", "ParentPath", new DataGridLength(2, DataGridLengthUnitType.Star)));
        g.Columns.Add(Col("Boyut", "SizeText", new DataGridLength(90), "SizeValue", right: true));
        if (!showPath)
        {
            g.Columns.Add(BarCol("Percent", "SizeValue"));
            g.Columns.Add(Col("%", "PercentText", new DataGridLength(60), "SizeValue", right: true));
        }
        g.Columns.Add(Col("Dosya", "Files", new DataGridLength(70), right: true));
        g.Columns.Add(Col("Son kullanım", "LastUsedText", new DataGridLength(95), "LastUsed"));
        g.Columns.Add(Col("Kullanılmıyor", "UnusedText", new DataGridLength(95), "DaysUnused"));

        var pill = new Style(typeof(TextBlock));
        pill.Setters.Add(new Setter(TextBlock.BackgroundProperty, new Binding("SafetyBrush")));
        pill.Setters.Add(new Setter(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Black));
        pill.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(6, 2, 6, 2)));
        pill.Setters.Add(new Setter(VerticalAlignmentProperty, VerticalAlignment.Center));
        g.Columns.Add(new DataGridTextColumn
        {
            Header = "Durum", Binding = new Binding("SafetyText"), Width = new DataGridLength(115),
            SortMemberPath = "SafetyText", ElementStyle = pill,
        });
        g.Columns.Add(Col("Açıklama", "Note", new DataGridLength(3, DataGridLengthUnitType.Star)));

        g.MouseDoubleClick += (_, _) =>
        {
            if (g.SelectedItem is not Node n) return;
            if (n.IsDir) Navigate(n);
            else if (n.Parent != null)
            {
                FilterBox.SelectedIndex = 0;
                Navigate(n.Parent);
                ExplorerGrid.SelectedItem = n;
                ExplorerGrid.ScrollIntoView(n);
            }
        };

        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("Klasörü aç", () => { if (g.SelectedItem is Node n) OpenFolder(n.IsDir ? n.FullPath : n.ParentPath); }));
        menu.Items.Add(MenuItem("Explorer'da göster", () => Reveal(g)));
        menu.Items.Add(MenuItem("Yolu kopyala", () => { if (g.SelectedItem is Node n) Clipboard.SetText(n.FullPath); }));
        g.ContextMenu = menu;
    }

    static DataGridTemplateColumn BarCol(string valuePath, string sortPath)
    {
        var bar = new FrameworkElementFactory(typeof(ProgressBar));
        bar.SetBinding(System.Windows.Controls.Primitives.RangeBase.ValueProperty, new Binding(valuePath) { Mode = BindingMode.OneWay });
        bar.SetValue(HeightProperty, 10.0);
        bar.SetValue(MarginProperty, new Thickness(4, 0, 4, 0));
        return new DataGridTemplateColumn
        {
            Header = "Oran", Width = new DataGridLength(110), SortMemberPath = sortPath,
            CellTemplate = new DataTemplate { VisualTree = bar },
        };
    }

    static DataGridTextColumn Col(string header, string path, DataGridLength width, string? sort = null, bool right = false)
    {
        var c = new DataGridTextColumn { Header = header, Binding = new Binding(path) { Mode = BindingMode.OneWay }, Width = width, SortMemberPath = sort ?? path };
        var s = new Style(typeof(TextBlock));
        s.Setters.Add(new Setter(VerticalAlignmentProperty, VerticalAlignment.Center));
        if (right)
        {
            s.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
            s.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(0, 0, 6, 0)));
        }
        c.ElementStyle = s;
        return c;
    }

    static MenuItem MenuItem(string header, Action action)
    {
        var m = new MenuItem { Header = header };
        m.Click += (_, _) => action();
        return m;
    }

    // ---------- Actions ----------

    static void Reveal(DataGrid g)
    {
        if (g.SelectedItem is Node n) Process.Start("explorer.exe", $"/select,\"{n.FullPath}\"");
    }

    static void OpenFolder(string path)
    {
        if (Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path}\"");
    }

    // ---------- File types ----------

    void SetupTypesGrid()
    {
        BasicGrid(TypesGrid);
        TypesGrid.Columns.Add(Col("Tür", "Category", new DataGridLength(220)));
        TypesGrid.Columns.Add(Col("Dosya sayısı", "Count", new DataGridLength(100), right: true));
        TypesGrid.Columns.Add(Col("Boyut", "SizeText", new DataGridLength(100), "SizeValue", right: true));
        TypesGrid.Columns.Add(BarCol("Percent", "SizeValue"));
        TypesGrid.Columns.Add(Col("%", "PercentText", new DataGridLength(60), "SizeValue", right: true));
        TypesGrid.Columns.Add(Col("En çok yer kaplayan uzantılar", "TopExtensions", new DataGridLength(1, DataGridLengthUnitType.Star)));
    }

    void Types_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (root == null || TypesGrid.SelectedItem is not TypeRow t) return;
        TypeFilesGrid.ItemsSource = FileTypes.Files(root, t.Category, 500);
        TypeFilesText.Text = $"{t.Category}: en büyük {Math.Min(500, t.Count):N0} dosya";
    }

    // ---------- Projects ----------

    void SetupProjectsGrid()
    {
        BasicGrid(ProjectsGrid);
        ProjectsGrid.Columns.Add(Col("Proje", "Name", new DataGridLength(1.5, DataGridLengthUnitType.Star)));
        ProjectsGrid.Columns.Add(Col("Tür", "Kind", new DataGridLength(130)));
        ProjectsGrid.Columns.Add(Col("Toplam", "SizeText", new DataGridLength(90), "SizeValue", right: true));
        ProjectsGrid.Columns.Add(Col("Yeniden oluşturulabilir", "RebuildText", new DataGridLength(150), "RebuildValue", right: true));
        ProjectsGrid.Columns.Add(Col("Son kullanım", "LastUsedText", new DataGridLength(95), "DaysUnused"));
        ProjectsGrid.Columns.Add(Col("Kullanılmıyor", "UnusedText", new DataGridLength(95), "DaysUnused"));
        ProjectsGrid.Columns.Add(Col("Konum", "FolderPath", new DataGridLength(2, DataGridLengthUnitType.Star)));
        ProjectsGrid.MouseDoubleClick += (_, _) => { if (ProjectsGrid.SelectedItem is ProjectRow p) Navigate(p.Node); };
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("Klasörü aç", () => { if (ProjectsGrid.SelectedItem is ProjectRow p) OpenFolder(p.FolderPath); }));
        menu.Items.Add(MenuItem("Gezgin'de göster", () => { if (ProjectsGrid.SelectedItem is ProjectRow p) Navigate(p.Node); }));
        ProjectsGrid.ContextMenu = menu;
    }

    void Projects_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectsGrid.SelectedItem is not ProjectRow p) return;
        ProjectPartsGrid.ItemsSource = p.Rebuildable.OrderByDescending(n => n.Size).ToList();
        ProjectPartsText.Text = p.Rebuildable.Count == 0
            ? $"{p.Name}: yeniden oluşturulabilir klasör yok."
            : $"{p.Name}: {p.Rebuildable.Count} klasör, {p.RebuildText}. Silerseniz proje bir sonraki derlemede / 'npm install' ile bunları yeniden oluşturur.";
    }

    // ---------- Duplicates ----------

    async void Dup_Click(object sender, RoutedEventArgs e)
    {
        if (root == null) { DupText.Text = "Önce bir tarama yapın."; return; }
        cts = new CancellationTokenSource();
        var token = cts.Token;
        var scanned = root;
        DupBtn.IsEnabled = ScanBtn.IsEnabled = false;
        CancelBtn.IsEnabled = true;
        var clock = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) => DupText.Text = $"Karşılaştırılıyor… {Fmt.Size(Duplicates.BytesRead)} okundu  ·  geçen {Clock(clock.Elapsed)}";
        timer.Start();
        try
        {
            long wasted = 0; int groups = 0;
            var list = await Task.Run(() => Duplicates.Find(scanned, token, out wasted, out groups));
            DupGrid.ItemsSource = list;
            DupText.Text = (groups == 0 ? "Kopya dosya bulunamadı."
                : $"{groups} grup, {list.Count} dosya. Her gruptan biri dışındakiler silinirse {Fmt.Size(wasted)} açılır.")
                + $"  (süre {Clock(clock.Elapsed)})";
        }
        catch (OperationCanceledException) { DupText.Text = "Durduruldu."; }
        finally
        {
            timer.Stop();
            DupBtn.IsEnabled = ScanBtn.IsEnabled = true;
            CancelBtn.IsEnabled = false;
        }
    }

    // ---------- Installed programs ----------

    void SetupAppsGrid()
    {
        BasicGrid(AppsGrid);
        AppsGrid.Columns.Add(Col("Program", "Name", new DataGridLength(2, DataGridLengthUnitType.Star)));
        AppsGrid.Columns.Add(Col("Yayıncı", "Publisher", new DataGridLength(1, DataGridLengthUnitType.Star)));
        AppsGrid.Columns.Add(Col("Boyut", "SizeText", new DataGridLength(90), "SizeValue", right: true));
        AppsGrid.Columns.Add(Col("Kurulum", "InstalledText", new DataGridLength(90), "Installed"));
        AppsGrid.Columns.Add(Col("Son kullanım", "LastUsedText", new DataGridLength(95), "LastUsed"));
        AppsGrid.Columns.Add(Col("Kullanılmıyor", "UnusedText", new DataGridLength(95), "DaysUnused"));
        AppsGrid.Columns.Add(Col("Konum", "Location", new DataGridLength(2, DataGridLengthUnitType.Star)));
        AppsGrid.MouseDoubleClick += (_, _) => { if (AppsGrid.SelectedItem is InstalledApp a) OpenFolder(a.Location); };
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("Klasörü aç", () => { if (AppsGrid.SelectedItem is InstalledApp a) OpenFolder(a.Location); }));
        menu.Items.Add(MenuItem("Ayarlar > Uygulamalar'ı aç", OpenAppsSettings));
        AppsGrid.ContextMenu = menu;
    }

    static void OpenAppsSettings() => Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true });
    void OpenAppsSettings_Click(object sender, RoutedEventArgs e) => OpenAppsSettings();

    // ---------- History ----------

    void SetupChangeGrid()
    {
        BasicGrid(ChangeGrid);
        ChangeGrid.Columns.Add(Col("Klasör", "FolderPath", new DataGridLength(3, DataGridLengthUnitType.Star)));
        ChangeGrid.Columns.Add(Col("Önceki", "BeforeText", new DataGridLength(100), "Before", right: true));
        ChangeGrid.Columns.Add(Col("Şimdi", "AfterText", new DataGridLength(100), "After", right: true));
        ChangeGrid.Columns.Add(Col("Fark", "DiffText", new DataGridLength(110), "Diff", right: true));
        ChangeGrid.MouseDoubleClick += (_, _) =>
        {
            if (ChangeGrid.SelectedItem is ChangeRow r && root != null && Scanner.Find(root, r.FolderPath) is { } n) Navigate(n);
        };
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("Klasörü aç", () => { if (ChangeGrid.SelectedItem is ChangeRow r) OpenFolder(r.FolderPath); }));
        ChangeGrid.ContextMenu = menu;
    }

    void ShowHistory(string location)
    {
        var all = History.LoadAll(location);
        Chart.SetData(all.Select(s => (s.Date, s.Total)).ToList());
        if (all.Count < 2)
        {
            ChangeText.Text = "Bu konumun ilk taraması kaydedildi. Bir sonraki taramada değişimler burada görünecek.";
            ChangeGrid.ItemsSource = null;
            return;
        }
        var before = all[^2];
        var now = all[^1];
        long diff = now.Total - before.Total;
        ChangeText.Text = $"Son iki tarama: {before.Date:dd.MM.yyyy HH:mm} → {now.Date:dd.MM.yyyy HH:mm}  —  toplam {(diff >= 0 ? "+" : "−")}{Fmt.Size(Math.Abs(diff))}  " +
                          $"({Fmt.Size(before.Total)} → {Fmt.Size(now.Total)}). 10 MB'tan büyük değişimler:";
        ChangeGrid.ItemsSource = History.Compare(before, now);
    }

    // ---------- CSV ----------

    void Export_Click(object sender, RoutedEventArgs e)
    {
        var grid = FindGrid(Tabs.SelectedContent as DependencyObject);
        if (grid == null || grid.Items.Count == 0) { StatusText.Text = "Bu sekmede aktarılacak liste yok."; return; }
        var header = (Tabs.SelectedItem as TabItem)?.Header?.ToString()?.Trim() ?? "liste";
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV (Excel)|*.csv",
            FileName = $"DiskRadar - {header} - {DateTime.Now:yyyy-MM-dd}.csv",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            int n = CsvExport.Write(grid, dlg.FileName);
            StatusText.Text = $"{n} satır kaydedildi: {dlg.FileName}";
        }
        catch (Exception ex) { StatusText.Text = "Kaydedilemedi: " + ex.Message; }
    }

    static DataGrid? FindGrid(DependencyObject? d)
    {
        if (d == null) return null;
        if (d is DataGrid g) return g;
        foreach (var child in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>())
            if (FindGrid(child) is { } found) return found;
        return null;
    }
}
