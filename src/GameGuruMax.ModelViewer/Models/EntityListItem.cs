using System.ComponentModel;
using System.Runtime.CompilerServices;
using GameGuruMax.ModelViewer.Core;

namespace GameGuruMax.ModelViewer.Models;

/// <summary>
/// Row wrapper so the models grid can bind a checkbox without mutating Core records.
/// </summary>
public sealed class EntityListItem : INotifyPropertyChanged
{
    private bool _isChecked;

    public EntityListItem(EntityRecord entity, bool isChecked = false)
    {
        Entity = entity;
        _isChecked = isChecked;
    }

    public EntityRecord Entity { get; }

    public string DisplayName => Entity.DisplayName;
    public string FileName => Entity.FileName;
    public string FolderRelative => Entity.FolderRelative;
    public int AssociatedFileCount => Entity.AssociatedFiles.Count;
    public string FpePath => Entity.FpePath;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
                return;
            _isChecked = value;
            OnPropertyChanged();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? CheckedChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
