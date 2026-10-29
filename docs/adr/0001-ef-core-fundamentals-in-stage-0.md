# ADR-0001: EF Core Fundamentals in Stage 0

- **Status:** Accepted
- **Date:** 2026-10-19

## Context

_What's the situation? Which goals, constraints and forces make this a decision? State the problem, not the answer._
- The primary goal of Stage 0 is shipping an app. EF Core is the industry standard choice for ORM, but it was identified as a gap in my skillset. My SQL experience is relatively strong so raw SQL would let me move faster now.
- A database migration strategy is part of Stage 0, so the data approach needs to be settled before a migration strategy can be decided.
- Stage 1 leans heavily on EF Core so it needs to be decided upfront when and how much of that gap should be tackled now.

## Options considered

_The realistic alternatives (two to four), each with its main pros and cons, judged against the goals above rather than in general._
- Hold EF Core until Stage 1 and use ADO.NET in this stage. This is what I have prior experience with so it would have been less costly upfront, but the next stage would have to start EF Core exposure from scratch.
- Do a deep dive on EF Core in this stage. This would get me fully up to speed on the industry standard at the cost of pushing the timeline for Stage 0 further out.
- Minimal EF Core leaning in this state (middle path).

## Decision

_"We will..." State what was chosen and why it won over the other options._
- We will pursue the middle path of exposure to just enough EF Core so that Stage 0 can be developed using best practices, while also giving me a solid foundation for Stage 1 and beyond, all with minimal impacts to the timeline.
- In: change tracking and AsNoTracking(), DbContext lifetime and DI, LINQ-to-SQL with SQL logging on, migrations including applying them in a pipeline, async queries, one relationship.
- Out until Stage 1.1: owned types, aggregate mapping, concurrency.

## Consequences

_What follows, good and bad: what gets easier, what you give up, what to watch for, and what would make you revisit this decision._
- The timeline for Stage 0 will be slightly impacted by the increased learning scope. However, we have determined that stretching the timebox is acceptable for learning depth.
- The EF Core concepts in Stage 1 will be built on top of a solid foundation of understanding. However, there is much to EF Core that will not be covered during Stage 0, which means I will still not be aware of many common pitfalls.
