using Microsoft.Data.Sqlite;
using PacePlanner.Core.Pacing;

namespace PacePlanner.Data.Tests;

public sealed class PlanRepositoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PacePlannerTests", Guid.NewGuid().ToString("N"));

    private string DbPath => Path.Combine(_dir, "sub", "plans.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static SavedPlan Sample(string name = "台北馬") => new()
    {
        Name = name,
        GpxFileName = "taipei.gpx",
        GpxContent = "<gpx>中文</gpx>",
        RaceDistance = RaceDistance.Marathon,
        DistanceKm = 42.195,
        TargetSeconds = 12600,
        Strategy = PaceStrategy.NegativeSplit,
        NegativeSplitSeconds = 8,
        AidMode = AidStationMode.CustomList,
        AidIntervalKm = 5,
        AidCustomKms = [5, 12.5, 30],
        AidStopSeconds = 25,
    };

    [Fact]
    public void Insert_ThenReopenDatabase_ReturnsIdenticalContent()
    {
        var id = new PlanRepository(DbPath).Insert(Sample());

        SqliteConnection.ClearAllPools();
        var loaded = new PlanRepository(DbPath).Get(id);

        Assert.NotNull(loaded);
        Assert.Equal(Sample() with { Id = id, UpdatedAt = loaded.UpdatedAt }, loaded);
    }

    [Fact]
    public void List_ReturnsSummariesMostRecentlyUpdatedFirst()
    {
        var repo = new PlanRepository(DbPath);
        var a = repo.Insert(Sample("A"));
        var b = repo.Insert(Sample("B"));
        repo.Update(repo.Get(a)! with { TargetSeconds = 13000 });

        var list = repo.List();

        Assert.Equal(new[] { a, b }, list.Select(p => p.Id));
        Assert.Equal("A", list[0].Name);
    }

    [Fact]
    public void Update_PersistsAllFields()
    {
        var repo = new PlanRepository(DbPath);
        var id = repo.Insert(Sample());
        var changed = repo.Get(id)! with
        {
            Strategy = PaceStrategy.GradeAdjusted,
            AidMode = AidStationMode.None,
            AidCustomKms = [],
            GpxContent = null,
            GpxFileName = null,
        };

        repo.Update(changed);

        Assert.Equal(changed with { UpdatedAt = repo.Get(id)!.UpdatedAt }, repo.Get(id));
    }

    [Fact]
    public void Duplicate_CopiesEverythingUnderNewName()
    {
        var repo = new PlanRepository(DbPath);
        var id = repo.Insert(Sample());

        var copyId = repo.Duplicate(id, "台北馬 (複本)");

        var copy = repo.Get(copyId)!;
        Assert.NotEqual(id, copyId);
        Assert.Equal("台北馬 (複本)", copy.Name);
        Assert.Equal(Sample("台北馬 (複本)") with { Id = copyId, UpdatedAt = copy.UpdatedAt }, copy);
        Assert.Equal(2, repo.List().Count);
    }

    [Fact]
    public void Rename_ChangesOnlyName()
    {
        var repo = new PlanRepository(DbPath);
        var id = repo.Insert(Sample());

        repo.Rename(id, "  新名字 ");

        var renamed = repo.Get(id)!;
        Assert.Equal("新名字", renamed.Name);
        Assert.Equal(Sample("新名字") with { Id = id, UpdatedAt = renamed.UpdatedAt }, renamed);
    }

    [Fact]
    public void Delete_RemovesPlan()
    {
        var repo = new PlanRepository(DbPath);
        var id = repo.Insert(Sample());

        repo.Delete(id);

        Assert.Null(repo.Get(id));
        Assert.Empty(repo.List());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyName_IsRejected(string name)
    {
        var repo = new PlanRepository(DbPath);
        var id = repo.Insert(Sample());

        Assert.Throws<ArgumentException>(() => repo.Insert(Sample(name)));
        Assert.Throws<ArgumentException>(() => repo.Rename(id, name));
    }

    [Fact]
    public void OperationsOnMissingId_Throw()
    {
        var repo = new PlanRepository(DbPath);

        Assert.Throws<KeyNotFoundException>(() => repo.Rename(999, "x"));
        Assert.Throws<KeyNotFoundException>(() => repo.Duplicate(999, "x"));
        Assert.Throws<KeyNotFoundException>(() => repo.Update(Sample() with { Id = 999 }));
    }
}
