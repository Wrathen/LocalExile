using System.Reflection;
using System.Runtime.Loader;
using GameServer.Modding;

/// <summary>
/// One mod's assemblies, collectible: unloading the mod lets the runtime free them once nothing holds one of its objects.
/// A mod sees the framework, the shared contracts (GameServer.Modding, the host's own copy, so <see cref="IMod"/> is one
/// type for every mod), the assemblies of the mods it depends on, and the files in its own folder. Not the server's other
/// assemblies: the Gateway, Core and Data are internal to it.
/// </summary>
sealed class ModLoadContext(string id, string folder, bool module, IReadOnlyList<ModLoadContext> dependencies)
    : AssemblyLoadContext($"mod {id}", isCollectible: true)
{
    static readonly string Contracts = typeof(IMod).Assembly.GetName().Name!;
    static readonly string CopyRoot = Path.Combine(Path.GetTempPath(), "localexile-mod-copy");
    readonly Lock copyGate = new();
    internal string CopyDirectory { get; } = Path.Combine(CopyRoot, Guid.NewGuid().ToString("N"));

    protected override Assembly? Load(AssemblyName name)
    {
        // A module can alter the game fully: the server's assemblies are the host's own copies.
        if (name.Name == Contracts || (module && name.Name?.StartsWith("GameServer.", StringComparison.Ordinal) == true)) return null;
        if (name.Name?.StartsWith("GameServer.", StringComparison.Ordinal) == true)
            throw new FileNotFoundException($"a mod can use GameServer.Modding, not {name.Name} (the server's own code)");
        foreach (var dependency in dependencies)
            if (dependency.Assemblies.FirstOrDefault(a => a.GetName().Name == name.Name) is { } shared) return shared;
        // Beside its manifest, or in its build's output (a module). null: the default context's (the framework).
        foreach (string path in new[] { Path.Combine(folder, name.Name + ".dll"), Path.Combine(folder, "bin", name.Name + ".dll") })
            if (File.Exists(path)) return LoadCopy(path);
        return null;
    }

    /// <summary>Loads a private disk copy (and its .pdb), leaving the build output free. Path-backed collectible
    /// assemblies avoid the LoadFromStream loader/finalizer failure observed on the supported Windows runtime.</summary>
    public Assembly LoadCopy(string path)
    {
        lock (copyGate)
        {
            Directory.CreateDirectory(CopyDirectory);
            string copy = Path.Combine(CopyDirectory, Path.GetFileName(path));
            if (!File.Exists(copy))
            {
                File.Copy(path, copy);
                string pdb = Path.ChangeExtension(path, ".pdb");
                if (File.Exists(pdb)) File.Copy(pdb, Path.ChangeExtension(copy, ".pdb"));
            }
            return LoadFromAssemblyPath(copy);
        }
    }

    // Called only after the context is collected and its copied modules have released their file handles.
    internal static bool TryDeleteCopy(string directory)
    {
        string full = Path.GetFullPath(directory);
        if (!string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(CopyRoot), StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(Path.GetFileName(full), "N", out _)) return false;
        try { if (Directory.Exists(full)) Directory.Delete(full, recursive: true); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>Loads what a mod's files or its compiled scripts were read into, by the same private disk copy as
    /// <see cref="LoadCopy"/>: never from a stream.</summary>
    public Assembly LoadImage(byte[] image, byte[]? symbols)
    {
        lock (copyGate)
        {
            Directory.CreateDirectory(CopyDirectory);
            string copy = Path.Combine(CopyDirectory, Guid.NewGuid().ToString("N") + ".dll");
            File.WriteAllBytes(copy, image);
            if (symbols is not null) File.WriteAllBytes(Path.ChangeExtension(copy, ".pdb"), symbols);
            return LoadFromAssemblyPath(copy);
        }
    }
}
