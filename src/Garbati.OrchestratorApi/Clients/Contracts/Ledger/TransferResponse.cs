namespace Garbati.OrchestratorApi.Clients.Contracts.Ledger;

// Mirrors LedgerApi.Contracts.Responses.TransferResponse. ChainReference/Sequence/Purpose are only ever
// set for a chain leg (decision #45), which this service never posts to - kept here only so a response
// with those fields still deserializes without error.
public record TransferResponse(
    string Reference,
    string Status,
    string? FailureReason = null,
    string? ChainReference = null,
    int? Sequence = null,
    string? Purpose = null);
