# Conventions

Code and testing conventions for this repository.

## Code
- C# / .NET 10, nullable reference types enabled, warnings as errors.
- **Dependencies point inward** (Domain ← Application ← Infrastructure ← Hosts) and **ports live with their consumers**. The architecture tests are the canonical statement of these rules; when this file and a test disagree, the test wins and this file gets fixed.
- **Rich aggregates, not anemic POCOs.** State transitions are aggregate behavior. Identity-prone concepts (BookingCode, ProductCode, ChannelId) are value objects; their formats come from tenant config.
- **Modular DI:** every project exposes `AddXxxServices(IConfiguration)`; hosts are the only composition roots.
- **Validated options everywhere:** options class + `SectionName` const + data annotations + `ValidateOnStart`. No naked `IConfiguration` reads outside options binding.
- **Fail closed.** Missing tenant config or malformed rule packs stop the host at startup. Unmatched tickets route to the default "needs human" category, never guessed. Degradation ladders (rerank → RRF, hybrid → dense, below-confidence → hold) are explicit, never silent.
- Tenant-supplied regex is untrusted input: always evaluated with a timeout.
- Keep files focused and small. A large file is a signal it does too much.

## Testing
- **Milestone rule:** a module's suite is green before the next module begins. No exceptions.
- **New code is TDD** (failing test first).
- **Architecture tests** assert the dependency rules in CI from `T0` onward.
- **Port contract tests:** one reusable suite per port; every adapter must pass its port's suite. A new adapter starts by making the contract suite pass.
- **Rule-engine golden tests:** the sample rule pack + a corpus of fictional ticket fixtures → expected categorizations. Doubles as living documentation of the rule format.
- **Integration tests** are compose-based (Postgres + mocked external APIs) and cover the three milestone flows.
- **Injected clock and delayer.** Time-dependent code takes a `TimeProvider` and, where it waits between retries or throttled calls, an injectable delayer (`Func<TimeSpan, CancellationToken, Task>`). Tests pass a fake clock and a no-op or recording delayer, so backoff and throttle logic is asserted without real waiting; the production default delays against the injected clock, which a fake clock would otherwise stall.
- All fixtures, sample data, and test names reference the fictional demo tenant only.
