using System.IO;
using NexusOptimizer.Core.Security;

namespace NexusOptimizer.Tests;

/// <summary>
/// Deletion-safety tests: dimostrano che il motore rifiuta la cancellazione su
/// directory critiche, cartelle utente, radici di drive e percorsi fuori perimetro.
/// </summary>
public sealed class PathGuardTests
{
    private static string TempRoot()
    {
        var d = Path.Combine(Path.GetTempPath(), "nexusoptimizer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    [Trait("Category", "DeletionSafety")]
    public void Rejects_DriveRoot()
    {
        var guard = new PathGuard();
        var root = Path.GetPathRoot(Path.GetTempPath())!;   // es. C:\
        Assert.Throws<PathGuardException>(() => guard.ValidateForDelete(root, [root]));
    }

    [Fact]
    [Trait("Category", "DeletionSafety")]
    public void Rejects_UserProtectedFolder()
    {
        var guard = new PathGuard();
        var docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "never_here.does.not.exist");
        Assert.Throws<PathGuardException>(() => guard.ValidateForDelete(docs, [Path.GetTempPath()]));
    }

    [Fact]
    [Trait("Category", "DeletionSafety")]
    public void Rejects_Path_Outside_AllowedRoots()
    {
        var guard = new PathGuard();
        var baseDir = TempRoot();
        var target = Path.Combine(baseDir, "file.txt");
        File.WriteAllText(target, "x");
        var other = Path.Combine(Path.GetTempPath(), "nexusoptimizer-tests-other-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(other);
        try
        {
            Assert.Throws<PathGuardException>(() =>
                guard.ValidateForDelete(target, [other]));
        }
        finally
        {
            Directory.Delete(other, true);
            Directory.Delete(baseDir, true);
        }
    }

    [Fact]
    [Trait("Category", "DeletionSafety")]
    public void Allows_File_Under_AllowedRoot()
    {
        var guard = new PathGuard();
        var baseDir = TempRoot();
        var file = Path.Combine(baseDir, "clean.tmp");
        File.WriteAllText(file, "x");
        try
        {
            // Non deve lanciare: percorso dentro la root autorizzata e non escluso.
            guard.ValidateForDelete(file, [baseDir]);
        }
        catch (PathGuardException)
        {
            Assert.Fail("Percorso autorizzato non dovrebbe essere rifiutato.");
        }
        finally
        {
            Directory.Delete(baseDir, true);
        }
    }

    [Fact]
    [Trait("Category", "DeletionSafety")]
    public void Rejects_UserExclusion_Always()
    {
        var baseDir = TempRoot();
        var ex = Path.Combine(baseDir, "keep");
        Directory.CreateDirectory(ex);
        try
        {
            var guard = new PathGuard(new[] { Path.Combine(baseDir, "keep") });
            Assert.Throws<PathGuardException>(() =>
                guard.ValidateForDelete(Path.Combine(ex, "f.bin"), new[] { Path.Combine(baseDir, "keep") }));
        }
        finally
        {
            Directory.Delete(baseDir, true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative\\cache.tmp")]
    [Trait("Category", "DeletionSafety")]
    public void Rejects_Empty_Or_Relative_Path(string path)
    {
        var guard = new PathGuard();
        Assert.Throws<PathGuardException>(() => guard.ValidateForDelete(path, [Path.GetTempPath()]));
    }

    [Fact]
    [Trait("Category", "DeletionSafety")]
    public void Rejects_The_Allowed_Root_Itself()
    {
        var root = TempRoot();
        try
        {
            Assert.Throws<PathGuardException>(() => new PathGuard().ValidateForDelete(root, [root]));
            Assert.True(Directory.Exists(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    [Trait("Category", "DeletionSafety")]
    public void Rejects_Prefix_Collision_Outside_Allowed_Root()
    {
        var parent = TempRoot();
        var allowed = Path.Combine(parent, "cache");
        var collision = Path.Combine(parent, "cache-other");
        Directory.CreateDirectory(allowed);
        Directory.CreateDirectory(collision);
        var file = Path.Combine(collision, "keep.bin");
        File.WriteAllText(file, "keep");
        try
        {
            Assert.Throws<PathGuardException>(() => new PathGuard().ValidateForDelete(file, [allowed]));
            Assert.True(File.Exists(file));
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    [Fact]
    [Trait("Category", "DeletionSafety")]
    public void Rejects_Path_Through_Symbolic_Link_When_Supported()
    {
        var parent = TempRoot();
        var allowed = Path.Combine(parent, "allowed");
        var outside = Path.Combine(parent, "outside");
        var link = Path.Combine(allowed, "link");
        Directory.CreateDirectory(allowed);
        Directory.CreateDirectory(outside);
        var outsideFile = Path.Combine(outside, "keep.bin");
        File.WriteAllText(outsideFile, "keep");
        try
        {
            try { Directory.CreateSymbolicLink(link, outside); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                return; // Gate esercitato sui sistemi che consentono symlink non elevati.
            }

            var apparentChild = Path.Combine(link, "keep.bin");
            Assert.Throws<PathGuardException>(() => new PathGuard().ValidateForDelete(apparentChild, [allowed]));
            Assert.True(File.Exists(outsideFile));
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }
}
