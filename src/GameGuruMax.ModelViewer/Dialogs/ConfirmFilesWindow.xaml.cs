using System.Windows;

namespace GameGuruMax.ModelViewer.Dialogs;

public partial class ConfirmFilesWindow : Window
{
    public ConfirmFilesWindow(string title, string message, IEnumerable<string> files, IEnumerable<string>? sharedSkipped = null)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        foreach (var f in files)
            FilesList.Items.Add(f);

        var skipped = sharedSkipped?.ToList() ?? new List<string>();
        if (skipped.Count > 0)
        {
            SharedText.Text = "Shared files that will NOT be deleted (" + skipped.Count + "):\n"
                              + string.Join("\n", skipped.Take(20))
                              + (skipped.Count > 20 ? "\n..." : "");
        }
        else
        {
            SharedText.Visibility = Visibility.Collapsed;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
