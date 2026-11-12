using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Weatherly.Core.Scenes;

/// <summary>
/// Templates as files (<c>.sillscene</c>, JSON) and as links (<c>sillfall://scene?d=…</c>: the same JSON, deflated
/// and Base64url-encoded). Everything read is size-limited and sanitized: files and links come from anyone.
/// </summary>
public static class SceneCodec
{
    public const string FileExtension = ".sillscene";

    public static string LinkPrefix { get; } = $"{AppInfo.UriScheme}://scene?d=";

    /// <summary>Largest file or decoded link accepted, in bytes. A scene is a few hundred.</summary>
    public const int MaxSize = 16 * 1024;

    private const string Format = "sillfall-scene";

    public static string ToJson(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return JsonSerializer.Serialize(new SceneFile { Scene = scene.Sanitized() }, SceneJsonContext.Default.SceneFile);
    }

    /// <summary>Reads a <c>.sillscene</c> file's text. Returns <see langword="false"/> with a reason for anything that is not a scene.</summary>
    public static bool TryFromJson(string json, out Scene scene, out string? error)
    {
        scene = new Scene();
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaxSize)
        {
            error = "The file is empty or too large for a scene.";
            return false;
        }

        try
        {
            SceneFile? file = JsonSerializer.Deserialize(json, SceneJsonContext.Default.SceneFile);
            if (file?.Format != Format || file.Scene is null)
            {
                error = "This is not a scene.";
                return false;
            }

            if (file.Version > SceneFile.CurrentVersion)
            {
                error = "The scene was made by a newer version of the app.";
                return false;
            }

            scene = file.Scene.Sanitized();
            error = null;
            return true;
        }
        catch (JsonException exception)
        {
            error = $"The scene cannot be read: {exception.Message}";
            return false;
        }
    }

    public static string ToLink(Scene scene)
    {
        byte[] json = Encoding.UTF8.GetBytes(ToJson(scene));
        using var compressed = new MemoryStream();
        using (var deflate = new DeflateStream(compressed, CompressionLevel.SmallestSize))
        {
            deflate.Write(json);
        }

        return LinkPrefix + Base64Url(compressed.ToArray());
    }

    public static bool TryFromLink(string link, out Scene scene, out string? error)
    {
        scene = new Scene();
        if (string.IsNullOrWhiteSpace(link) || !link.StartsWith(LinkPrefix, StringComparison.OrdinalIgnoreCase) || link.Length > MaxSize)
        {
            error = "This is not a scene link.";
            return false;
        }

        string data = link[LinkPrefix.Length..];
        int end = data.IndexOfAny(['&', '#']);
        if (end >= 0)
        {
            data = data[..end];
        }

        byte[]? compressed = FromBase64Url(data);
        if (compressed is null)
        {
            error = "The link is damaged.";
            return false;
        }

        try
        {
            using var input = new DeflateStream(new MemoryStream(compressed), CompressionMode.Decompress);
            using var output = new MemoryStream();
            byte[] buffer = new byte[4096];
            int read;
            while ((read = input.Read(buffer)) > 0)
            {
                output.Write(buffer, 0, read);
                if (output.Length > MaxSize)
                {
                    error = "The link holds too much data for a scene.";
                    return false;
                }
            }

            return TryFromJson(Encoding.UTF8.GetString(output.ToArray()), out scene, out error);
        }
        catch (InvalidDataException)
        {
            error = "The link is damaged.";
            return false;
        }
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[]? FromBase64Url(string text)
    {
        string base64 = text.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
        byte[] buffer = new byte[base64.Length];
        return Convert.TryFromBase64String(base64, buffer, out int written) ? buffer[..written] : null;
    }

    internal sealed record SceneFile
    {
        public const int CurrentVersion = 1;

        public string Format { get; set; } = SceneCodec.Format;

        public int Version { get; set; } = CurrentVersion;

        public Scene? Scene { get; set; }
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(SceneCodec.SceneFile))]
internal sealed partial class SceneJsonContext : JsonSerializerContext;
