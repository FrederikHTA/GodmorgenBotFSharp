F# Style Guide for AI Agents

This document defines the coding standards, patterns, and principles for the Aleta codebase.
**As an AI agent, you must strictly adhere to these guidelines when generating, refactoring, or reviewing code.**

## Core Principles

1.  **Safety First**: Leverage the F# type system to make invalid states unrepresentable (Read part 1.1 for more details).
2.  **Explicit Intent**: Code should be obvious. Avoid "clever" shortcuts (like custom operators) that obscure meaning.
3.  **Error as Values**: Use `Result` and `FsToolkit.ErrorHandling` for domain errors. Exceptions are for bugs/panics only.
4.  **Maintainability**: Optimize for readability and ease of change.

---

## 1. Type Design & Safety

### 1.1 Make Impossible States Unrepresentable
**Rule**: Do not use primitive types (string, int, bool) for domain concepts if those primitives allow invalid values. Use Discriminated Unions (DUs) and Records.

**Bad:**
```fsharp
type Portfolio = {
    Status: string // Could be "Active", "Closed", or "potato"
    ClosedDate: DateTime option // Can be set even if Status is "Active"
}
```

**Good:**
```fsharp
type PortfolioStatus =
    | Active
    | Closed of closedDate: DateTime

type Portfolio = {
    Status: PortfolioStatus
}
```

### 1.2 Smart Constructors
**Rule**: If a type has validation logic (e.g., a non-empty string), use a private constructor and a public `create` function that returns a `Result`.

**Pattern:**
```fsharp
type CustomerName = private CustomerName of string

module CustomerName =
    let create (input: string) : Result<CustomerName, string> =
        if String.IsNullOrWhiteSpace(input) then
            Error "Name cannot be empty"
        else
            Ok (CustomerName input)

    let value (CustomerName inner) = inner
```

---

## 2. Error Handling (FsToolkit.ErrorHandling)

**Rule**: Use `FsToolkit.ErrorHandling` for all flow control involving failures.
**Rule**: Prefer computation expressions (`result {}`, `asyncResult {}`) over raw `Result.bind`/`map` chains for complex logic.

### 2.1 Async/Task Flows
Prefer F# `Async` over .NET `Task`. Most I/O operations will be `Async<Result<'T, 'Error>>`. Use `asyncResult`.

**Pattern:**
```fsharp
open FsToolkit.ErrorHandling

let processTransaction (cmd: CreateTransactionCmd) : Async<Result<TransactionId, DomainError>> = asyncResult {
    // 1. Validation (returns Result)
    let! validAmount = Money.create cmd.Amount |> Result.mapError DomainError.Validation

    // 2. Async I/O (returns Async<Result>)
    let! account = repo.GetAccount cmd.AccountId

    // 3. Logic
    let! updatedAccount = Account.debit validAmount account

    // 4. Side Effect
    do! repo.SaveAccount updatedAccount

    return updatedAccount.Id
}
```

### 2.2 Validation
Use `validation {}` (from FsToolkit) or `Result` for pure validation logic.

---

## 3. Formatting & Syntax

### 3.1 Type Annotations
**Rule**: Use generic syntax `List<'T>` and `Option<'T>`.
**Forbid**: Postfix syntax `string list` or `int option`.

**Bad:**
```fsharp
let names: string list = []
```

**Good:**
```fsharp
let names: List<string> = []
```

### 3.2 Pipelining
**Rule**: Use the forward pipe `|>` freely.
**Forbid**: The backward pipe `<|`. It increases cognitive load. Parentheses are preferred if `|>` isn't suitable.

**Bad:**
```fsharp
let x = normalize <| "some string"
```

**Good:**
```fsharp
let x = normalize "some string"
// OR
let x = "some string" |> normalize
```

### 3.3 Naming Conventions
- **Types/Modules/DUs/Records**: `PascalCase`
- **Functions/Values/Parameters**: `camelCase`
- **Interfaces**: `IInterfaceName`

**Exception**:
- Unsafe/Unvalidated IDs: Prefix with `unsafe_` (e.g., `unsafe_customerId`).
- Database foreign keys or DTOs: Use `camelCase` if matching external JSON/DB schema strictly requires it, but prefer mapping to domain types immediately.

---

## 4. Code Structure

### 4.1 File Organization
- **Top**: Domain Types
- **Middle**: Pure Logic / Domain Functions
- **Bottom**: I/O and Orchestration (Shell)

### 4.2 Function Length
**Rule**: Keep functions short. As a rule of thumb, aim for functions that are less than 70 lines long.
**Rule**: Push `if`/`match` logic up. Leaf functions should be simple.

### 4.3 Comments
**Rule**: Document *why*, not *what*.
**Rule**: Use XML docs (`///`) for public API members.

```fsharp
/// Calculates the accrued interest.
/// NOTE: Uses 360-day year convention as per regulatory requirement X.
let calculateInterest ...
```

### 4.4 Function Structure
**Rule**: Structure functions with assertions first if necessary. Then do error handling with break out early concept. At last, handle success cases.
**Rule**: Functions should generally avoid branching logic. Use `Result` or `Option` to handle errors gracefully.
**Rule**: Use `Result` or `Option` to handle errors gracefully.

---

## 5. Testing Intent

**Rule**: When generating tests, prioritize covering:
1.  **Happy Path**: The standard success case.
2.  **Edge Cases**: Boundary values (0, -1, max int, empty lists).
3.  **Error Paths**: Ensure specific domain errors are returned, not just generic failures.

---

## 6. Checklist for AI Review

Before outputting code, verify:
- [ ] Did I use `asyncResult` for async flows?
- [ ] Are all lists/options using `<'T>` syntax (e.g., `List<int>`)?
- [ ] Did I eliminate backward pipes `<|`?
- [ ] Are invalid states impossible (e.g., no raw strings for IDs where types exist)?
- [ ] Are `unsafe_` prefixes used for unvalidated inputs?
- [ ] Is `FsToolkit.ErrorHandling` used instead of exceptions?
