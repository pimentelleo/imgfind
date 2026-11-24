using System.Windows;

namespace ImgFind;

public partial class ReportWindow : Window
{
    public ReportWindow(string title, string summary, string details)
    {
        InitializeComponent();
        Title = title;
        Summary.Text = summary;
        Details.Text = details;
    }
}
