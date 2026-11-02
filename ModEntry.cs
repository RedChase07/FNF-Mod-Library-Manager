using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace FnFModLibrary_WinUI;

public sealed class FavoriteGlyphConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? "★" : "☆";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class ModEntry : INotifyPropertyChanged
{
    private string _path = "";
    private string _relativePath = "";
    private int _playCount;
    private bool _isFavorite;
    private List<string> _tags = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Path
    {
        get => _path;
        set
        {
            if (SetProperty(ref _path, value))
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(SearchText));
            }
        }
    }

    public string RelativePath
    {
        get => _relativePath;
        set
        {
            if (SetProperty(ref _relativePath, value))
            {
                OnPropertyChanged(nameof(SearchText));
            }
        }
    }
    public long FileSize { get; set; }
    public long LastWriteUtcTicks { get; set; }
    public int PlayCount
    {
        get => _playCount;
        set
        {
            if (SetProperty(ref _playCount, value))
            {
                OnPropertyChanged(nameof(PlayCountText));
            }
        }
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetProperty(ref _isFavorite, value);
    }

    public List<string> Tags
    {
        get => _tags;
        set
        {
            if (SetProperty(ref _tags, value ?? []))
            {
                OnPropertyChanged(nameof(TagSummary));
                OnPropertyChanged(nameof(SearchText));
            }
        }
    }

    [JsonIgnore]
    public string DisplayName => System.IO.Path.GetFileNameWithoutExtension(Path);

    [JsonIgnore]
    public string PlayCountText => $"Play count: {PlayCount}";

    [JsonIgnore]
    public string TagSummary => string.Join(", ", Tags);

    [JsonIgnore]
    public string SearchText =>
        $"{DisplayName} {RelativePath} {string.Join(" ", Tags)}".ToUpperInvariant();

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class LibraryState
{
    public string? LastFolder { get; set; }
    public bool ConfirmLaunch { get; set; } = true;
    public int CardColumns { get; set; } = 2;
    public string CardStyle { get; set; } = "Compact";
    public string Theme { get; set; } = "Ocean";
    public bool UseCustomAccentColor { get; set; }
    public string CustomAccentColor { get; set; } = "#54B8E8";
    public bool UseCustomColors { get; set; }
    public Dictionary<string, string> CustomColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> IgnoredPaths { get; set; } = [];
    public List<ModEntry> Mods { get; set; } = [];
}
