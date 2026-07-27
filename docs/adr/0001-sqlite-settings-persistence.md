# ADR-0001: SQLite settings persistence and startup seam

## Context

RestCue's product contract requires local SQLite storage. Settings must survive an
application restart, invalid combinations must not be saved, corrupted storage must
recover to privacy-safe defaults, and Core must remain independent of SQLite and WPF.

## Decision

Store the validated `AppSettings` document in the contract's SQLite `settings`
key/value table under the `app_settings` key. Infrastructure owns schema creation and
the SQLite implementation of Core's `ISettingsRepository`. Schema version 1 is recorded
with `PRAGMA user_version`.

The WPF composition root creates the repository at startup. `ApplicationStartup`
loads settings before starting the tray lifecycle and exposes the loaded settings to
later application services. This seam accepts `ISettingsRepository` and
`IApplicationLifecycle`, so startup ordering and restart behaviour can be tested
without WPF or a real database.

If the database or stored settings document is invalid, the closed database is copied
to a timestamped `.bak` file, recreated with the current schema, and populated with
validated defaults. Foreground process-name collection therefore remains off during
recovery.

Recovery is restricted to SQLite's `CORRUPT` and `NOTADB` result codes, plus an
invalid serialized settings document. Operational failures such as `BUSY`, `LOCKED`,
permissions, or I/O errors are propagated and never delete the database. A schema
version newer than this application supports is also rejected without downgrade.

## Alternatives

- A JSON settings file was rejected because the product contract specifies SQLite and
  would create two persistence systems.
- One SQLite column per setting was rejected because every new optional setting would
  require a table migration. The key/value contract plus a versioned settings document
  keeps migrations at the document boundary.
- Loading settings lazily after the tray starts was rejected because consumers could
  briefly observe defaults rather than persisted values.

## Consequences

- `Microsoft.Data.Sqlite` is an Infrastructure dependency.
- Settings and future usage events can share the local `restcue.db` database.
- A malformed settings document causes conservative whole-database recovery in this
  first schema version; future usage-event storage must refine recovery so valid event
  data is not discarded.
- Startup currently loads and exposes settings; later feature slices inject them into
  timing and UI services.
- Startup failure writes only a fixed, non-sensitive diagnostic and exits without
  showing a modal window or taking focus.

## Review Trigger

Review this decision when usage-event persistence is added, settings require partial
recovery, schema version 2 is introduced, or multiple processes need concurrent access.
