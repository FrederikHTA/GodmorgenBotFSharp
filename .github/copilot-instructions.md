# Copilot Instructions for Pleteum Data Extract

This file contains custom instructions for GitHub Copilot to ensure code quality, consistency, and adherence to project standards.

## Project Overview
This project, `Pleteum-Data-Extract`, is a data extraction utility running on Azure Functions.
- **Language**: F# / .NET
- **Framework**: Azure Functions (Worker Model)
- **Key Libraries**: `FsToolkit.ErrorHandling`, `Dapper`, `AWSSDK.S3`

## Tech Stack & Standards
- **Strictly follow the F# Style Guide located at** `.github/fsharp_style_guide`.
- **Error Handling**: ALways use `FsToolkit.ErrorHandling`. Prefer `asyncResult {}` or `result {}` computation expressions. Do NOT throw exceptions for domain errors.
- **Type Safety**: Use Discriminated Unions and Single Case Unions to make illegal states unrepresentable. Avoid primitive obsession.
- **Syntax**: ALWAYS use `Option<T>` and `List<T>` syntax. NEVER use `T option` or `T list`.
- **Pipelines**: Use forward pipes `|>` only. Do NOT use backward pipes `<|`.

## Code Quality Guidelines
1.  **Domain Driven**: Logic should be built around domain types defined in `Domain.fs`.
2.  **Validation**: All inputs (from SQL, env vars, etc.) must be validated and mapped to domain types immediately at the boundary.
3.  **Immutability**: Prefer immutable data structures.
4.  **Async**: Use `async {}` or `asyncResult {}` for I/O bound operations.

## Copilot Behavior
- **Be Concise**: Provide code solutions directly.
- **F# Idiomatic**: Generate idiomatic F# code. Avoid C#-isms (e.g., avoid `if (x == null)`, use `Option` instead).
- **Security**: Do not suggest hardcoded credentials. Use `Environment.GetEnvironmentVariable`.
- **Refactoring**: When modifying code, ensure type signatures remain explicit and safe.
