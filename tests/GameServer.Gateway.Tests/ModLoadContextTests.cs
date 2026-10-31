using System.Runtime.CompilerServices;

namespace GameServer.Gateway.Tests;

public sealed class ModLoadContextTests
{
    [Fact]
    public async Task Private_path_load_leaves_build_output_free_and_copy_can_be_removed_after_collection()
    {
        var (weak, directory) = LoadAndUnload();
        for (int i = 0; i < 20 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(50);
        }
        Assert.False(weak.IsAlive);
        Assert.True(ModLoadContext.TryDeleteCopy(directory));
        Assert.False(Directory.Exists(directory));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static (WeakReference Context, string Directory) LoadAndUnload()
    {
        string source = Path.Combine(Path.GetTempPath(), "localexile-mod-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(source);
        string path = Path.Combine(source, "test.dll");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "GameServer.Modding.TestMod.dll"), path);
        var context = new ModLoadContext("copy-test", source, false, []);
        var assembly = context.LoadCopy(path);
        Assert.Equal(Path.Combine(context.CopyDirectory, "test.dll"), assembly.Location);
        File.Delete(path); // The live assembly never locks its original build output.
        Directory.Delete(source);
        var weak = new WeakReference(context);
        context.Unload();
        return (weak, context.CopyDirectory);
    }

    [Fact]
    public void Copy_cleanup_refuses_the_temporary_directory_itself()
    {
        Assert.False(ModLoadContext.TryDeleteCopy(Path.GetTempPath()));
        Assert.True(Directory.Exists(Path.GetTempPath()));
    }
}
