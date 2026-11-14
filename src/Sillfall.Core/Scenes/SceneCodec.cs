using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sillfall.Core.Localization;

namespace Sillfall.Core.Scenes;

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
            error = Texts.Get("Scene.Error.EmptyOrTooLarge");
            return false;
        }

        try
        {
            SceneFile? file = JsonSerializer.Deserialize(json, SceneJsonContext.Default.SceneFile);
            if (file?.Format != Format || file.Scene is null)
            {
                error = Texts.Get("Scene.Error.NotAScene");
                return false;
            }

            if (file.Version > SceneFile.CurrentVersion)
            {
                error = Texts.Get("Scene.Error.Newer");
                return false;
            }

            scene = file.Scene.Sanitized();
            error = null;
            return true;
        }
        catch (JsonException exception)
        {
            error = Texts.Format("Scene.Error.Unreadable", exception.Message);
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

    /// <summary>
    /// Reads a scene link. Accepts what Windows and browsers hand over, not only what <see cref="ToLink"/> writes
    /// (finding F-04): <c>sillfall://scene/?d=…</c> (the shell adds a slash after the host), any letter case in scheme
    /// and host, other query parameters before or after <c>d</c>, a fragment, and a percent-encoded value.
    /// </summary>
    public static bool TryFromLink(string link, out Scene scene, out string? error)
    {
        scene = new Scene();
        string? data = link is { Length: <= MaxSize } ? FindLinkData(link.Trim()) : null;
        if (data is null)
        {
            error = Texts.Get("Scene.Error.NotALink");
            return false;
        }

        byte[]? compressed = FromBase64Url(data);
        if (compressed is null || compressed.Length == 0)
        {
            error = Texts.Get("Scene.Error.Damaged");
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
                    error = Texts.Get("Scene.Error.TooMuchData");
                    return false;
                }
            }

            if (output.Length == 0)
            {
                error = Texts.Get("Scene.Error.Damaged");
                return false;
            }

            return TryFromJson(Encoding.UTF8.GetString(output.ToArray()), out scene, out error);
        }
        catch (InvalidDataException)
        {
            error = Texts.Get("Scene.Error.Damaged");
            return false;
        }
    }

    /// <summary>The value of <c>d</c> in a <c>sillfall://scene</c> link, percent-decoded, or <see langword="null"/>.</summary>
    private static string? FindLinkData(string link)
    {
        string scheme = AppInfo.UriScheme + "://";
        if (!link.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string rest = link[scheme.Length..];
        int fragment = rest.IndexOf('#', StringComparison.Ordinal);
        if (fragment >= 0)
        {
            rest = rest[..fragment];
        }

        int queryStart = rest.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0)
        {
            return null;
        }

        // Host and path: "scene" or "scene/", nothing else.
        string authority = rest[..queryStart];
        if (!authority.Equals("scene", StringComparison.OrdinalIgnoreCase) && !authority.Equals("scene/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach (string parameter in rest[(queryStart + 1)..].Split('&'))
        {
            if (parameter.StartsWith("d=", StringComparison.Ordinal))
            {
                string value = Uri.UnescapeDataString(parameter[2..]);
                return value.Length > 0 ? value : null;
            }
        }

        return null;
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
