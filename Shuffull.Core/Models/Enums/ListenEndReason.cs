namespace Shuffull.Core.Models.Enums;

/// <summary>Why a <see cref="Database.Listen"/> ended. On the wire it is the lower-camel name ("finished", ...).</summary>
public enum ListenEndReason
{
    /// <summary>The song played to its end.</summary>
    Finished = 0,
    /// <summary>The user skipped forward to the next song.</summary>
    Skipped = 1,
    /// <summary>The user went back to the previous song.</summary>
    Previous = 2,
    /// <summary>Playback stopped: paused for good, the app closed, the queue ran out.</summary>
    Stopped = 3,
}
