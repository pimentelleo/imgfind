using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
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
            ImageCount.Text = $"{Images.Count} {(Images.Count == 1 ? "imagem" : "imagens")} na biblioteca";
            var selected = Images.Count(x => x.IsChecked);
            SelectionCount.Text = $"{selected} {(selected == 1 ? "selecionada" : "selecionadas")}";
            EmptyState.Visibility = Images.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyTitle.Text = scanCompleted ? "Nenhuma imagem encontrada" : "Sua biblioteca começa aqui";
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
        Preview.Source = null;
        PreviewPlaceholder.Visibility = Visibility.Visible;
        PreviewName.Text = "Nenhuma imagem selecionada";
        PreviewText.Text = "Clique em um arquivo para visualizar.";
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
            Status.Text = "Clique em Varrer imagens para carregar a pasta.";
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
        try
        {
            var result = await Task.Run(() => ImageOperations.Scan(root));
            scanCompleted = true;
            ClearImages();
            ScheduleSummaryUpdate();
            foreach (var path in result.Images)
                Images.Add(new ImageItem { Path = path });
            ClearPreview();
            Status.Text = Images.Count == 1 ? "1 imagem encontrada." : $"{Images.Count} imagens encontradas.";
            if (result.Errors.Count > 0)
                ShowReport("A varredura terminou com erros", "Algumas pastas ou arquivos não puderam ser lidos:", result.Errors);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Status.Text = "Não foi possível concluir a varredura.";
            ShowNotice("Erro na varredura", ex.Message, error: true);
        }
        finally { SetBusy(false); }
    }

    private void ImageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Preview.Source = null;
        if (ImageList.SelectedItem is not ImageItem item)
        {
            ClearPreview();
            return;
        }
        PreviewName.Text = item.FileName;
        PreviewText.Text = item.Path;
        PreviewPlaceholder.Visibility = Visibility.Collapsed;
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
            ShowNotice("Nenhuma imagem para mover", "Varra a pasta de origem e selecione imagens, se necessário.");
            return;
        }
        if (string.IsNullOrWhiteSpace(DestinationPath.Text))
        {
            ShowNotice("Escolha uma pasta de destino", "Escolha onde deseja reunir as imagens antes de continuar.");
            return;
        }
        var destination = DestinationPath.Text;
        SetBusy(true, "Conferindo conteúdo e duplicatas...");
        try
        {
            var plan = await Task.Run(() => ImageOperations.Prepare(paths, destination));
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
                         $"Nenhuma duplicata foi encontrada. Mover {paths.Length} {(paths.Length == 1 ? "imagem" : "imagens")} para:\n\n{destination}?",
                         confirm: true))
            {
                Status.Text = "Movimentação cancelada; nenhum arquivo foi movido.";
                return;
            }

            Status.Text = "Movendo imagens...";
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
        finally { SetBusy(false); }
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
        if (!busy) return;
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
