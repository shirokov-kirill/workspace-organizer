using System.Text.Json;
using System.Text.Json.Serialization;
using WorkspaceOrganizer.Core.Models;

namespace WorkspaceOrganizer.Core.Storage;

public sealed class JsonDataStore(string filePath)
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WorkspaceOrganizer", "organizer.json");

    public string FilePath { get; } = filePath;

    /// <summary>
    /// Loads the data file, or defaults if there is none. An unreadable file is moved aside
    /// (never overwritten) and reported through <paramref name="warning"/>.
    /// </summary>
    public OrganizerData Load(out string? warning)
    {
        warning = null;
        if (!File.Exists(FilePath))
            return OrganizerData.CreateDefault();

        try
        {
            using var stream = File.OpenRead(FilePath);
            return (JsonSerializer.Deserialize<OrganizerData>(stream, Options) ?? OrganizerData.CreateDefault()).Normalize();
        }
        catch (JsonException ex)
        {
            var backup = $"{FilePath}.corrupt-{DateTime.Now:yyyyMMddHHmmss}";
            File.Move(FilePath, backup);
            warning = $"Data file was unreadable ({ex.Message}); it was moved to {backup}.";
            return OrganizerData.CreateDefault();
        }
    }

    /// <summary>Like <see cref="Load"/> but never touches the file; null if it's missing or unreadable.</summary>
    public OrganizerData? TryRead()
    {
        try
        {
            using var stream = File.OpenRead(FilePath);
            return JsonSerializer.Deserialize<OrganizerData>(stream, Options)?.Normalize();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(OrganizerData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        var temp = FilePath + ".tmp";
        using (var stream = File.Create(temp))
            JsonSerializer.Serialize(stream, data, Options);
        File.Move(temp, FilePath, overwrite: true);
    }
}
