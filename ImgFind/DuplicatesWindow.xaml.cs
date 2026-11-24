using System.Windows;

namespace ImgFind;

public partial class DuplicatesWindow : Window
{
    public bool ExcludeDuplicates { get; private set; }

    public DuplicatesWindow(IReadOnlyList<DuplicateGroup> groups)
    {
        InitializeComponent();
        Details.Text = string.Join(Environment.NewLine + Environment.NewLine,
            groups.Select((group, index) => $"Grupo {index + 1}:\n" +
                string.Join("\n", group.Sources.Select(path => "  Origem: " + path)
                    .Concat(group.ExistingFiles.Select(path => "  Já no destino: " + path)))));
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Keep_Click(object sender, RoutedEventArgs e) { ExcludeDuplicates = false; DialogResult = true; }
    private void Exclude_Click(object sender, RoutedEventArgs e) { ExcludeDuplicates = true; DialogResult = true; }
}
