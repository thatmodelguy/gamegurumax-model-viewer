using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GameGuruMax.ModelViewer.Models;

public sealed class FpeMapField : INotifyPropertyChanged
{
    private string _value = "";

    public string Key { get; init; } = "";

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value)
                return;
            _value = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
