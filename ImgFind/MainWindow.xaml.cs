using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ImgFind;

public sealed class ImageItem : INotifyPropertyChanged
{
    private bool isChecked;
    public required string Path { get; init; }
    public string FileName => System.IO.Path.GetFileName(Path);
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? "";
    public string Extension => System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();
    public override string ToString() => FileName;
    public bool IsChecked
    {
        get => isChecked;
        set { if (isChecked != value) { isChecked = value; OnPropertyChanged(); } }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class MainWindow : Window
{
    public ObservableCollection<ImageItem> Images { get; } = new();
    private bool busy;
    private bool summaryUpdatePending;
    private bool scanCompleted;
    private MediaElement? video;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Images.CollectionChanged += Images_CollectionChanged;
    }

    private void Images_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (ImageItem item in e.NewItems) item.PropertyChanged += ImageItem_PropertyChanged;
        if (e.OldItems != null)
            foreach (ImageItem item in e.OldItems) item.PropertyChanged -= ImageItem_PropertyChanged;
        ScheduleSummaryUpdate();
    }

    private void ImageItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImageItem.IsChecked)) ScheduleSummaryUpdate();
    }

    private void ScheduleSummaryUpdate()
    {
        if (summaryUpdatePending) return;
        summaryUpdatePending = true;
        Dispatcher.BeginInvoke(() =>
        {
            summaryUpdatePending = false;
            ImageCount.Text = $"{Images.Count} {(Images.Count == 1 ? "arquivo" : "arquivos")} na biblioteca";
            var selected = Images.Count(x => x.IsChecked);
            SelectionCount.Text = $"{selected} {(selected == 1 ? "selecionado" : "selecionados")}";
            EmptyState.Visibility = Images.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyTitle.Text = scanCompleted ? "Nenhuma foto ou vídeo encontrado" : "Sua biblioteca começa aqui";
            EmptyDescription.Text = scanCompleted ? "Tente outra pasta ou confira os formatos suportados." :
                "Escolha uma pasta de origem e inicie a varredura.";
        });
    }

    private void ClearImages()
    {
        foreach (var item in Images) item.PropertyChanged -= ImageItem_PropertyChanged;
        Images.Clear();
    }

    private void ClearPreview()
    {
        CloseVideo();
        Preview.Source = null;
        PreviewPlaceholder.Visibility = Visibility.Visible;
        PreviewName.Text = "Nenhum arquivo selecionado";
        PreviewText.Text = "Clique em um arquivo para visualizar.";
    }

    private void CloseVideo()
    {
        var old = video;
        video = null;
        if (old != null)
        {
            old.Stop();
            old.Close();
            PreviewSurface.Children.Remove(old);
        }
        VideoControls.Visibility = Visibility.Collapsed;
    }

    private void BrowseSource_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Escolha a pasta de origem" };
        if (dialog.ShowDialog(this) == true)
        {
            SourcePath.Text = dialog.FolderName;
            scanCompleted = false;
            ClearImages();
            ScheduleSummaryUpdate();
            ClearPreview();
            Status.Text = "Clique em Varrer arquivos para carregar a pasta.";
        }
    }

    private void BrowseDestination_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Escolha a pasta de destino" };
        if (dialog.ShowDialog(this) == true)
            DestinationPath.Text = dialog.FolderName;
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SourcePath.Text))
        {
            ShowNotice("Escolha uma pasta de origem", "Para começar, escolha a pasta que deseja varrer.");
            return;
        }
        var root = SourcePath.Text;
        SetBusy(true, "Varrendo pastas...");
        ScanProgressPanel.Visibility = Visibility.Visible;
        IndeterminateProgress.Visibility = Visibility.Visible;
        DeterminateProgress.Visibility = Visibility.Collapsed;
        ScanProgressTitle.Text = "Varrendo pastas...";
        ScanProgressDetails.Text = "0 pastas iniciadas · 0 arquivos examinados · 0 arquivos encontrados · 0 erros";
        ScanCurrentFolder.Text = root;
        ScanElapsed.Text = "Tempo: 00:00";
        if (Images.Count == 0)
        {
            EmptyTitle.Text = "Procurando fotos e vídeos...";
            EmptyDescription.Text = "Aguarde enquanto examinamos a pasta e suas subpastas.";
        }
        var clock = Stopwatch.StartNew();
        ScanProgress? latest = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) =>
        {
            var elapsed = clock.Elapsed;
            ScanElapsed.Text = $"Tempo: {(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
            var progress = Volatile.Read(ref latest);
            if (progress == null) return;
            ScanCurrentFolder.Text = progress.CurrentDirectory;
            ScanProgressDetails.Text = $"{progress.DirectoriesVisited} pastas iniciadas · {progress.FilesExamined} arquivos examinados · " +
                                       $"{progress.ImagesFound} arquivos encontrados · {progress.Errors} erros";
        };
        timer.Start();
        try
        {
            var result = await Task.Run(() => ImageOperations.Scan(root, progress => Volatile.Write(ref latest, progress)));
            timer.Stop();
            ScanProgressTitle.Text = "Preparando a lista de arquivos...";
            ScanProgressDetails.Text = $"{result.Images.Count} arquivos encontrados · {result.Errors.Count} erros";
            scanCompleted = true;
            ClearImages();
            ScheduleSummaryUpdate();
            for (var i = 0; i < result.Images.Count; i++)
            {
                Images.Add(new ImageItem { Path = result.Images[i] });
                if ((i + 1) % 200 == 0)
                    await Dispatcher.Yield(DispatcherPriority.Background);
            }
            ClearPreview();
            Status.Text = Images.Count == 1 ? "1 arquivo encontrado." : $"{Images.Count} arquivos encontrados.";
            if (result.Errors.Count > 0)
                ShowReport("A varredura terminou com erros", "Algumas pastas ou arquivos não puderam ser lidos:", result.Errors);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Status.Text = "Não foi possível concluir a varredura.";
            ShowNotice("Erro na varredura", ex.Message, error: true);
        }
        finally
        {
            timer.Stop();
            ScanProgressPanel.Visibility = Visibility.Collapsed;
            ScheduleSummaryUpdate();
            SetBusy(false);
        }
    }

    private void ImageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CloseVideo();
        Preview.Source = null;
        if (ImageList.SelectedItem is not ImageItem item)
        {
            ClearPreview();
            return;
        }
        PreviewName.Text = item.FileName;
        PreviewText.Text = item.Path;
        PreviewPlaceholder.Visibility = Visibility.Collapsed;
        if (ImageOperations.IsVideo(item.Path))
        {
            OpenVideo(item.Path);
            return;
        }
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 1200;
            image.UriSource = new Uri(item.Path);
            image.EndInit();
            image.Freeze();
            Preview.Source = image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or NotSupportedException or FileFormatException or ArgumentException)
        {
            PreviewText.Text = $"Não foi possível visualizar: {item.Path}\n{ex.Message}";
        }
    }

    private void OpenVideo(string path)
    {
        var player = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(12),
            Volume = 0.5
        };
        video = player;
        VideoControls.Visibility = Visibility.Visible;
        PlayVideoButton.IsEnabled = true;
        PauseVideoButton.IsEnabled = false;
        RestartVideoButton.IsEnabled = false;
        PreviewText.Text = $"Clique em Reproduzir para visualizar o vídeo.\n{path}";
        player.MediaOpened += (_, _) =>
        {
            if (video != player) return;
            PreviewText.Text = path;
            RestartVideoButton.IsEnabled = player.NaturalDuration.HasTimeSpan;
        };
        player.MediaEnded += (_, _) =>
        {
            if (video != player) return;
            player.Stop();
            PlayVideoButton.IsEnabled = true;
            PauseVideoButton.IsEnabled = false;
        };
        player.MediaFailed += (_, args) =>
        {
            if (video != player) return;
            var message = args.ErrorException?.Message ?? "Erro de reprodução desconhecido.";
            CloseVideo();
            PreviewText.Text = $"Não foi possível reproduzir este vídeo. O formato ou codec pode não ser suportado pelo Windows.\n{message}";
        };
        PreviewSurface.Children.Add(player);
        try { player.Source = new Uri(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            CloseVideo();
            PreviewText.Text = $"Não foi possível abrir o vídeo: {ex.Message}";
        }
    }

    private void PlayVideo_Click(object sender, RoutedEventArgs e)
    {
        if (video == null) return;
        video.Play();
        PlayVideoButton.IsEnabled = false;
        PauseVideoButton.IsEnabled = true;
    }

    private void PauseVideo_Click(object sender, RoutedEventArgs e)
    {
        if (video == null) return;
        video.Pause();
        PlayVideoButton.IsEnabled = true;
        PauseVideoButton.IsEnabled = false;
    }

    private void RestartVideo_Click(object sender, RoutedEventArgs e)
    {
        if (video == null) return;
        video.Stop();
        video.Play();
        PlayVideoButton.IsEnabled = false;
        PauseVideoButton.IsEnabled = true;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var image in Images) image.IsChecked = true;
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var image in Images) image.IsChecked = false;
    }

    private async void MoveSelected_Click(object sender, RoutedEventArgs e) =>
        await MoveAsync(Images.Where(x => x.IsChecked).Select(x => x.Path).ToArray());

    private async void MoveAll_Click(object sender, RoutedEventArgs e) =>
        await MoveAsync(Images.Select(x => x.Path).ToArray());

    private async Task MoveAsync(string[] paths)
    {
        if (busy) return;
        if (paths.Length == 0)
        {
            ShowNotice("Nenhum arquivo para mover", "Varra a pasta de origem e selecione arquivos, se necessário.");
            return;
        }
        if (string.IsNullOrWhiteSpace(DestinationPath.Text))
        {
            ShowNotice("Escolha uma pasta de destino", "Escolha onde deseja reunir os arquivos antes de continuar.");
            return;
        }
        var destination = DestinationPath.Text;
        ImageList.SelectedItem = null;
        ClearPreview();
        SetBusy(true, "Conferindo conteúdo e duplicatas...");
        ScanProgressPanel.Visibility = Visibility.Visible;
        ScanProgressTitle.Text = "Conferindo arquivos de origem...";
        ScanProgressDetails.Text = $"0 de {paths.Length} arquivos · 0% desta etapa";
        ScanCurrentFolder.Text = "Preparando...";
        ScanElapsed.Text = "Tempo: 00:00";
        IndeterminateProgress.Visibility = Visibility.Collapsed;
        DeterminateProgress.Visibility = Visibility.Visible;
        DeterminateProgress.Value = 0;
        var clock = Stopwatch.StartNew();
        PrepareProgress? latest = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) =>
        {
            var elapsed = clock.Elapsed;
            ScanElapsed.Text = $"Tempo: {(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
            var progress = Volatile.Read(ref latest);
            if (progress == null) return;
            ScanCurrentFolder.Text = string.IsNullOrEmpty(progress.CurrentPath) ? destination : progress.CurrentPath;
            ScanProgressTitle.Text = progress.Phase switch
            {
                "origem" => "Conferindo arquivos de origem...",
                "listar destino" => "Examinando arquivos no destino...",
                "destino" => "Conferindo possíveis duplicatas no destino...",
                _ => "Agrupando duplicatas..."
            };
            if (progress.Total is int total)
            {
                var fraction = progress.CurrentSize > 0
                    ? Math.Min(1.0, (double)progress.BytesRead / progress.CurrentSize) : 0;
                var percent = total == 0 ? 100 : Math.Min(100, (progress.Completed + fraction) * 100 / total);
                if (progress.Completed < total) percent = Math.Min(99.9, percent);
                ScanProgressDetails.Text = $"{progress.Completed} de {total} arquivos concluídos · {percent:F1}% desta etapa";
                DeterminateProgress.Value = percent;
                DeterminateProgress.Visibility = Visibility.Visible;
                IndeterminateProgress.Visibility = Visibility.Collapsed;
            }
            else
            {
                ScanProgressDetails.Text = progress.Phase == "listar destino"
                    ? $"{progress.Completed} arquivos examinados · total ainda desconhecido"
                    : "Comparando conteúdos identificados...";
                IndeterminateProgress.Visibility = Visibility.Visible;
                DeterminateProgress.Visibility = Visibility.Collapsed;
            }
        };
        timer.Start();
        var progressShownAt = Stopwatch.StartNew();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.Render);
            var plan = await Task.Run(() => ImageOperations.Prepare(paths, destination, progress => Volatile.Write(ref latest, progress)));
            timer.Stop();
            ScanProgressTitle.Text = "Conferência concluída";
            ScanProgressDetails.Text = $"100% da conferência · {plan.Sources.Count} arquivos · {plan.Duplicates.Count} grupos de duplicatas";
            ScanCurrentFolder.Text = "Pronto para escolher como tratar as duplicatas.";
            DeterminateProgress.Value = 100;
            DeterminateProgress.Visibility = Visibility.Visible;
            IndeterminateProgress.Visibility = Visibility.Collapsed;
            await Task.Delay(Math.Max(0, 800 - (int)progressShownAt.ElapsedMilliseconds));
            ScanProgressPanel.Visibility = Visibility.Collapsed;
            bool excludeDuplicates = false;
            if (plan.Duplicates.Count > 0)
            {
                var dialog = new DuplicatesWindow(plan.Duplicates) { Owner = this };
                if (dialog.ShowDialog() != true)
                {
                    Status.Text = "Movimentação cancelada; nenhum arquivo foi movido.";
                    return;
                }
                excludeDuplicates = dialog.ExcludeDuplicates;
            }
            else if (!ShowNotice("Tudo pronto para mover",
                         $"Nenhuma duplicata foi encontrada. Mover {paths.Length} {(paths.Length == 1 ? "arquivo" : "arquivos")} para subpastas por extensão em:\n\n{destination}?",
                         confirm: true))
            {
                Status.Text = "Movimentação cancelada; nenhum arquivo foi movido.";
                return;
            }

            Status.Text = "Movendo arquivos...";
            var result = await Task.Run(() => ImageOperations.Move(plan, excludeDuplicates));
            var movedPaths = result.Moved.Select(x => x.Source)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var item in Images.Where(x => movedPaths.Contains(x.Path)).ToList())
                Images.Remove(item);
            Status.Text = $"Movidas: {result.Moved.Count}. Não movidas: {result.Skipped.Count}. Erros: {result.Errors.Count}.";
            var lines = result.Moved.Select(x => $"Movida: {x.Source} → {x.Destination}")
                .Concat(result.Skipped.Select(x => "Não movida: " + x))
                .Concat(result.Errors.Select(x => "Erro: " + x)).ToList();
            ShowReport("Resultado da movimentação", Status.Text, lines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Status.Text = "A conferência falhou; nenhum arquivo foi movido.";
            ShowNotice("Não foi possível continuar", $"{Status.Text}\n\n{ex.Message}", error: true);
        }
        finally
        {
            timer.Stop();
            ScanProgressPanel.Visibility = Visibility.Collapsed;
            SetBusy(false);
        }
    }

    private void SetBusy(bool value, string? message = null)
    {
        busy = value;
        StatusLabel.Text = value ? "OPERAÇÃO EM ANDAMENTO" : "PRONTO PARA ORGANIZAR";
        BrowseSource.IsEnabled = BrowseDestination.IsEnabled = ScanButton.IsEnabled =
            SelectAllButton.IsEnabled = ClearButton.IsEnabled = MoveSelectedButton.IsEnabled =
            MoveAllButton.IsEnabled = ImageList.IsEnabled = !value;
        if (message != null) Status.Text = message;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!busy)
        {
            CloseVideo();
            return;
        }
        e.Cancel = true;
        ShowNotice("Operação em andamento", "Aguarde a operação terminar antes de fechar o aplicativo.");
    }

    private bool ShowNotice(string title, string message, bool confirm = false, bool error = false)
    {
        var dialog = new NoticeWindow(title, message, confirm, error) { Owner = this };
        return dialog.ShowDialog() == true;
    }

    private void ShowReport(string title, string summary, IEnumerable<string> lines)
    {
        var window = new ReportWindow(title, summary, string.Join(Environment.NewLine, lines)) { Owner = this };
        window.ShowDialog();
    }
}
