using System.Text.Json;

namespace FnFModLibrary_WinUI;

public sealed class LibraryStore
{
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _statePath;

    public LibraryStore()
    {
        var dataDirectory = Environment.GetEnvironmentVariable("FNF_MOD_LIBRARY_DATA_DIR");
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            dataDirectory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FNF Mod Library WinUI");
        }
        _statePath = System.IO.Path.Combine(dataDirectory, "library.json");
    }

    public LibraryState Load()
    {
        if (!File.Exists(_statePath))
        {
            return new LibraryState();
        }

        var json = File.ReadAllText(_statePath);
        return JsonSerializer.Deserialize<LibraryState>(json) ?? new LibraryState();
    }

    public async Task SaveAsync(LibraryState state)
    {
        await _saveLock.WaitAsync();
        try
        {
            var directory = System.IO.Path.GetDirectoryName(_statePath)
                ?? throw new InvalidOperationException("The library data folder is unavailable.");
            Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(state, JsonOptions);
            var temporaryPath = _statePath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, json);
            File.Move(temporaryPath, _statePath, overwrite: true);
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
