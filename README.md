# SimForge Circuit Studio

SimForge is a modern desktop workspace for building, inspecting, and simulating electronic circuits. Version 0.7.0 adds a beginner-focused Circuit Assistant that explains missing wiring, unsafe LED paths, pin mismatches, and C++ sketch problems with actionable fixes.

## Highlights

- Interactive component palette and gridded circuit workspace
- Visible, type-aware pins and guided wire creation
- Contextual inspector for component properties
- Arduino-style `pinMode` and `digitalWrite` sketch parsing
- Arduino constants, `#define`, `LED_BUILTIN`, `analogRead`, `digitalRead`, and `pulseIn` recognition
- Live sensor-driven `if/else` output rules, including common HC-SR04 distance conversion
- Non-linear LDR response, analog ADC values, HC-SR04 echo timing, and DHT11 sampling limits
- Live circuit status, topology validation, and short-circuit protection
- Live Circuit Assistant with ordered wiring fixes and complete beginner C++ examples
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

Verify that the native UI can initialize and open its main window:

```bash
dotnet run --project SimForge/SimForge.csproj -- --smoke-test
```

## Sensor-driven sketches

SimForge runs a deterministic Arduino-style subset rather than invoking a native compiler. A sensor read can be used directly or assigned to a variable before a simple `if/else`:

```cpp
void setup() { pinMode(13, OUTPUT); }

void loop() {
  int light = analogRead(A0);
  if (light > 600) {
    digitalWrite(13, HIGH);
  } else {
    digitalWrite(13, LOW);
  }
}
```

Equivalent `pulseIn`, HC-SR04 distance, and DHT11 temperature conditions are supported. The editor reports a diagnostic instead of silently approximating unsupported complex expressions.

## Create platform builds

Each command creates a self-contained application that does not require a separate .NET installation on the target computer.

```bash
# Windows x64
dotnet publish SimForge/SimForge.csproj -c Release -r win-x64 --self-contained true -o artifacts/SimForge-0.7.0-windows-x64

# Linux x64
dotnet publish SimForge/SimForge.csproj -c Release -r linux-x64 --self-contained true -o artifacts/SimForge-0.7.0-linux-x64

# macOS Apple Silicon
dotnet publish SimForge/SimForge.csproj -c Release -r osx-arm64 --self-contained true -o artifacts/SimForge-0.7.0-macos-arm64
```

To create a signed macOS application bundle and upload-ready ZIP:

```bash
./scripts/package-macos.sh 0.7.0
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
