# Coding conventions

Rules for all C# in this repository. `.editorconfig` enforces most of them: run
`dotnet format CS2-SimpleAdmin.sln` before committing.

## Types

- Always write the explicit type. No `var`.
  ```csharp
  List<CCSPlayerController> players = Helper.GetValidPlayers();
  foreach (CCSPlayerController player in players) { ... }
  ```
- The only exception is an anonymous type, which has no name (Dapper parameters): `var parameters = new { ... };`.
- Target-typed `new()` and collection expressions (`[]`) are fine: the type is already written on the left.

## Conditions

- No ternary operator (`c ? a : b`). Write an `if`, or extract a small helper that returns.
  ```csharp
  string status = "OFF";
  if (loaded)
  {
      status = "ON";
  }
  ```
- Always use braces, even for one statement. No single-line `if (x) return;`.
- `switch` expressions on a value are fine (`penaltyType switch { ... }`).
- `??` and `?.` are fine.

## Layout

- Allman braces, 4 spaces, LF line endings.
- File-scoped namespaces.
- `using` directives outside the namespace, `System` first, no unused ones.
- Early return to keep nesting shallow.

## Naming

- PascalCase for types, methods, properties, constants. camelCase for locals and parameters. `_camelCase` for private fields.
- A lambda parameter you do not use is `_`. Never assign to a discard (`_ = ...`) inside a lambda whose parameter is `_`: the assignment goes to the parameter.

## Duplication

- When the same expression shows up in several commands, add a helper in `Helper` (see `GetCallerName`,
  `GetReasonOrUnknown`, `GetArgsReason`, `GetAdminInfo`, `GetLogName`, `Truncate`, `HasCommandPermission`).

## Comments

- Comment why, not what. No commented-out code: git keeps the history.
- XML `<summary>` on public members and on anything non-obvious.

## UI

- All UI goes through the Panorama UI Kit (`Menus/UiKit.cs`). Never call CounterStrikeSharp chat/center menus directly.
- Menus are built with `MenuBuilder`. A sub-menu returns `builder.WithBackButton()`; the kit draws the back button.
- An option closes the menu after its action, unless it passes `keepOpen: true` or its action opens another menu.
- Panorama does not render chat color tags (`{red}`, `ChatColors`): keep them out of UI text.
- Native calls are not thread-safe: after an `await`, come back with `Server.NextWorldUpdateAsync` before touching the UI.
