using RestCue.Core.Domain;
using RestCue.Core.Reminders;

namespace RestCue.Core.UsageEvents;

public abstract record UsageEventPayload;

public sealed record ReminderDismissedPayload(ReminderResult Result) : UsageEventPayload;

public sealed record RestDebtLevelChangedPayload(RestDebtLevel Previous, RestDebtLevel Current) : UsageEventPayload;
