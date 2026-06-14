using FluentValidation.TestHelper;
using Shuffull.Core.Features.Playlists.AddSongToPlaylist;
using Shuffull.Core.Tests.Tools;

namespace Shuffull.Core.Tests.Features.Playlists.AddSongToPlaylist;

public class AddSongToPlaylistValidatorTest
{
    private static AddSongToPlaylistCommand SafeValue()
        => new("user-123", "playlist-123", "song-123");

    public static IEnumerable<object[]> CreateErrorValues() => new List<object[]>
    {
        (SafeValue() with { UserId = null! }).ToObjectArray(),
        (SafeValue() with { UserId = "" }).ToObjectArray(),
        (SafeValue() with { PlaylistId = null! }).ToObjectArray(),
        (SafeValue() with { PlaylistId = "" }).ToObjectArray(),
        (SafeValue() with { SongId = null! }).ToObjectArray(),
        (SafeValue() with { SongId = "" }).ToObjectArray(),
    }.ToArray();

    public static IEnumerable<object[]> CreateValidValues() => new List<object[]>
    {
        SafeValue().ToObjectArray(),
        (SafeValue() with { PlaylistId = "other-playlist", SongId = "other-song" }).ToObjectArray(),
    }.ToArray();

    [Theory]
    [MemberData(nameof(CreateErrorValues))]
    public void InvalidCommand_ProducesValidationErrors(AddSongToPlaylistCommand command)
    {
        var validator = new AddSongToPlaylistValidator();
        var errors = validator.TestValidate(command).Errors;
        Assert.NotEmpty(errors);
    }

    [Theory]
    [MemberData(nameof(CreateValidValues))]
    public void ValidCommand_ProducesNoErrors(AddSongToPlaylistCommand command)
    {
        var validator = new AddSongToPlaylistValidator();
        var errors = validator.TestValidate(command).Errors;
        Assert.Empty(errors);
    }
}
