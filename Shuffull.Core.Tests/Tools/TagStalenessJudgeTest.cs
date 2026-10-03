using Microsoft.Extensions.Configuration;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tools;
using Shuffull.Metadata.Tools;
using Shuffull.Shared.Tools;

namespace Shuffull.Core.Tests.Tools;

public class TagStalenessJudgeTest
{
    private const string StrongModel = "gpt-strong";
    private const string WeakModel = "gpt-weak";

    private static TagStalenessJudge Judge(Dictionary<string, int>? strengths = null, string? strongModel = StrongModel)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:OpenAI:StrongModelName"] = strongModel,
            })
            .Build();
        return new TagStalenessJudge(new ModelStrengths(strengths), config);
    }

    private static Song NewSong(string? tagModel, bool exploratory = false, bool metadataLocked = false) => new()
    {
        SongId = IdGenerator.Generate(),
        Name = "Test Song",
        FileExtension = ".mp3",
        FileHash = "hash",
        TagModel = tagModel,
        Exploratory = exploratory,
        MetadataLocked = metadataLocked,
        Version = DateTime.UtcNow,
    };

    private static readonly Dictionary<string, int> DefaultStrengths = new()
    {
        [StrongModel] = 30,
        [WeakModel] = 10,
    };

    [Fact]
    public void WeakTaggedSong_IsStale()
        => Assert.True(Judge(DefaultStrengths).IsStale(NewSong(WeakModel)));

    [Fact]
    public void NullTagModel_IsStale()
        => Assert.True(Judge(DefaultStrengths).IsStale(NewSong(null)));

    [Fact]
    public void StrongTaggedSong_IsNotStale()
        => Assert.False(Judge(DefaultStrengths).IsStale(NewSong(StrongModel)));

    [Fact]
    public void ExploratorySong_IsNeverStale()
        => Assert.False(Judge(DefaultStrengths).IsStale(NewSong(null, exploratory: true)));

    [Fact]
    public void LockedSong_IsNeverStale()
        => Assert.False(Judge(DefaultStrengths).IsStale(NewSong(WeakModel, metadataLocked: true)));

    [Fact]
    public void UnregisteredStrongModel_FailsSafe_NothingIsStale()
        => Assert.False(Judge(strengths: null).IsStale(NewSong(null)));

    [Fact]
    public void MissingStrongModelConfig_FailsSafe_NothingIsStale()
        => Assert.False(Judge(DefaultStrengths, strongModel: null).IsStale(NewSong(null)));
}
