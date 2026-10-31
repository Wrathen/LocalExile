using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using GameServer.Modding;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

/// <summary>
/// A mod's loose <c>.cs</c> files (Agents/docs/dev/modding.md), compiled into one assembly in memory when the mod loads.
/// They see the framework, GameServer.Modding, the assemblies in their folder and those of the mods they depend on; the
/// server's other assemblies are not offered to the compiler, as <see cref="ModLoadContext"/> does not offer them at run time.
/// </summary>
static class ModScripts
{
    /// <summary>What a script file has without writing it.</summary>
    const string Usings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using GameServer.Modding;
        """;

    /// <summary>The compiler's errors, as <c>file(line,column): CS0000 text</c>.</summary>
    public sealed class CompileException(string message) : Exception(message);

    /// <summary>The script files directly in a mod's folder (not its subfolders), in a stable order.</summary>
    public static string[] Sources(string folder)
    {
        string[] files = Directory.GetFiles(folder, "*.cs");
        Array.Sort(files, StringComparer.Ordinal);
        return files;
    }

    // Roslyn loads only here: a server with no scripts never pays for it.
    static readonly Lazy<MetadataReference[]> Platform = new(() =>
    {
        string runtime = RuntimeEnvironment.GetRuntimeDirectory();
        return [.. Directory.GetFiles(runtime, "*.dll").Where(IsManaged)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(IMod).Assembly.Location))];
    });

    static bool IsManaged(string path)
    {
        try { AssemblyName.GetAssemblyName(path); return true; }
        catch (BadImageFormatException) { return false; }
    }

    /// <summary>Compiles the files; <paramref name="references"/> are the images of the other assemblies they may use.</summary>
    public static (byte[] Dll, byte[] Pdb) Compile(string id, IReadOnlyList<string> files, IEnumerable<byte[]> references, bool module = false)
    {
        var options = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = files.Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), options, path, Encoding.UTF8))
            .Append(CSharpSyntaxTree.ParseText(Usings, options)).ToList();
        var compilation = CSharpCompilation.Create(id + ".scripts", trees,
            [.. Platform.Value, .. module ? [MetadataReference.CreateFromFile(typeof(GameServer.Core.Protocol.Instance.Item).Assembly.Location)] : Array.Empty<MetadataReference>(), .. references.Select(image => MetadataReference.CreateFromImage(image))],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable,
                optimizationLevel: OptimizationLevel.Debug));
        using var dll = new MemoryStream();
        using var pdb = new MemoryStream();
        var result = compilation.Emit(dll, pdb, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
        if (!result.Success)
        {
            var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            throw new CompileException(string.Join(" | ", errors.Take(5).Select(Describe))
                + (errors.Count > 5 ? $" | and {errors.Count - 5} more" : ""));
        }
        return (dll.ToArray(), pdb.ToArray());
    }

    static string Describe(Diagnostic error)
    {
        var at = error.Location.GetLineSpan();
        return $"{Path.GetFileName(at.Path)}({at.StartLinePosition.Line + 1},{at.StartLinePosition.Character + 1}): {error.Id} {error.GetMessage()}";
    }
}
