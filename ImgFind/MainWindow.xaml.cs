using System.Collections.ObjectModel;
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

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void BrowseSource_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Escolha a pasta de origem" };
        if (dialog.ShowDialog(this) == true)
        {
            SourcePath.Text = dialog.FolderName;
            Images.Clear();
            Preview.Source = null;
            PreviewText.Text = "Selecione um arquivo para visualizar.";
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
            MessageBox.Show(this, "Escolha uma pasta de origem.", "ImgFind", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var root = SourcePath.Text;
        SetBusy(true, "Varrendo pastas...");
        try
        {
            var result = await Task.Run(() => ImageOperations.Scan(root));
            Images.Clear();
            foreach (var path in result.Images)
                Images.Add(new ImageItem { Path = path });
            Preview.Source = null;
            PreviewText.Text = "Selecione um arquivo para visualizar.";
            Status.Text = $"{Images.Count} imagem(ns) encontrada(s).";
            if (result.Errors.Count > 0)
                ShowReport("A varredura terminou com erros", "Algumas pastas ou arquivos não puderam ser lidos:", result.Errors);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Status.Text = "Não foi possível concluir a varredura.";
            MessageBox.Show(this, ex.Message, "Erro na varredura", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
    }

    private void ImageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Preview.Source = null;
        if (ImageList.SelectedItem is not ImageItem item)
        {
            PreviewText.Text = "Selecione um arquivo para visualizar.";
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
            PreviewText.Text = item.Path;
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
            MessageBox.Show(this, "Não há imagens para mover. Varra a origem e selecione imagens, se necessário.",
                "ImgFind", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(DestinationPath.Text))
        {
            MessageBox.Show(this, "Escolha uma pasta de destino.", "ImgFind", MessageBoxButton.OK, MessageBoxImage.Information);
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
            else if (MessageBox.Show(this, $"Mover {paths.Length} imagem(ns) para:\n{destination}?",
                         "Confirmar movimentação", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
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
            MessageBox.Show(this, $"{Status.Text}\n\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool value, string? message = null)
    {
        busy = value;
        BrowseSource.IsEnabled = BrowseDestination.IsEnabled = ScanButton.IsEnabled =
            SelectAllButton.IsEnabled = ClearButton.IsEnabled = MoveSelectedButton.IsEnabled =
            MoveAllButton.IsEnabled = ImageList.IsEnabled = !value;
        if (message != null) Status.Text = message;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!busy) return;
        e.Cancel = true;
        MessageBox.Show(this, "Aguarde a operação terminar antes de fechar o aplicativo.",
            "Operação em andamento", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowReport(string title, string summary, IEnumerable<string> lines)
    {
        var window = new ReportWindow(title, summary, string.Join(Environment.NewLine, lines)) { Owner = this };
        window.ShowDialog();
    }
}
