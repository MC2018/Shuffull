using MediatR;
using Nut.Results;
using Shuffull.Metadata.Models;

namespace Shuffull.Core.Features.Songs.ApplySongTags;

/// <summary>
/// Producer write-back: the tags an external engine generated for a song, plus the model that produced them.
///
/// <para>Deliberately NOT UpdateSong. That command is the curator's door — it rewrites Name/Artists and sets
/// <c>MetadataLocked = true</c>, which for a producer would permanently exclude every song it touched from all
/// future re-tagging (the work query requires an unlocked song or a real upgrade). It also never stamps
/// <c>TagModel</c>, so the song would stay "behind" forever and be handed back on the next poll — an infinite,
/// self-refilling work queue.</para>
///
/// <para>This writes tags, BPM/energy and <c>TagModel</c> only. Name and artists are untouched, so a curator's
/// identity corrections survive a re-tag.</para>
/// </summary>
public record ApplySongTagsCommand(
    string SongId,
    GeneratedSongTags Tags,
    string TagModel) : IRequest<Result<ApplySongTagsResponse>>;

public record ApplySongTagsResponse(string SongId, int TagCount, string TagModel);
