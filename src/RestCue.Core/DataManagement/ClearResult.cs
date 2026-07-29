namespace RestCue.Core.DataManagement;

public sealed record ClearResult(bool Succeeded, int AffectedRowCount, string? ErrorMessage);
