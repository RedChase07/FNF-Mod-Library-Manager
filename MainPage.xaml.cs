using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using WinRT.Interop;

namespace FnFModLibrary_WinUI;

public sealed partial class MainPage : Page
{
    private readonly LibraryStore _store = new();
    private readonly ObservableCollection<ModEntry> _allMods = [];
    public ObservableCollection<ModEntry> FilteredMods { get; } = [];
    private readonly HashSet<string> _ignoredPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapImage> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private LibraryState _state = new();
    private string? _libraryFolder;
    private bool _initializing;
    private bool _scanning;

    public MainPage()
    {
        _initializing = true;
        InitializeComponent();
        Loaded += MainPage_Loaded;
        RootGrid.RequestedTheme = ElementTheme.Dark;
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        _initializing = true;
        try
        {
            _state = _store.Load();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            SetStatus($"Could not load saved library data: {error.Message}", isError: true);
            _state = new LibraryState();
        }

        _state.CustomColors ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _state.CardColumns = Math.Clamp(_state.CardColumns, 1, 6);
        _state.CardStyle = _state.CardStyle is "Compact" or "Large" ? _state.CardStyle : "Compact";
        _state.Theme = "Ocean";
        _ignoredPaths.Clear();
        _ignoredPaths.UnionWith(_state.IgnoredPaths);
        _libraryFolder = _state.LastFolder;
        ApplyTheme();
        RefreshTagFilter();
        if (_libraryFolder is not null && Directory.Exists(_libraryFolder))
        {
            FolderLabel.Text = _libraryFolder;
            _allMods.Clear();
            foreach (var entry in _state.Mods)
            {
                _allMods.Add(entry);
            }
            ApplyFilters();
        }
        _initializing = false;

        if (_libraryFolder is not null && Directory.Exists(_libraryFolder))
        {
            await ScanLibraryAsync();
        }
        else
        {
            UpdateEmptyState();
        }
        UpdateGridLayout();
    }

    private async void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(
            picker,
            WindowNative.GetWindowHandle(App.MainWindow!));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        _libraryFolder = folder.Path;
        _state.LastFolder = _libraryFolder;
        _allMods.Clear();
        _imageCache.Clear();
        FolderLabel.Text = _libraryFolder;
        ApplyFilters();
        await SaveStateAsync();
        await ScanLibraryAsync();
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        await ScanLibraryAsync();
    }

    private async Task ScanLibraryAsync()
    {
        if (_scanning)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(_libraryFolder) || !Directory.Exists(_libraryFolder))
        {
            SetStatus("Choose a folder that contains your FNF mods before scanning.");
            return;
        }

        _scanning = true;
        ScanButton.IsEnabled = false;
        ChooseFolderButton.IsEnabled = false;
        ScanProgress.IsActive = true;
        ScanProgressBar.IsIndeterminate = true;
        SetStatus("Scanning for executable mods. This does not run them.");
        var root = _libraryFolder;
        var oldEntries = _allMods.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        try
        {
            var result = await Task.Run(() => ScanFolder(root, oldEntries, _ignoredPaths));
            _allMods.Clear();
            foreach (var entry in result.Entries)
            {
                _allMods.Add(entry);
            }
            _state.Mods = _allMods.ToList();
            _state.LastFolder = root;
            _imageCache.Clear();
            RefreshTagFilter();
            ApplyFilters();
            if (result.InaccessibleDirectories > 0)
            {
                SetStatus(
                    $"Found {_allMods.Count} executables. {result.InaccessibleDirectories} folder(s) could not be scanned.",
                    isError: true);
            }
            else
            {
                SetStatus($"Scan complete. Found {_allMods.Count} executable(s).");
            }
            await SaveStateAsync();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            SetStatus($"Scan failed: {error.Message}", isError: true);
        }
        finally
        {
            _scanning = false;
            ScanButton.IsEnabled = true;
            ChooseFolderButton.IsEnabled = true;
            ScanProgress.IsActive = false;
            ScanProgressBar.IsIndeterminate = false;
        }
    }

    private static ScanResult ScanFolder(
        string root,
        IReadOnlyDictionary<string, ModEntry> previous,
        HashSet<string> ignored)
    {
        var entries = new List<ModEntry>();
        var directories = new Stack<string>();
        directories.Push(root);
        var inaccessible = 0;
        while (directories.TryPop(out var current))
        {
            IEnumerable<string> executables;
            try
            {
                executables = Directory.EnumerateFiles(current);
                foreach (var file in executables)
                {
                    if (!System.IO.Path.GetExtension(file).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                        || ignored.Contains(file))
                    {
                        continue;
                    }

                    try
                    {
                        var info = new FileInfo(file);
                        previous.TryGetValue(file, out var saved);
                        entries.Add(new ModEntry
                        {
                            Path = file,
                            RelativePath = System.IO.Path.GetRelativePath(root, file),
                            FileSize = info.Length,
                            LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks,
                            PlayCount = saved?.PlayCount ?? 0,
                            IsFavorite = saved?.IsFavorite ?? false,
                            Tags = saved?.Tags.ToList() ?? []
                        });
                    }
                    catch (Exception error) when (
                        error is IOException or UnauthorizedAccessException or FileNotFoundException)
                    {
                        inaccessible++;
                    }
                }

                foreach (var directory in Directory.EnumerateDirectories(current))
                {
                    if (System.IO.Path.GetFileName(directory).Equals("__MACOSX", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    try
                    {
                        if ((File.GetAttributes(directory) & System.IO.FileAttributes.ReparsePoint) == 0)
                        {
                            directories.Push(directory);
                        }
                    }
                    catch (Exception error) when (
                        error is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
                    {
                        inaccessible++;
                    }
                }
            }
            catch (Exception error) when (
                error is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                inaccessible++;
            }
        }

        entries.Sort((left, right) =>
        {
            var byName = StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName);
            return byName != 0
                ? byName
                : StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath);
        });
        return new ScanResult(entries, inaccessible);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initializing)
        {
            ApplyFilters();
        }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initializing)
        {
            ApplyFilters();
        }
    }

    private void ApplyFilters()
    {
        var query = SearchBox.Text.Trim();
        var selectedTag = TagFilterBox.SelectedItem as string;
        IEnumerable<ModEntry> entries = _allMods;
        if (query.Length > 0)
        {
            var foldedQuery = query.ToUpperInvariant();
            entries = entries.Where(entry => entry.SearchText.Contains(foldedQuery, StringComparison.Ordinal));
        }
        if (FavoritesToggle.IsChecked == true)
        {
            entries = entries.Where(entry => entry.IsFavorite);
        }
        if (!string.IsNullOrEmpty(selectedTag) && selectedTag != "All tags")
        {
            entries = entries.Where(entry =>
                entry.Tags.Any(tag => tag.Equals(selectedTag, StringComparison.OrdinalIgnoreCase)));
        }

        var sortOrder = (SortBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Name (A-Z)";
        entries = sortOrder switch
        {
            "Name (Z-A)" => entries.OrderByDescending(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase),
            "Most played" => entries.OrderByDescending(entry => entry.PlayCount)
                .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase),
            "Least played" => entries.OrderBy(entry => entry.PlayCount)
                .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase),
            "Favorites first" => entries.OrderByDescending(entry => entry.IsFavorite)
                .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase),
            _ => entries.OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
        };

        var visible = entries.ToList();
        UpdateFilteredMods(visible);
        var filtered = query.Length > 0
            || FavoritesToggle.IsChecked == true
            || (!string.IsNullOrEmpty(selectedTag) && selectedTag != "All tags");
        CountLabel.Text = filtered
            ? $"{visible.Count} of {_allMods.Count} executables"
            : $"{_allMods.Count} executable{(_allMods.Count == 1 ? "" : "s")}";
        UpdateEmptyState();
    }

    private void UpdateFilteredMods(IReadOnlyList<ModEntry> visible)
    {
        var visibleEntries = new HashSet<ModEntry>(visible);
        for (var index = FilteredMods.Count - 1; index >= 0; index--)
        {
            if (!visibleEntries.Contains(FilteredMods[index]))
            {
                FilteredMods.RemoveAt(index);
            }
        }

        for (var index = 0; index < visible.Count; index++)
        {
            if (index < FilteredMods.Count && ReferenceEquals(FilteredMods[index], visible[index]))
            {
                continue;
            }

            var existingIndex = FilteredMods.IndexOf(visible[index]);
            if (existingIndex >= 0)
            {
                FilteredMods.Move(existingIndex, index);
            }
            else
            {
                FilteredMods.Insert(index, visible[index]);
            }
        }
    }

    private void RefreshTagFilter()
    {
        var previousTag = TagFilterBox.SelectedItem as string;
        var tags = _allMods.SelectMany(entry => entry.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var options = new[] { "All tags" }.Concat(tags).ToList();
        TagFilterBox.ItemsSource = options;
        var selection = previousTag is not null
            ? options.FirstOrDefault(tag => tag.Equals(previousTag, StringComparison.OrdinalIgnoreCase))
            : null;
        TagFilterBox.SelectedItem = selection ?? "All tags";
    }

    private void UpdateEmptyState()
    {
        var showEmpty = FilteredMods.Count == 0;
        EmptyState.Visibility = showEmpty ? Visibility.Visible : Visibility.Collapsed;
        ModsGridView.Visibility = showEmpty ? Visibility.Collapsed : Visibility.Visible;
        if (!showEmpty)
        {
            return;
        }

        EmptyTitle.Text = _allMods.Count == 0
            ? "Your library is ready for its first scan."
            : "No mods match those filters.";
    }

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ModEntry entry)
        {
            return;
        }
        if (_state.ConfirmLaunch)
        {
            var confirm = new ContentDialog
            {
                Title = "Launch this executable?",
                Content = $"{entry.DisplayName}\n\nOnly run mods you trust. Downloaded executables can be unsafe.\n\nLaunch it now?",
                PrimaryButtonText = "Launch",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = entry.Path,
                WorkingDirectory = System.IO.Path.GetDirectoryName(entry.Path)
                    ?? throw new InvalidOperationException("The executable folder could not be determined."),
                UseShellExecute = true
            };
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Windows did not start the executable.");
            entry.PlayCount++;
            _state.Mods = _allMods.ToList();
            ApplyFilters();
            await SaveStateAsync();
            SetStatus($"Launched {entry.DisplayName}.");
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetStatus($"Could not launch {entry.DisplayName}: {error.Message}", isError: true);
        }
    }

    private async void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ModEntry entry)
        {
            return;
        }
        entry.IsFavorite = !entry.IsFavorite;
        _state.Mods = _allMods.ToList();
        ApplyFilters();
        await SaveStateAsync();
    }

    private async void Ignore_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ModEntry entry)
        {
            return;
        }
        _ignoredPaths.Add(entry.Path);
        _state.IgnoredPaths = _ignoredPaths.ToList();
        _allMods.Remove(entry);
        _state.Mods = _allMods.ToList();
        RefreshTagFilter();
        ApplyFilters();
        await SaveStateAsync();
        SetStatus($"Ignored {entry.DisplayName}. Restore it from Ignored in the toolbar.");
    }

    private async void EditTags_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ModEntry entry)
        {
            return;
        }
        var editor = new TextBox
        {
            Text = string.Join(", ", entry.Tags),
            PlaceholderText = "sonic.exe, horror"
        };
        var dialog = new ContentDialog
        {
            Title = $"Tags for {entry.DisplayName}",
            Content = editor,
            PrimaryButtonText = "Save tags",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        entry.Tags = editor.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _state.Mods = _allMods.ToList();
        RefreshTagFilter();
        ApplyFilters();
        await SaveStateAsync();
    }

    private async void ManageIgnored_Click(object sender, RoutedEventArgs e)
    {
        var ignored = _ignoredPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        if (ignored.Count == 0)
        {
            SetStatus("There are no ignored executables.");
            return;
        }

        var list = new ListView
        {
            ItemsSource = ignored,
            SelectionMode = ListViewSelectionMode.Multiple,
            MaxHeight = 360,
            MinWidth = 480
        };
        var dialog = new ContentDialog
        {
            Title = "Ignored executables",
            Content = list,
            PrimaryButtonText = "Restore selected",
            SecondaryButtonText = "Restore all",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            foreach (var selectedItem in list.SelectedItems)
            {
                if (selectedItem is string path)
                {
                    _ignoredPaths.Remove(path);
                }
            }
        }
        else if (result == ContentDialogResult.Secondary)
        {
            _ignoredPaths.Clear();
        }
        else
        {
            return;
        }

        _state.IgnoredPaths = _ignoredPaths.ToList();
        await SaveStateAsync();
        await ScanLibraryAsync();
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var confirmLaunch = new ToggleSwitch
        {
            Header = "Confirm before launching mods",
            IsOn = _state.ConfirmLaunch
        };
        var columns = new ComboBox { Header = "Cards per row", MinWidth = 220 };
        AutomationProperties.SetAutomationId(columns, "SettingsColumns");
        for (var count = 1; count <= 6; count++)
        {
            columns.Items.Add(new ComboBoxItem { Content = $"{count} column{(count == 1 ? "" : "s")}" });
        }
        columns.SelectedIndex = Math.Clamp(_state.CardColumns, 1, 6) - 1;
        var cardStyle = new ComboBox { Header = "Card artwork", MinWidth = 220 };
        AutomationProperties.SetAutomationId(cardStyle, "SettingsCardStyle");
        cardStyle.Items.Add(new ComboBoxItem { Content = "Compact" });
        cardStyle.Items.Add(new ComboBoxItem { Content = "Large" });
        cardStyle.SelectedIndex = _state.CardStyle == "Large" ? 1 : 0;
        var colorKeys = new[]
        {
            "Background", "Card", "Border", "Accent", "Text", "MutedText",
            "Button", "ButtonText", "ButtonBorder"
        };
        var colorLabels = new[]
        {
            "Window background", "Mod cards", "Card borders", "Accent", "Main text",
            "Secondary text", "Button fill", "Button text", "Button border"
        };
        var palette = OceanColors;
        var customColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        var colorControls = new StackPanel { Spacing = 8 };
        for (var index = 0; index < colorKeys.Length; index++)
        {
            var key = colorKeys[index];
            var fallback = ParseColor(palette[index]);
            var initialColor = fallback;
            if (_state.CustomColors.TryGetValue(key, out var savedColor))
            {
                initialColor = ParseColorOrDefault(savedColor, fallback);
            }
            else if (key == "Accent" && _state.UseCustomAccentColor)
            {
                initialColor = ParseColorOrDefault(_state.CustomAccentColor, fallback);
            }
            customColors[key] = initialColor;
            colorControls.Children.Add(CreateColorPickerButton(key, colorLabels[index], initialColor, customColors));
        }

        var useCustomColors = new ToggleSwitch
        {
            Header = "Customize colors individually",
            IsOn = _state.UseCustomColors || _state.UseCustomAccentColor
        };
        AutomationProperties.SetAutomationId(useCustomColors, "SettingsCustomColors");
        var colorSettings = new ScrollViewer
        {
            MaxHeight = 620,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = colorControls,
            Visibility = useCustomColors.IsOn ? Visibility.Visible : Visibility.Collapsed
        };
        useCustomColors.Toggled += (_, _) =>
        {
            colorSettings.Visibility = useCustomColors.IsOn ? Visibility.Visible : Visibility.Collapsed;
        };

        var options = new StackPanel
        {
            MaxHeight = 620,
            Spacing = 12,
            Children = { confirmLaunch, columns, cardStyle, useCustomColors }
        };
        var content = new Grid
        {
            ColumnSpacing = 24,
            Children = { options, colorSettings }
        };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        Grid.SetColumn(colorSettings, 1);
        var dialog = new ContentDialog
        {
            Title = "Settings",
            Content = content,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _state.ConfirmLaunch = confirmLaunch.IsOn;
        _state.CardColumns = Math.Clamp(columns.SelectedIndex + 1, 1, 6);
        _state.CardStyle = (cardStyle.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Compact";
        _state.Theme = "Ocean";
        _state.UseCustomColors = useCustomColors.IsOn;
        _state.CustomColors = customColors.ToDictionary(
            entry => entry.Key,
            entry => FormatColor(entry.Value),
            StringComparer.OrdinalIgnoreCase);
        _state.UseCustomAccentColor = _state.UseCustomColors;
        _state.CustomAccentColor = _state.CustomColors["Accent"];
        ApplyTheme();
        UpdateGridLayout();
        await SaveStateAsync();
    }

    private Button CreateColorPickerButton(
        string key,
        string label,
        Color initialColor,
        IDictionary<string, Color> selectedColors)
    {
        var swatch = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Colors.White),
            Background = new SolidColorBrush(initialColor),
            VerticalAlignment = VerticalAlignment.Center
        };
        var button = new Button
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = (Brush)App.Current.Resources["ButtonBackgroundBrush"],
            BorderBrush = (Brush)App.Current.Resources["ButtonBorderBrush"],
            Foreground = (Brush)App.Current.Resources["ButtonForegroundBrush"],
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    swatch,
                    new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }
                }
            }
        };
        AutomationProperties.SetAutomationId(button, $"SettingsColor{key}");
        var picker = new ColorPicker
        {
            Color = initialColor,
            IsAlphaEnabled = false,
            IsColorPreviewVisible = true,
            IsMoreButtonVisible = true,
            MinWidth = 280
        };
        picker.ColorChanged += (_, args) =>
        {
            selectedColors[key] = args.NewColor;
            swatch.Background = new SolidColorBrush(args.NewColor);
        };
        button.Flyout = new Flyout { Content = picker };
        return button;
    }

    private static readonly string[] OceanColors =
        ["#091520", "#122432", "#203746", "#54B8E8", "#EFF9FF", "#9BB6C7", "#183B53", "#F0FAFF", "#203746"];

    private void ApplyTheme()
    {
        var colorKeys = new[]
        {
            "Background", "Card", "Border", "Accent", "Text", "MutedText",
            "Button", "ButtonText", "ButtonBorder"
        };
        var resourceKeys = new[]
        {
            "AppBackgroundBrush", "CardBackgroundBrush", "CardBorderBrush",
            "AccentBrush", "PrimaryTextBrush", "MutedTextBrush", "ButtonBackgroundBrush",
            "ButtonForegroundBrush", "ButtonBorderBrush"
        };
        for (var index = 0; index < colorKeys.Length; index++)
        {
            var color = ParseColor(OceanColors[index]);
            if (_state.UseCustomColors
                && _state.CustomColors.TryGetValue(colorKeys[index], out var customColor))
            {
                color = ParseColorOrDefault(customColor, color);
            }
            else if (index == 3 && _state.UseCustomAccentColor)
            {
                color = ParseColorOrDefault(_state.CustomAccentColor, color);
            }
            App.Current.Resources[resourceKeys[index]] = new SolidColorBrush(color);
        }
        RootGrid.Background = (Brush)App.Current.Resources["AppBackgroundBrush"];
    }

    private static Color ParseColor(string value) =>
        Color.FromArgb(
            255,
            Convert.ToByte(value.Substring(1, 2), 16),
            Convert.ToByte(value.Substring(3, 2), 16),
            Convert.ToByte(value.Substring(5, 2), 16));

    private static Color ParseColorOrDefault(string value, Color fallback)
    {
        if (string.IsNullOrEmpty(value) || value.Length != 7 || value[0] != '#')
        {
            return fallback;
        }
        try
        {
            return ParseColor(value);
        }
        catch (FormatException)
        {
            return fallback;
        }
        catch (OverflowException)
        {
            return fallback;
        }
    }

    private static string FormatColor(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private void ModsGridView_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateGridLayout();
    }

    private void ModsGridView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateGridLayout();
    }

    private void UpdateGridLayout()
    {
        if (ModsGridView.ItemsPanelRoot is not ItemsWrapGrid panel || ModsGridView.ActualWidth <= 0)
        {
            return;
        }
        var columns = Math.Clamp(_state.CardColumns, 1, 6);
        var itemWidth = Math.Max(120, (ModsGridView.ActualWidth - (columns * 12)) / columns);
        panel.ItemWidth = itemWidth;
        panel.ItemHeight = _state.CardStyle == "Large" ? 300 : 244;
    }

    private async void ModIcon_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Image image)
        {
            await UpdateModIconAsync(image);
        }
    }

    private async void ModIcon_DataContextChanged(
        FrameworkElement sender,
        DataContextChangedEventArgs args)
    {
        if (sender is Image image)
        {
            await UpdateModIconAsync(image);
        }
    }

    private async Task UpdateModIconAsync(Image image)
    {
        if (image.DataContext is not ModEntry entry)
        {
            image.Tag = null;
            image.Source = null;
            image.Visibility = Visibility.Collapsed;
            SetFallbackVisibility(image, Visibility.Visible);
            return;
        }

        if (image.Tag is IconLoadRequest currentRequest
            && currentRequest.Path.Equals(entry.Path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var request = new IconLoadRequest(entry.Path);
        image.Tag = request;
        image.Source = null;
        image.Visibility = Visibility.Collapsed;
        SetFallbackVisibility(image, Visibility.Visible);
        image.Width = _state.CardStyle == "Large" ? 88 : 64;
        image.Height = image.Width;
        var iconGrid = image.Parent as Grid;
        if (iconGrid is not null)
        {
            iconGrid.Width = image.Width;
            iconGrid.Height = image.Height;
        }
        var cacheKey = entry.Path;
        if (!_imageCache.TryGetValue(cacheKey, out var bitmap))
        {
            bitmap = await LoadIconAsync(entry);
            if (bitmap is not null)
            {
                _imageCache[cacheKey] = bitmap;
            }
        }
        if (!ReferenceEquals(image.Tag, request) || image.DataContext is not ModEntry currentEntry
            || !currentEntry.Path.Equals(request.Path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        image.Source = bitmap;
        image.Visibility = bitmap is null ? Visibility.Collapsed : Visibility.Visible;
        SetFallbackVisibility(
            image,
            bitmap is null ? Visibility.Visible : Visibility.Collapsed);
    }

    private static void SetFallbackVisibility(Image image, Visibility visibility)
    {
        if (image.Parent is Grid iconGrid
            && iconGrid.Children.OfType<FontIcon>().FirstOrDefault() is { } fallback)
        {
            fallback.Visibility = visibility;
        }
    }

    private static async Task<BitmapImage?> LoadIconAsync(ModEntry entry)
    {
        var folder = System.IO.Path.GetDirectoryName(entry.Path);
        if (folder is not null)
        {
            foreach (var name in new[] { "icon.ico", "icon.png" })
            {
                var candidate = System.IO.Path.Combine(folder, name);
                if (!File.Exists(candidate))
                {
                    continue;
                }
                try
                {
                    if (System.IO.Path.GetExtension(candidate).Equals(".ico", StringComparison.OrdinalIgnoreCase))
                    {
                        var iconFileBitmap = await LoadNativeIconAsync(candidate, isIconFile: true);
                        if (iconFileBitmap is not null)
                        {
                            return iconFileBitmap;
                        }
                        continue;
                    }

                    var iconFile = await StorageFile.GetFileFromPathAsync(candidate);
                    using var iconStream = await iconFile.OpenReadAsync();
                    var iconBitmap = new BitmapImage();
                    await iconBitmap.SetSourceAsync(iconStream);
                    return iconBitmap;
                }
                catch (Exception error) when (
                    error is IOException or UnauthorizedAccessException or COMException)
                {
                    continue;
                }
            }
        }

        var executableIcon = await LoadNativeIconAsync(entry.Path, isIconFile: false);
        if (executableIcon is not null)
        {
            return executableIcon;
        }

        try
        {
            var executable = await StorageFile.GetFileFromPathAsync(entry.Path);
            using var thumbnail = await executable.GetThumbnailAsync(
                ThumbnailMode.SingleItem,
                128,
                ThumbnailOptions.ResizeThumbnail);
            if (thumbnail is null || thumbnail.Size == 0)
            {
                return null;
            }
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(thumbnail);
            return bitmap;
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or COMException or FileNotFoundException)
        {
            return null;
        }
    }

    private static async Task<BitmapImage?> LoadNativeIconAsync(string path, bool isIconFile)
    {
        const int iconSize = 96;
        var pixels = await Task.Run(() => ExtractIconPixels(path, iconSize, isIconFile));
        if (pixels is null)
        {
            return null;
        }

        using var softwareBitmap = SoftwareBitmap.CreateCopyFromBuffer(
            CryptographicBuffer.CreateFromByteArray(pixels),
            BitmapPixelFormat.Bgra8,
            iconSize,
            iconSize,
            BitmapAlphaMode.Premultiplied);
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetSoftwareBitmap(softwareBitmap);
        await encoder.FlushAsync();
        stream.Seek(0);
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    private static byte[]? ExtractIconPixels(string path, int size, bool isIconFile)
    {
        const uint imageIcon = 1;
        const uint loadFromFile = 0x0010;
        const uint dibRgbColors = 0;
        const uint biRgb = 0;
        const uint drawIconNormal = 0x0003;
        var largeIcon = IntPtr.Zero;
        var smallIcon = IntPtr.Zero;
        var deviceContext = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var previousBitmap = IntPtr.Zero;
        var icon = IntPtr.Zero;
        try
        {
            if (isIconFile)
            {
                icon = LoadImageW(IntPtr.Zero, path, imageIcon, size, size, loadFromFile);
            }
            else if (ExtractIconExW(path, 0, out largeIcon, out smallIcon, 1) > 0)
            {
                icon = largeIcon != IntPtr.Zero ? largeIcon : smallIcon;
            }

            if (icon == IntPtr.Zero)
            {
                return null;
            }

            deviceContext = CreateCompatibleDC(IntPtr.Zero);
            if (deviceContext == IntPtr.Zero)
            {
                return null;
            }

            var header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = size,
                Height = -size,
                Planes = 1,
                BitCount = 32,
                Compression = biRgb
            };
            bitmap = CreateDIBSection(
                deviceContext,
                ref header,
                dibRgbColors,
                out var pixelBuffer,
                IntPtr.Zero,
                0);
            if (bitmap == IntPtr.Zero || pixelBuffer == IntPtr.Zero)
            {
                return null;
            }

            previousBitmap = SelectObject(deviceContext, bitmap);
            if (previousBitmap == IntPtr.Zero
                || !DrawIconEx(deviceContext, 0, 0, icon, size, size, 0, IntPtr.Zero, drawIconNormal))
            {
                return null;
            }

            var pixels = new byte[size * size * 4];
            Marshal.Copy(pixelBuffer, pixels, 0, pixels.Length);
            return pixels;
        }
        finally
        {
            if (previousBitmap != IntPtr.Zero && deviceContext != IntPtr.Zero)
            {
                SelectObject(deviceContext, previousBitmap);
            }
            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }
            if (deviceContext != IntPtr.Zero)
            {
                DeleteDC(deviceContext);
            }
            if (isIconFile)
            {
                if (icon != IntPtr.Zero)
                {
                    DestroyIcon(icon);
                }
            }
            else
            {
                if (largeIcon != IntPtr.Zero)
                {
                    DestroyIcon(largeIcon);
                }
                if (smallIcon != IntPtr.Zero && smallIcon != largeIcon)
                {
                    DestroyIcon(smallIcon);
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPixelsPerMeter;
        public int YPixelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(
        string file,
        int iconIndex,
        out IntPtr largeIcon,
        out IntPtr smallIcon,
        uint iconCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImageW(
        IntPtr instance,
        string name,
        uint imageType,
        int width,
        int height,
        uint loadFlags);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr deviceContext,
        ref BitmapInfoHeader bitmapInfo,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr obj);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DrawIconEx(
        IntPtr deviceContext,
        int x,
        int y,
        IntPtr icon,
        int width,
        int height,
        uint step,
        IntPtr brush,
        uint flags);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    private async Task SaveStateAsync()
    {
        _state.LastFolder = _libraryFolder;
        _state.IgnoredPaths = _ignoredPaths.ToList();
        try
        {
            await _store.SaveAsync(_state);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            SetStatus($"Could not save library data: {error.Message}", isError: true);
        }
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusLabel.Text = message;
        StatusLabel.Foreground = isError
            ? new SolidColorBrush(ParseColor("#FF8795"))
            : (Brush)App.Current.Resources["MutedTextBrush"];
    }

    private sealed record ScanResult(List<ModEntry> Entries, int InaccessibleDirectories);
    private sealed record IconLoadRequest(string Path);
}
