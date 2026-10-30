using Shuffull.Core.Features.Listens.RecordListens;

namespace Shuffull.Api.Commands.Listens.RecordListens;

/// <summary>Body for the batched listen log: the plays the app's outbox has queued, oldest first.</summary>
public record RecordListensRequest(ListenItem[]? Listens);
