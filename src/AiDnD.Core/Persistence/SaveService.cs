using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using AiDnD.Core.Game;

namespace AiDnD.Core.Persistence;

/// <summary>
/// Збереження стану гри в JSON-файл. Інтерфейс дозволяє пізніше замінити на БД (SQLite / SQL Server / EF Core).
/// </summary>
public interface IGameRepository
{
    Task SaveAsync(GameState state, string slot, CancellationToken ct = default);
    Task<GameState?> LoadAsync(string slot, CancellationToken ct = default);
    IEnumerable<string> ListSlots();
}

public sealed class JsonFileGameRepository(string directory) : IGameRepository
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        // Щоб українські літери зберігались читабельно, а не як А
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private string PathFor(string slot) => Path.Combine(directory, $"{slot}.json");

    public async Task SaveAsync(GameState state, string slot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(directory);
        var tmp = PathFor(slot) + ".tmp";
        await using (var fs = File.Create(tmp))
            await JsonSerializer.SerializeAsync(fs, state, Options, ct);
        File.Move(tmp, PathFor(slot), overwrite: true);
    }

    public async Task<GameState?> LoadAsync(string slot, CancellationToken ct = default)
    {
        var path = PathFor(slot);
        if (!File.Exists(path)) return null;
        await using var fs = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<GameState>(fs, Options, ct);
    }

    public IEnumerable<string> ListSlots() =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>()
            : [];
}
