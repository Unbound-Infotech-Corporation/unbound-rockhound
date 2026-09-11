namespace GeoMineralTrace.Core.App;

/// <summary>
/// Local application data root under %LocalAppData%.
/// Migrates legacy <c>GeoMineralTrace</c> folder on first access when the new folder is absent.
/// </summary>
public static class AppDataPaths
{
    public const string FolderName = "UnboundRockhound";
    private const string LegacyFolderName = "GeoMineralTrace";

    private static readonly Lazy<string> RootLazy = new(ResolveAndPrepareRoot);

    public static string LocalRoot => RootLazy.Value;

    public static string Sub(params string[] parts)
    {
        var path = LocalRoot;
        foreach (var part in parts)
        {
            path = Path.Combine(path, part);
        }

        return path;
    }

    public static string LocalAppDataDisplayPath =>
        $"%LocalAppData%\\{FolderName}\\";

    private static string ResolveAndPrepareRoot()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(baseDir, FolderName);
        var legacy = Path.Combine(baseDir, LegacyFolderName);

        if (!Directory.Exists(root) && Directory.Exists(legacy))
        {
            try
            {
                Directory.Move(legacy, root);
            }
            catch (IOException)
            {
                // Legacy folder in use — copy what we can and keep using the new path going forward.
                Directory.CreateDirectory(root);
                CopyDirectoryContents(legacy, root);
            }
            catch (UnauthorizedAccessException)
            {
                Directory.CreateDirectory(root);
                CopyDirectoryContents(legacy, root);
            }
        }

        Directory.CreateDirectory(root);
        return root;
    }

    private static void CopyDirectoryContents(string source, string destination)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, dir);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            var targetDir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            if (!File.Exists(target))
            {
                File.Copy(file, target);
            }
        }
    }
}
