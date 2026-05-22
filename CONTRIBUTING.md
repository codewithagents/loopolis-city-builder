# Contributing to Loopolis

Thanks for your interest in contributing! Loopolis is a solo indie project open to community improvements. Keep PRs small and focused — that makes review fast and merges easy.

Website: https://www.codewithagents.de

---

## Getting Started

**Prerequisites**

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Godot 4.4.1 .NET edition](https://godotengine.org/download) — install to `/Applications/Godot_mono.app` on macOS or adjust paths accordingly

**Clone and build**

```bash
git clone https://github.com/codewithagents/loopolis-city-builder.git
cd loopolis-city-builder
dotnet build src/
```

---

## Run Tests

All simulation logic lives in `src/Loopolis.Core/` and is covered by NUnit tests. Run them with:

```bash
dotnet test
```

Tests complete in under 5 seconds — no Godot startup required.

---

## Run the Game

### Standalone mode (Godot runs its own simulation)

```bash
DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec \
  /Applications/Godot_mono.app/Contents/MacOS/Godot \
  --path /path/to/loopolis/godot/ \
  --editor
# Open scenes/World.tscn and press F5
```

### Server + viewer mode (headless simulation + Godot viewer)

```bash
# Terminal 1 — start simulation server
export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"
dotnet run --project src/Loopolis.Runner -- server default --speed 2

# Terminal 2 — launch Godot viewer
DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec \
  /Applications/Godot_mono.app/Contents/MacOS/Godot \
  --path /path/to/loopolis/godot/ \
  --editor
# Open scenes/World.tscn and press F5
```

### Headless balance checks

```bash
dotnet run --project src/Loopolis.Runner -- 500 default
dotnet run --project src/Loopolis.Runner -- 1000 powered_start
```

Output is JSON — useful for verifying simulation balance without opening Godot.

---

## How to Contribute

1. **Fork** the repository
2. **Create a branch** — `git checkout -b feat/my-feature`
3. **Implement** Core changes first (if any), then Godot presentation
4. **Test** — `dotnet test` must be green; Godot must compile (`cd godot && dotnet build`)
5. **Open a PR** — fill in the PR template

---

## Rules

- **No Godot imports in `Loopolis.Core/`** — ever. Core is pure C# with zero engine dependencies.
- **Every new system needs NUnit tests** — test files mirror source paths (e.g. `Simulation/MySystem.cs` → `tests/.../Simulation/MySystemTests.cs`).
- **No business logic in `Program.cs` or `World.cs`** — wiring only.
- **Keep PRs small and focused** — one feature or fix per PR makes review much easier.
- **Balance check** — if you touch simulation math, run a headless scenario and include the output (or a summary) in the PR description.

---

## Questions?

Open a [GitHub Discussion](https://github.com/codewithagents/loopolis-city-builder/discussions) or reach out via the website: https://www.codewithagents.de
