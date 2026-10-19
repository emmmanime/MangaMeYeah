using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace MangaMeYeah.Models;

public sealed class ThumbnailItemViewModel : INotifyPropertyChanged
{
    private BitmapSource? _thumbnailBitmap;
    private bool _isSelected;
    private bool _isCurrentPage;

    public required int PageIndex { get; init; }
    public int DisplayPageNumber => PageIndex + 1;
    public required string FilePath { get; init; }
    public string FileName
    {
        get
        {
            int pipeIdx = FilePath.IndexOf('|');
            return pipeIdx >= 0 ? Path.GetFileName(FilePath[(pipeIdx + 1)..]) : Path.GetFileName(FilePath);
        }
    }

    public BitmapSource? ThumbnailBitmap
    {
        get => _thumbnailBitmap;
        set
        {
            if (!ReferenceEquals(_thumbnailBitmap, value))
            {
                _thumbnailBitmap = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsCurrentPage
    {
        get => _isCurrentPage;
        set
        {
            if (_isCurrentPage != value)
            {
                _isCurrentPage = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
