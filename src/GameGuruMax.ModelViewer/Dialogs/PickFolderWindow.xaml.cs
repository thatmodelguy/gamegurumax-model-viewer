using System.Windows;
using System.Windows.Controls;
using GameGuruMax.ModelViewer.Models;

namespace GameGuruMax.ModelViewer.Dialogs;

public partial class PickFolderWindow : Window
{
    private readonly FolderNode _root;
    private readonly Action<string, string>? _createFolder;

    public string? SelectedRelativePath { get; private set; }
    public string? CreatedFolderRelativePath { get; private set; }

    public PickFolderWindow(FolderNode root, Action<string, string>? createFolder = null)
    {
        InitializeComponent();
        _root = root;
        _createFolder = createFolder;
        FolderTree.Items.Add(BuildItem(root));
        if (FolderTree.Items[0] is TreeViewItem first)
            first.IsExpanded = true;
    }

    private static TreeViewItem BuildItem(FolderNode node)
    {
        var item = new TreeViewItem
        {
            Header = string.IsNullOrEmpty(node.Name) ? "(entitybank root)" : node.Name,
            Tag = node,
            IsExpanded = node.RelativePath.Length < 40
        };
        foreach (var child in node.Children)
            item.Items.Add(BuildItem(child));
        return item;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (FolderTree.SelectedItem is TreeViewItem { Tag: FolderNode node })
        {
            SelectedRelativePath = node.RelativePath;
            DialogResult = true;
            Close();
            return;
        }
        MessageBox.Show(this, "Select a destination folder.", "Move", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CreateFolder_Click(object sender, RoutedEventArgs e)
    {
        var name = NewFolderNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Enter a folder name.", "Create folder", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (FolderTree.SelectedItem is not TreeViewItem { Tag: FolderNode parent })
        {
            MessageBox.Show(this, "Select a parent folder first.", "Create folder", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            _createFolder?.Invoke(parent.RelativePath, name);
            var childRel = string.IsNullOrEmpty(parent.RelativePath) ? name : parent.RelativePath + "\\" + name;
            var child = new FolderNode { Name = name, RelativePath = childRel };
            parent.Children.Add(child);
            var childItem = BuildItem(child);
            ((TreeViewItem)FolderTree.SelectedItem).Items.Add(childItem);
            ((TreeViewItem)FolderTree.SelectedItem).IsExpanded = true;
            childItem.IsSelected = true;
            CreatedFolderRelativePath = childRel;
            NewFolderNameBox.Clear();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Create folder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
