using System.Security.Cryptography;
using System.Text;

namespace Filetranslate;

public partial class Form1
{
    private static readonly UTF8Encoding Utf8NoBom = new(false, true);
    private static string ContentHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string SafeChildPath(string root, string relative)
    {
        var parts = NormalizePath(relative).Split('/');
        if (parts.Any(p => p is ".." or "." || p.Contains(':') || p.Contains('\0')) || Path.IsPathRooted(relative))
            throw new IOException($"Unsafe relative path: {relative}");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var result = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!result.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("Path escapes output directory.");
        var parent = Path.GetDirectoryName(result);
        while (parent is not null)
        {
            if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Refusing linked output directory: {parent}");
            parent = Path.GetDirectoryName(parent);
        }
        if (File.Exists(result) && (File.GetAttributes(result) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Refusing linked output file.");
        return result;
    }

    private static void CommitFile(string temporary, string target, bool backup)
    {
        if (File.Exists(target))
        {
            if (backup)
            {
                _ = SafeChildPath(Path.GetDirectoryName(target)!, Path.GetFileName(target) + ".beeslator.bak");
                _ = SafeChildPath(Path.GetDirectoryName(target)!, Path.GetFileName(target) + ".beeslator.previous.bak");
            }
            if (backup && !File.Exists(target + ".beeslator.bak")) File.Copy(target, target + ".beeslator.bak");
            File.Replace(temporary, target, backup ? target + ".beeslator.previous.bak" : null);
        }
        else File.Move(temporary, target);
    }

    private static async Task WriteAtomicAsync(string target, string content, bool backup, CancellationToken ct)
    {
        target = SafeChildPath(Path.GetDirectoryName(Path.GetFullPath(target))!, Path.GetFileName(target));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + ".beeslator-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, content, Utf8NoBom, ct);
            ct.ThrowIfCancellationRequested();
            CommitFile(temporary, target, backup);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
