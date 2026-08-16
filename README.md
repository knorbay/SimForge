# SimForge Circuit Studio

SimForge is a modern desktop workspace for building, inspecting, and simulating electronic circuits. Version 0.3 introduces an English-first interface, a searchable component library, pin-aware wiring, Arduino-style sketch analysis, circuit safety checks, and a polished engineering canvas.

## Highlights

- Interactive component palette and gridded circuit workspace
- Visible, type-aware pins and guided wire creation
- Contextual inspector for component properties
- Arduino-style `pinMode` and `digitalWrite` sketch parsing
- Live circuit status, topology validation, and short-circuit protection
- Keyboard-accessible controls and clear simulation feedback
- Native desktop builds for Windows, Linux, and macOS

## Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Windows 10 or later, a modern x64 Linux desktop, or macOS 12 or later

## Build and test

```bash
dotnet restore SimForge.sln
dotnet build SimForge.sln
dotnet test SimForge.sln
```

Run the desktop application:

```bash
dotnet run --project SimForge/SimForge.csproj
```

## Create platform builds

Each command creates a self-contained application that does not require a separate .NET installation on the target computer.

```bash
# Windows x64
dotnet publish SimForge/SimForge.csproj -c Release -r win-x64 --self-contained true -o artifacts/SimForge-0.3-windows-x64

# Linux x64
dotnet publish SimForge/SimForge.csproj -c Release -r linux-x64 --self-contained true -o artifacts/SimForge-0.3-linux-x64

# macOS Apple Silicon
dotnet publish SimForge/SimForge.csproj -c Release -r osx-arm64 --self-contained true -o artifacts/SimForge-0.3-macos-arm64
```

On Linux, mark the executable as runnable if the archive tool does not preserve permissions:

```bash
chmod +x SimForge
```

## Continuous integration

GitHub Actions builds and tests SimForge on native Windows, Linux, and macOS runners. Downloadable self-contained packages are attached to each successful workflow run as artifacts.

## Project structure

- `SimForge` — Avalonia desktop interface
- `SimForge.Core` — circuit graph, components, pins, and sketch logic
- `SimForge.Core.Tests` — unit tests for the simulation core
- `SimForge.Physics` — reserved physics integration layer

## License

No license has been declared yet. All rights are reserved by the repository owner.
