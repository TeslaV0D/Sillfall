# ADR-0002: AwesomeAssertions statt FluentAssertions

- Status: akzeptiert
- Datum: 2026-11-11
- Meilenstein: M0

## Kontext

PLAN.md nennt FluentAssertions für die Tests. Seit Version 8 steht FluentAssertions unter einer
Lizenz von Xceed, die für kommerzielle Nutzung eine bezahlte Lizenz verlangt. Weatherly soll
verkauft werden.

## Optionen

1. **FluentAssertions 8 mit bezahlter Lizenz.** Laufende Kosten für eine reine Test-Bibliothek.
2. **FluentAssertions 7.x einfrieren.** Letzte Version unter Apache 2.0. Bekommt keine neuen Features.
3. **AwesomeAssertions.** Community-Fork von FluentAssertions unter Apache 2.0, aktiv gepflegt,
   gleiche API (`value.Should().Be(…)`), anderer Namespace.

## Entscheidung

Option 3: **AwesomeAssertions**. Keine Lizenzkosten, kein eingefrorener Stand, und die Tests lesen
sich genau so, wie der Plan es vorsieht.

## Konsequenzen

- In Testprojekten ist `AwesomeAssertions` als globales `using` eingetragen.
- Ein späterer Wechsel zurück wäre ein Austausch von Paket und Namespace.
- Boss kann die Entscheidung jederzeit kippen, falls eine FluentAssertions-Lizenz vorhanden ist.
