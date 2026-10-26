using System.ComponentModel;
using System.Windows.Media;

namespace Tomodachi.Launcher;

/// <summary>Eine Zeile in der Trefferliste. Das Icon kommt asynchron nach.</summary>
internal sealed class AppResult(AppEntry app) : INotifyPropertyChanged
{
    private ImageSource? _icon;

    public AppEntry App { get; } = app;
    public string Name => App.Name;

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (ReferenceEquals(_icon, value)) return;
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
