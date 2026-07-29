namespace RestCue.Core.DataManagement;

public sealed record ExportResult(bool Succeeded, string? WrittenPath, string? ErrorMessage);
