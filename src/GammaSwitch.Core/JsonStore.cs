using System.Text.Json;
using System.Text.Json.Serialization;

namespace GammaSwitch.Core;

public sealed class JsonStore<T>(string path) where T : new()
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public string FilePath { get; } = Path.GetFullPath(path);
    public T Load()
    {
        if (!File.Exists(FilePath)) return new T();
        using var stream = File.OpenRead(FilePath);
        if (stream.Length > 8 * 1024 * 1024) throw new InvalidDataException("设置文件超过大小限制。");
        return JsonSerializer.Deserialize<T>(stream, Options) ?? throw new InvalidDataException("设置文件为空。");
    }
    public void Save(T value)
    {
        var folder = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(folder);
        var temporary = Path.Combine(folder, $".{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, Options);
                stream.Flush(true);
            }
            File.Move(temporary, FilePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static T Copy(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;
}

public sealed class RecoveryStore(string path) : IRecoveryStore
{
    private readonly JsonStore<RecoveryState> store = new(path);
    public RecoveryState Load() => store.Load();
    public void Save(RecoveryState state) => store.Save(state);
}
