# ADR-0001: Data Access with EF Core, Not ADO.NET or Dapper

- **Status:** Accepted
- **Date:** 2026-10-19

## Context

- The app stores todo lists and their items in SQL Server locally and Azure SQL in production: one one-to-many relationship, basic CRUD.
- The project includes a database migration strategy that runs in a pipeline, so the data access choice has to come first: it decides where the schema lives and how changes to it are made.
- My background is SQL and ADO.NET. EF Core is the standard ORM for .NET and what most .NET teams use.
- The goal is a complete delivery path within about four weeks, so the data layer should be done properly but kept to what this model needs.

## Options considered

- **ADO.NET, or Dapper over it.** Full control of every statement, and SQL I already write well. But the mapping is by hand, and so is the schema: migrations would be hand-written scripts with a tool to track and apply them.
- **EF Core, kept to its core features.** The schema lives in the model, migrations are generated from it and checked against it, and queries are LINQ, with the generated SQL logged to compare against what I'd write. Costs learning where EF Core's defaults and SQL differ from hand-written ADO.NET.
- **EF Core with its advanced mapping** (owned types, aggregate mapping, concurrency tokens). More of the ORM exercised, but nothing in a two-table model needs it, and it would stretch the timeline.

## Decision

- We will **use EF Core, kept to its core features**: change tracking and `AsNoTracking()`, DbContext lifetime and DI, LINQ-to-SQL with SQL logging on, migrations including applying them in a pipeline, async queries, one relationship.
- Out of scope: owned types, aggregate mapping, concurrency tokens. This model doesn't need them.

## Consequences

- The schema has one source of truth, the model, and every change goes through a migration that CI can check (`has-pending-model-changes`).
- SQL logging stays on in Development, so the SQL EF Core sends is always visible and can be compared with what I'd write by hand.
- The data layer may take a little longer than ADO.NET would have, while EF Core's behaviour is checked against the logged SQL. Accepted for a data layer that's maintainable and standard.
- Updates replace the whole item with no concurrency token, so a stale copy can overwrite a newer one (lost updates). Acceptable for a single-user app; a concurrency token is the fix if that changes.
