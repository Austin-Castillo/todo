# ADR-0001: Data Access with EF Core, Learned to the Fundamentals

- **Status:** Accepted
- **Date:** 2026-10-19

## Context

- The primary goal of this project is shipping an app. EF Core is the industry standard choice for ORM, but it was identified as a gap in my skillset. My SQL experience is relatively strong so raw SQL would let me move faster now.
- A database migration strategy is part of this project, so the data approach needs to be settled before a migration strategy can be decided.
- Most .NET work I'm aiming for uses EF Core, so it needs to be decided upfront when and how much of that gap should be tackled now.

## Options considered

- Use ADO.NET in this project and learn EF Core later. This is what I have prior experience with so it would have been less costly upfront, but the EF Core learning would still be ahead of me, from scratch.
- Do a deep dive on EF Core in this project. This would get me fully up to speed on the industry standard at the cost of pushing the timeline further out.
- Minimal EF Core learning in this project (middle path).

## Decision

- We will pursue the middle path of exposure to just enough EF Core so that this app can be developed using best practices, while also giving me a solid foundation for later work, all with minimal impacts to the timeline.
- In: change tracking and AsNoTracking(), DbContext lifetime and DI, LINQ-to-SQL with SQL logging on, migrations including applying them in a pipeline, async queries, one relationship.
- Out of scope here: owned types, aggregate mapping, concurrency.

## Consequences

- The timeline will be slightly impacted by the increased learning scope. However, we have determined that stretching the timebox is acceptable for learning depth.
- Later EF Core work will be built on top of a solid foundation of understanding. However, there is much to EF Core that this project doesn't cover, which means I will still not be aware of many common pitfalls.
