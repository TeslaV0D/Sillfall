# ADR-0002: AwesomeAssertions instead of FluentAssertions

- Status: accepted
- Date: 2026-11-11
- Milestone: M0

## Context

PLAN.md names FluentAssertions for tests. Since version 8, FluentAssertions is under an Xceed license that requires a paid license for commercial use. Sillfall is intended to be sold.

## Options

1. **FluentAssertions 8 with a paid license.** Ongoing cost for a test-only library.
2. **Pin FluentAssertions 7.x.** Last version under Apache 2.0. No new features.
3. **AwesomeAssertions.** Community fork of FluentAssertions under Apache 2.0, actively maintained, same API (`value.Should().Be(…)`), different namespace.

## Decision

Option 3: **AwesomeAssertions**. No license cost, no frozen dependency, and tests read the same way the plan describes.

## Consequences

- Test projects use `AwesomeAssertions` as a global `using`.
- Switching back later is a package and namespace swap.
- The maintainer can reverse this decision if a FluentAssertions license is available.
