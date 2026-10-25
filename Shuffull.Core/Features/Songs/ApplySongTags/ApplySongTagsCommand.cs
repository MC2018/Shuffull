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
/// <para><see cref="TagRevision"/> is the prompt revision the tags came from (Shuffull.Metadata TagRevision). 0 from a
/// producer that predates revisions, which leaves the song due for a re-score.</para>
///
/// <para>This writes tags, BPM/heaviness, <c>TagModel</c> and <c>TagRevision</c> only. Name and artists are untouched, so a curator's
/// identity corrections survive a re-tag.</para>
/// </summary>
public record ApplySongTagsCommand(
    string SongId,
    GeneratedSongTags Tags,
    string TagModel,
    int TagRevision = 0) : IRequest<Result<ApplySongTagsResponse>>;

public record ApplySongTagsResponse(string SongId, int TagCount, string TagModel);
