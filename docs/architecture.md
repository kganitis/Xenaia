# Architecture

How the solution is structured now: present tense, facts only. When code
changes the structure or Core's surface, this page is updated in the same
unit of work.

## Solution layout

Clean Architecture, dependencies point inward. 24 src projects, layered:

- `Xenaia.Core` - shared kernel (see below)
- `Xenaia.Core.Ai` - AI ports and retrieval pipeline (empty until T6)
- `Xenaia.Domain.Bookings` - booking bounded context: Booking/Product/Channel/Availability aggregates plus catalog entities (see below)
- `Xenaia.Data`, `Xenaia.Data.PostgreSql` - EF context + outbox store, the bookings persistence configs, and the PostgreSQL provider with the `InitialCreate` migration (see Data layer below); `Xenaia.Data.{MySql,MsSql}` remain empty seams
- `src/modules/Xenaia.Modules.Triage` - the rules engine, declarative actions, the booking-urgency and booking-lookup processors, and the sweep + polling pipeline (see below); `src/modules/Xenaia.Modules.Sync` - the five sync flows, their channels, and the durable-queue processors (see below); `src/modules/Xenaia.Modules.{Concierge,AgentQa,Rostering}` remain empty
- `src/adapters/Xenaia.Adapters.Freshdesk` - `IHelpdeskProvider` over the Freshdesk v2 REST API; `src/adapters/Xenaia.Adapters.BrightTide` - `IBookingSystemProvider` over the BrightTide REST API; `src/adapters/Xenaia.Adapters.GoogleWorkspace` - `ISpreadsheetGateway` over the Google Sheets API (see below); the rest of `src/adapters/Xenaia.Adapters.*` remain empty
- `src/extensions/*` - extension contract + samples (empty)
- `src/hosts/{Xenaia.Api,Xenaia.McpServer}` - the only composition roots (`Xenaia.Api` is wired: Core + Bookings domain + PostgreSql data + Triage/Freshdesk behind `Providers:Helpdesk`, Sync + BrightTide behind `Providers:BookingSystem`, GoogleWorkspace behind `Providers:Spreadsheet`, the `/api/*` sync ingestion surface behind the API-key gate, and `/health`; `Xenaia.McpServer` is still a template stub)
- `tests/Xenaia.ArchitectureTests` - layer rules, enforced in CI
- `tests/Xenaia.Core.Tests` - kernel unit tests
- `tests/Xenaia.Data.Tests` - data-layer tests against real Postgres (Testcontainers)
- `tests/Xenaia.Modules.Triage.Tests` - rule pack loader/evaluator/action-folding unit tests, the Meridian Trails golden tests, and the Milestone 1 end-to-end test (real engine, real actions, real urgency processor; only the helpdesk and the clock are test doubles)
- `tests/Xenaia.PortContracts` - the reusable port contract suites and their reference doubles: `HelpdeskProviderContract` + `InMemoryHelpdeskProvider`, the booking-system contract + `InMemoryBookingSystemProvider`, the spreadsheet contract + `InMemorySpreadsheetGateway`, and shared in-memory store fakes (`FakeBookingStore`); built with `IsTestProject=false` so solution-level `dotnet test` does not try to discover tests in it directly, only consume it from other test projects
- `tests/Xenaia.Adapters.Freshdesk.Tests` - runs `HelpdeskProviderContract` against `FreshdeskHelpdeskProvider` over a stateful fake HTTP server
- `tests/Xenaia.Modules.Sync.Tests` - the five flows' unit tests (catalog, inbound sweep, outbound enqueue/push, availability patch/fetch/push, sheet write-back) and the Milestone 2 end-to-end test (every module service real; only the booking system, the spreadsheet gateway, and the clock are test doubles)
- `tests/Xenaia.Adapters.BrightTide.Tests` - runs the booking-system contract against `BrightTideClient` over a stateful fake HTTP server, plus the vendor mapping and three-step create tests
- `tests/Xenaia.Adapters.GoogleWorkspace.Tests` - the spreadsheet gateway's mapping, batching, and 429 retry tests against a fake Sheets API seam

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
  sync state individually (a recorded deviation from the source model);
  catalog sync (Sync module, below) confirms this by syncing state at the
  aggregate and catalog-entity level rather than per child row.
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
- **Booking-system port**: `IBookingSystemProvider`
  (`Xenaia.Domain.Bookings.Providers`) is the driven port for the external
  booking system (bookings, availability, products/options). It is a
  deliberate exception to the consumer-ownership rule: three separate
  consumers use it (the Sync module, Triage's booking-lookup processor,
  and the future MCP host), and Domain.Bookings is the only project all of
  them may reference, so the port lives here rather than in any one
  consumer. Provider-agnostic snapshots (`BookingSnapshot`,
  `ProductSnapshot`, `AvailabilityTimeslot`, `AvailabilityUpdate`) and the
  `BookingDraft` cross the port; `BookingSystemException` and
  `BookingSystemEntityNotFoundException` are its error contract.
  `BookingIngestService` maps an inbound snapshot onto a `Booking`
  aggregate (validating the code, setting the sync direction).
- **Outbound booking requests**: `OutboundBookingRequest` (an `ISyncTracked`
  entity) is the durable queue for locally originated writes. A local
  booking is not born from a draft: the booking system is the system of
  record and assigns the code, so a create is persisted as a request
  (payload = the draft JSON), pushed to the vendor, and the local `Booking`
  row is created only when the confirmed snapshot comes back and is
  ingested. A cancel is a request whose payload is the booking code.
- **Stores**: `IBookingStore`, `ICatalogStore`, `IAvailabilityStore`,
  `IOutboundBookingRequestStore`, `ISyncCheckpointStore` are the
  domain-owned persistence ports the Sync module drives (EF
  implementations live in the Data layer).
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
- **Booking lookup**: `BookingLookupProcessor` (name `booking-lookup`) is
  the first booking-aware processor: it resolves a ticket's `bookingCode`
  capture against the local `IBookingStore`, falling back to the
  `IBookingSystemProvider` (and ingesting the result) when the local copy
  has not seen it, and leaves a private note summarizing the outcome. It is
  purely informational (no tags, no status changes). It registers only when
  the host has a booking system configured; a rule pack naming it without
  one fails startup through `TriageOptionsValidator`. It lives in the
  Triage module rather than in Domain.Bookings because a domain project
  cannot reference a module; the domain-owned `IBookingSystemProvider` port
  is what keeps the booking-system dependency pointing inward.
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

## Xenaia.Modules.Sync surface

- **Five flows**: the module keeps the local catalog, bookings, and
  availability in step with the booking system across five flows:
  availability outbound (local edits pushed to the vendor), availability
  inbound (vendor state fetched into local rows), bookings inbound (vendor
  bookings swept into local aggregates), bookings outbound (local creates
  and cancels pushed to the vendor), and catalog sync (products, options,
  and participant types pulled into the domain entities). Each flow is
  gated independently by a `Sync:Flows:*` boolean (default true); the gate
  turns the flow's hosted loop on or off, while its application services
  always register so the REST surface can drive them on demand.
- **Ports**: the module drives the domain-owned `IBookingSystemProvider`
  (booking system) and owns `ISpreadsheetGateway`
  (`Xenaia.Modules.Sync.Spreadsheets`), the spreadsheet port whose only
  consumer is Sync, so it lives with its consumer per the ownership rule.
  `SheetValueRange` and A1-notation ranges cross that port; adapters
  implement it.
- **Durable queue**: the sync-tracked database rows are the queue. An edit
  sets a row (or an `OutboundBookingRequest`) to `Pending` and persists it;
  a `TryClaimAsync` flips exactly one `Pending` row to `Processing` (the
  claim, at-least-once and idempotent); success marks it `Synced`, an
  exhausted push marks it `Failed`. An in-memory channel is only a wake-up
  hint so the processor need not poll: losing a channel message never loses
  work, because the row is still `Pending` in the database. The bookings
  channel is sized by `Sync:Bookings:ChannelCapacity` (default 1000), the
  availability channel by `Sync:Availability:ChannelCapacity` (default
  10000).
- **Startup recovery**: a row stuck in `Processing` (a host that died
  mid-push) would otherwise never be reclaimed, so each outbound processor
  runs recovery once at startup: `ResetProcessingAsync` requeues every
  `Processing` row back to `Pending` and re-enqueues it onto the channel,
  before the drain loop begins.
- **Outbound bookings**: `OutboundBookingEnqueuer` validates a draft
  against the catalog (unknown product/option fails at the edge) and
  persists an `OutboundBookingRequest`; `BookingPusher` claims it, calls
  the vendor, and (for a create) ingests the confirmed snapshot back as a
  local `Booking` in the `Outbound` direction. A cancel pushes the code and
  cancels the local aggregate. Retries back off with jitter and truncate
  the stored error; a permanent vendor "not found" fails without retrying;
  exhaustion sends a `Warning` notification. Booking update push is
  deliberately out of scope for now (create and cancel only).
- **Availability and the canonical sheet layout**: `AvailabilityPatchService`
  expands each incoming patch into per-timeslot rows and dedups
  value-aware against the queue; `AvailabilityPusher` claims a row, pushes
  the update, and buffers sheet write-backs into a per-drain
  `SheetWriteBuffer` that flushes once per spreadsheet at the end of a
  cycle. The get-sheet layout is fixed for the whole feature: input columns
  A = time (blank for slotless), B = product external id, C = option
  external id, D = participant aliases, E = combination string
  (`productId|optionId|from|to`); write-back columns F = vacancies,
  G = timestamp, H = stop-sales. `AvailabilityFetchService` reads A:E,
  fetches each combination from the vendor, upserts local rows, and writes
  the F:G status back; `SheetWriteBuffer` resolves a claimed row against a
  fresh A:E read and writes F:H. The sheet gateway is an optional
  dependency of the outbound pusher: with no spreadsheet provider
  registered it stays null and no write-back is attempted.
- **Catalog and inbound**: `CatalogSyncService` pulls products, options,
  and participant types into the domain catalog entities (matching options
  by external id and participant types by alias, leaving existing options
  untouched), throttling between products. `BookingInboundSweep` queries
  the vendor from a checkpoint (`ISyncCheckpointStore`) minus an overlap
  window, ingests each booking, advances the checkpoint only on success,
  and skips a malformed code without failing the sweep.
- **REST surface and the API-key gate**: `SyncEndpoints` maps the module's
  ingestion routes under `/api` (availability patch/sync, bookings
  create/cancel, catalog refresh); `ApiKeyMiddleware` gates every `/api/*`
  request behind the static `Api:ApiKey` (fail closed: an unset key returns
  503 so a tenant never exposes an open write surface; a missing or wrong
  `X-Api-Key` header returns 401). Handlers stay thin, mapping the module's
  documented failures onto 400/404/409/503.
- **Composition**: `AddSyncModule(IConfiguration, spreadsheetConfigured)`
  registers validated `SyncOptions` (section `Sync`) and its validator,
  the five flows' channels and application services (scoped/singleton, for
  the endpoints), and one hosted background service per flow gated by its
  `Sync:Flows:*` flag. When a spreadsheet provider is registered the host
  passes `spreadsheetConfigured: true`, which makes the validator require
  the configured sheet names.

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

## BrightTide adapter

- `Xenaia.Adapters.BrightTide`: `BrightTideClient : IBookingSystemProvider`
  over the BrightTide REST API via a typed `HttpClient` with the standard
  resilience handler. Vendor DTOs, snake_case wire shapes, the vendor date
  formats, and the multi-step create flow never leak past this class.
- Auth is a static `API-Key` header set on the typed client from
  `BrightTideOptions.ApiKey`; the base address, a 30 second timeout, and the
  resilience handler are configured at registration.
- A create is a three-step dance: `bookings/start` (the vendor assigns the
  code and secret code), `bookings/items` (authenticated by the returned
  `Secret-Code` header), then `bookings/complete` (which returns the full
  confirmed snapshot). The port's single `CreateBookingAsync` hides all
  three.
- Error contract: a 404 on a read maps to null (unknown code) rather than
  throwing; a 404 on cancel throws `BookingSystemEntityNotFoundException`;
  any other non-success maps to `BrightTideApiException` (a
  `BookingSystemException` carrying the status code and a truncated body),
  so callers treat it as a retryable transport fault.
- Resilience: transport failures, timeouts, and a Polly open circuit or
  timeout rejection are all translated into `BookingSystemException` at the
  send boundary, so nothing vendor- or Polly-specific crosses the port.
- `BrightTideOptions` (section `Adapters:BrightTide`: `BaseUrl`, `ApiKey`)
  is never tracked; it lives in tenant-local config or environment.
  `AddBrightTideBookingSystem(IConfiguration)` registers the validated
  options and the typed client as the scoped `IBookingSystemProvider`.
- Proved against the reusable booking-system contract over a stateful fake
  HTTP server (`tests/Xenaia.Adapters.BrightTide.Tests`), the same suite
  `InMemoryBookingSystemProvider` runs.

## GoogleWorkspace adapter

- `Xenaia.Adapters.GoogleWorkspace`: `GoogleSpreadsheetGateway :
  ISpreadsheetGateway` over the Google Sheets v4 API, reached through an
  `ISheetsApi` seam so the mapping and resilience logic are unit-testable
  without Google. Cell-value boxing, batch range limits, and vendor
  exceptions never leak past this class.
- Auth is a Google service-account credential from the
  `GOOGLE_APPLICATION_CREDENTIALS_JSON` environment variable (falling back
  to Application Default Credentials), scoped to Sheets read/write. The
  `SheetsService` and the `ISheetsApi` seam are singletons; the gateway is
  scoped, matching the other provider registrations.
- Reads coerce boxed numeric cells to strings under the invariant culture
  (a comma-decimal locale would otherwise corrupt values); null cells
  become empty strings.
- Resilience: batch writes are chunked to the API's per-request range
  limit; a 429 ("rate limited") is retried up to twice with a fixed delay
  aligned to the per-minute write quota (the delayer is injectable for
  tests), and a third failure propagates. `EnsureTabAsync` is idempotent,
  swallowing the already-exists race.
- `AddGoogleWorkspaceSpreadsheets()` registers the credential, the seam,
  and the scoped `ISpreadsheetGateway`; the credential comes from
  environment, never from a tracked file.
- Proved against the reusable spreadsheet contract and mapping/batching/
  retry tests over a fake Sheets API seam
  (`tests/Xenaia.Adapters.GoogleWorkspace.Tests`), the same contract
  `InMemorySpreadsheetGateway` runs.

## Provider selection

`Xenaia.Api`'s `Program.cs` reads provider switches from configuration and
fails closed on an unsupported value:

- `Providers:Helpdesk`: absent or empty leaves triage unregistered (logged
  at startup as disabled), `freshdesk` registers both `AddTriageModule` and
  `AddFreshdeskHelpdesk`, any other value throws.
- `Providers:BookingSystem`: absent or empty leaves the Sync module
  unregistered, `brighttide` registers `AddSyncModule` and
  `AddBrightTideBookingSystem`, any other value throws.
- `Providers:Spreadsheet`: absent or empty leaves the spreadsheet flows
  without a gateway (availability inbound returns 503, outbound skips
  write-back). `googleworkspace` with a booking system also on registers
  `AddGoogleWorkspaceSpreadsheets` and tells the Sync module the sheet names
  are now required; `googleworkspace` with no booking system is a logged
  no-op (nothing consumes the gateway); any other value throws.

## Enforcement

`tests/Xenaia.ArchitectureTests` checks compiled assembly references
against the forbidden-layer table (reflection-based; type-level rules can
be added when cross-project types exist). CI runs build + tests + gitleaks.
