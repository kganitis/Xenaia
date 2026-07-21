# Architecture

How the solution is structured now.



Present tense, facts only. When code changes the structure
or Core's surface, this page is updated in the same unit of work.


## Solution layout

Clean Architecture, dependencies point inward. 24 src projects, layered:

- `Xenaia.Core` - shared kernel (see below)
- `Xenaia.Core.Ai` - AI ports and retrieval pipeline (empty until T6)
- `Xenaia.Domain.Bookings` - booking bounded context: Booking/Product/Channel/Availability aggregates plus catalog entities (see below)
- `Xenaia.Data`, `Xenaia.Data.PostgreSql` - EF context + outbox store, the bookings persistence configs, and the PostgreSQL provider with the `InitialCreate` migration (see Data layer below); `Xenaia.Data.{MySql,MsSql}` remain empty seams
- `src/modules/Xenaia.Modules.Triage` - the rules engine, declarative actions, the booking-urgency processor, and the sweep + polling pipeline (see below); `src/modules/Xenaia.Modules.{Concierge,Sync,AgentQa,Rostering}` remain empty
- `src/adapters/Xenaia.Adapters.Freshdesk` - `IHelpdeskProvider` over the Freshdesk v2 REST API (see below); the rest of `src/adapters/Xenaia.Adapters.*` remain empty
- `src/extensions/*` - extension contract + samples (empty)
- `src/hosts/{Xenaia.Api,Xenaia.McpServer}` - the only composition roots (`Xenaia.Api` is wired: Core + Bookings domain + PostgreSql data + Triage/Freshdesk behind `Providers:Helpdesk` + `/health`; `Xenaia.McpServer` is still a template stub)
- `tests/Xenaia.ArchitectureTests` - layer rules, enforced in CI
- `tests/Xenaia.Core.Tests` - kernel unit tests
- `tests/Xenaia.Data.Tests` - data-layer tests against real Postgres (Testcontainers)
- `tests/Xenaia.Modules.Triage.Tests` - rule pack loader/evaluator/action-folding unit tests, the Meridian Trails golden tests, and the Milestone 1 end-to-end test (real engine, real actions, real urgency processor; only the helpdesk and the clock are test doubles)
- `tests/Xenaia.PortContracts` - `HelpdeskProviderContract`, the reusable helpdesk-port contract suite, plus `InMemoryHelpdeskProvider`; built with `IsTestProject=false` so solution-level `dotnet test` does not try to discover tests in it directly, only consume it from other test projects
- `tests/Xenaia.Adapters.Freshdesk.Tests` - runs `HelpdeskProviderContract` against `FreshdeskHelpdeskProvider` over a stateful fake HTTP server

## Xenaia.Core surface

- **Options**: `ISectionOptions` (static `SectionName`) + `AddValidatedOptions<T>`:
  binds, validates annotations + `IValidateOptions`, `ValidateOnStart`;
  missing section throws at registration (fail closed).
- **Domain**: `Entity<TId>` (equality by type+id), `AggregateRoot<TId>`
  (`Raise`/`DomainEvents`/`DequeueDomainEvents`), `IDomainEvent` (`OccurredAt`).
- **Events**: `IDomainEventHandler<TEvent>`, `IDomainEventDispatcher`,
  DI-based `DomainEventDispatcher` (no mediator dependency; reflection
  cached per event type, handler exceptions surface unwrapped).
- **Outbox**: `OutboxMessage` (`From(IDomainEvent)` / `ToDomainEvent()`,
  JSON payload, assembly-qualified type name), `IOutboxStore` port, and
  `OutboxDrainerService` (polling `BackgroundService`; parks poison
  messages by setting `Error`, single-instance by design) with validated
  `OutboxOptions` (section `Outbox`).
- **Notifications**: `Notification` record, `INotificationChannelSender`
  (adapters implement), `INotificationService` fan-out that logs and
  continues on per-channel failure.
- **Tenancy**: `TenantProfileOptions` (section `Tenant`: BusinessName,
  TimeZone, Locales, BusinessHours) + `TenantProfileValidator`
  (unknown TZ/locale, duplicate weekday windows, inverted windows).
- **Business hours**: `IBusinessHoursService` (`IsOpenNow` / `IsOpenAt` /
  `NextOpeningAfter`) evaluated in the tenant's time zone via
  `TimeProvider` for testability.
- **Composition**: `AddXenaiaCore(IConfiguration)`; hosts call it first.

## Xenaia.Domain.Bookings surface

- **Aggregates**: `Booking` (with `BookingItem`/`BookingExtra`/
  `BookingGiftCard`/`BookingPayment` children), `Product` (with
  `ProductOption`/`ProductOptionExtra` children), `Channel`,
  `Availability`. Each raises domain events through `AggregateRoot<TId>`
  and enforces its own invariants, throwing a `*RuleViolationException`
  per aggregate on violation.
- **Catalog**: `Category`, `Extra`, `ParticipantType`, `PaymentType` -
  reference entities the aggregates above point at.
- **Sync**: `Xenaia.Domain.Bookings.Sync` - `SyncState` is an immutable
  value object owning the transition table (`Pending -> Processing ->
  Synced | Failed`, plus `Requeue` back to `Pending`); every move either
  returns a new state or throws `InvalidSyncTransitionException`, so the
  table in `SyncState` is the only statement of the rules. `ISyncTracked`
  is the entity-facing contract (`ClaimForSync`/`MarkSynced`/
  `MarkSyncFailed`/`RequeueSync`), implemented by the four aggregate roots
  (`Booking`, `Product`, `Channel`, `Availability`) and the four catalog
  entities (`Category`, `Extra`, `ParticipantType`, `PaymentType`). Child
  entities (`BookingItem`, `BookingExtra`, `BookingGiftCard`,
  `BookingPayment`, `ProductOption`, `ProductOptionExtra`) do not track
  sync state individually (a recorded deviation from the source model,
  revisited at T5).
- **Codes**: `Xenaia.Domain.Bookings.Codes` - `BookingCode`/`ProductCode`
  value objects validated against tenant-configured formats
  (`CodeFormat`/`CodeFormats`), bound from validated `BookingsFormatOptions`
  (section `Tenant:Bookings`) and `BookingsFormatValidator`
  (fail-closed at startup); malformed codes throw `InvalidCodeException`,
  malformed configured formats throw `InvalidCodeFormatException`.
- **Events**: `Bookings/Events` (`BookingReceived`, `BookingCancelled`,
  `BookingSyncFailed`) carry natural keys (booking code, not the DB id),
  because database ids are unassigned at `SavingChanges` time when the
  event is raised.
- **Composition**: `AddBookingsDomain(IServiceCollection, IConfiguration)`
  registers the validated tenant code-format options; called from
  `Xenaia.Api`'s `Program.cs` alongside `AddXenaiaCore`.

## Data layer

- `Xenaia.Data`: `XenaiaDbContext` (configurations discovered from the
  assembly), `OutboxMessageConfiguration` (filtered index on the
  unprocessed predicate), per-entity configurations for every Bookings
  aggregate and catalog entity under `Configuration/` (one file per
  entity type), `SyncStateMapping` (maps `SyncState` as an EF complex
  type across the sync-tracked aggregates so its columns stay inline on
  the owning table), `ConcurrencyMapping` (maps PostgreSQL `xmin` as the
  optimistic concurrency token; shared across configs as recorded pre-v1
  pragmatism, since only the PostgreSQL provider exists today - it moves
  behind a provider seam when a second provider arrives, spec section
  5.3), `AuditStamps`/`AuditStampInterceptor` (shadow `CreatedAt`/
  `UpdatedAt` columns stamped by persistence, never touched by domain
  code), `DomainEventsToOutboxInterceptor` (drains raised domain events
  from tracked aggregates into outbox rows inside the same
  `SaveChanges`, completing the transactional outbox that the Core
  drainer services), `EfOutboxStore : IOutboxStore`, validated
  `DataOptions` (section `Data`: ConnectionString, AutoMigrate), and
  `AddXenaiaData` (provider-agnostic registrations, including both
  interceptors).
- `Xenaia.Data.PostgreSql`: `AddXenaiaPostgreSql` (Npgsql + snake_case
  naming convention, migrations live in this assembly),
  `MigrationHostedService` (applies migrations at startup when
  `Data:AutoMigrate` is true, aborts startup on failure), and the single
  `InitialCreate` migration (schema only, regenerated to include the
  Bookings tables, still regenerated until v1).
- Testing: `tests/Xenaia.Data.Tests` runs against real Postgres via
  Testcontainers (requires Docker); the real migration is applied, never
  `EnsureCreated`.

## Xenaia.Modules.Triage surface

- **Port**: `IHelpdeskProvider` (owned by Triage; adapters implement it) -
  `GetOpenTicketsAsync` (open tickets oldest first, may include already-
  triaged ones), `UpdateTicketAsync` (status/priority/tags/custom fields;
  throws `HelpdeskTicketNotFoundException` for an unknown id),
  `AddPrivateNoteAsync`. Provider-agnostic `HelpdeskTicket`/`TicketUpdate`/
  `TicketStatus`/`TicketPriority` carry canonical field names across the
  port. `HelpdeskProviderContract` (`tests/Xenaia.PortContracts`) is the
  reusable suite every adapter must pass; it runs against both
  `InMemoryHelpdeskProvider` (also in PortContracts) and the Freshdesk
  adapter.
- **Rule packs**: YAML, one file per tenant, loaded and validated
  fail-closed at startup by `RulePackLoader` into `RulePack { Version,
  UnmatchedCategory, Rules }`. Validation checks structure (unknown
  top-level or per-rule keys), regex compilation, and capture-token
  references (an `addNote`/`setCustomFields` template naming `{x}` must
  reference a capture the rule's own patterns define); every problem
  collects into one `RulePackValidationException` rather than failing on
  the first. Processor-name references are validated separately by
  `TriageOptionsValidator` (the one component that knows the registered
  `ITicketProcessor` set), also fail-closed, also part of
  `ValidateOnStart`.
- **Matching**: `RuleEvaluator` walks rules in file order; first match
  wins. Within a rule, every `match` field must match (AND); within one
  field's pattern list, any pattern matching is enough (OR). `TicketField`
  is `Subject | Body | Sender | Channel`; body is matched against
  `TextNormalizer.Normalize(BodyHtml)` (HtmlAgilityPack strips tags,
  scripts, and styles, decodes entities, collapses whitespace, and joins
  table cells onto one line), never against raw HTML. Every tenant regex
  compiles through `TimedRegex` under a 250 ms match timeout; a timeout is
  treated as no-match (fail closed), mirroring `CodeFormat` in
  Domain.Bookings. Captures from matched conditions and `extract`
  patterns feed both action templates and named processors.
- **Actions**: declarative `RuleAction` records (`SetStatusAction`,
  `SetPriorityAction`, `AddTagsAction`, `SetCustomFieldsAction`,
  `AddNoteAction`) fold onto a `TicketUpdateDraft` via `ActionFolder`. A
  rule may additionally name one processor (`ITicketProcessor`) for logic
  too dynamic for a declarative action. `BookingUrgencyProcessor` (name
  `booking-urgency`) is the first: escalates a matched booking to
  `Urgent` when its extracted start time is imminent (a configurable
  proximity window) and falls inside tenant business hours, and sends a
  `Warning` notification through Core's fan-out; anything unparseable is
  logged and skipped rather than failing the sweep.
- **Idempotency**: `TriageConstants.MarkerTag` (`xenaia-triaged`) is
  stamped on every processed ticket; `TriageSweep` skips tickets that
  already carry it, so idempotency lives on the ticket itself, not in
  local database state. Per ticket, any `addNote` actions fire before the
  single `UpdateTicketAsync` call that carries status/priority/tags
  (including the marker); the marker riding the final call makes the
  whole sweep at-least-once (a crash after the note but before the update
  simply retries the ticket next poll; the note is the only step that can
  duplicate).
- **Unmatched tickets**: fall to `defaults.unmatchedCategory`;
  `TriageSweep` sends an `Info` needs-human notification through Core's
  fan-out and makes no other mutation beyond the marker tag.
- **Composition**: `AddTriageModule(IConfiguration)` registers validated
  `TriageOptions` (section `Tenant:Triage`: `RulePackPath`,
  `Urgency.ProximityHours`, `Urgency.DateTimeFormats`),
  `TriageOptionsValidator`, `RulePackProvider`, `RuleEvaluator`,
  `BookingUrgencyProcessor` as `ITicketProcessor`, `TriageSweep`, and the
  `TriagePollingService` hosted service.

## Freshdesk adapter

- `Xenaia.Adapters.Freshdesk`: `FreshdeskHelpdeskProvider : IHelpdeskProvider`
  over the Freshdesk v2 REST API via a typed `HttpClient` with the
  standard resilience handler. Vendor DTOs, integer status/priority
  codes, `cf_*` custom-field names, and pagination never leak past this
  class.
- Lists open tickets via the ticket list endpoint (paged, oldest first),
  not the search endpoint: search cannot return descriptions and carries
  tighter rate limits. The vendor list endpoint has no status parameter,
  so the open-status filter is applied client-side after each page is
  fetched.
- Status and priority map both directions through fixed integer tables
  (`Open`=2 .. `Closed`=5, `Low`=1 .. `Urgent`=4); an unrecognized vendor
  value is logged and mapped to a safe default (`Pending`, `Low`) rather
  than throwing.
- Custom fields map both directions through `FreshdeskOptions.FieldMap`
  (canonical name -> vendor `cf_*` name); canonical names are what cross
  the port, never vendor names.
- Tag updates are read-modify-write: adding a tag first re-fetches the
  ticket's current tags, since the vendor API has no additive tag
  endpoint.
- A 404 from any endpoint (update, note, tag re-fetch) throws
  `HelpdeskTicketNotFoundException`, the same exception every adapter
  must throw per the port contract.
- `FreshdeskOptions` (section `Adapters:Freshdesk`: `BaseUrl`, `ApiKey`,
  `FieldMap`, `PageSize`) is never tracked; it lives in tenant-local
  config or environment. `AddFreshdeskHelpdesk(IConfiguration)` registers
  the typed client and options.
- Proved against `HelpdeskProviderContract` over a stateful fake HTTP
  server (`tests/Xenaia.Adapters.Freshdesk.Tests`), the same suite the
  in-memory provider runs.

## Provider selection

`Xenaia.Api`'s `Program.cs` reads `Providers:Helpdesk` from configuration:
absent or empty leaves triage unregistered (logged at startup as
disabled), `freshdesk` registers both `AddTriageModule` and
`AddFreshdeskHelpdesk`, and any other value throws
`InvalidOperationException` at startup naming the unsupported value
(fail closed).

## Enforcement

`tests/Xenaia.ArchitectureTests` checks compiled assembly references
against the forbidden-layer table (reflection-based; type-level rules can
be added when cross-project types exist). CI runs build + tests + gitleaks.
