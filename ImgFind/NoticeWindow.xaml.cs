using System.Windows;

namespace ImgFind;

public partial class NoticeWindow : Window
{
    public NoticeWindow(string title, string message, bool confirm, bool error)
    {
        InitializeComponent();
        Title = title + " — ImgFind";
        Heading.Text = title;
        Message.Text = message;
        Eyebrow.Text = error ? "ATENÇÃO" : confirm ? "CONFIRMAR MOVIMENTAÇÃO" : "IMGFIND";
        CancelButton.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;
        AcceptButton.Content = confirm ? "Confirmar e mover" : "Entendi";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Accept_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
