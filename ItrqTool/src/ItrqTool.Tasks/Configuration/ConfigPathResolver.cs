namespace ItrqTool.Tasks.Configuration;

public static class ConfigPathResolver
{
    /// <summary>
    /// Resolves a config-file path: absolute paths are returned unchanged; a relative
    /// path is resolved against the application directory (AppContext.BaseDirectory),
    /// so configs load regardless of the process working directory.
    /// </summary>
    public static string Resolve(string path)
        => string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory, path);
}
