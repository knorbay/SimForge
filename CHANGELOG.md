# Changelog

## 0.7.0 - 2026-09-01

- Added a live Circuit Assistant designed for people who do not know C++ yet.
- Added ordered, actionable diagnostics for missing controllers, sensor power, signal wiring, empty sketch pins, unused outputs, incomplete LED loops, and unsafe LED paths.
- Added beginner-friendly explanations for common sketch diagnostics.
- Added complete starter sketches for analog sensors, HC-SR04, and DHT11 circuits.
- Increased regression coverage to 51 tests.

## 0.6.0 - 2026-09-01

- Connected live analog, pulse, distance, temperature, and humidity readings to simple Arduino `if/else` output rules.
- Added common sensor-variable and HC-SR04 distance-conversion recognition.
- Added D7 as a bidirectional controller pin and made automatic wiring follow the sketch's referenced pins.
- Added explicit diagnostics for sensor conditions that are too complex to simulate reliably.
- Added live sensor-to-output reaction feedback and clearer powered/wired sensor status.
- Increased regression coverage to 41 tests.

## 0.5.0 - 2026-08-31

- Expanded Arduino-style C++ analysis with constants, macros, built-in pin aliases, numeric logic levels, and input-only sketches.
- Added independent HIGH/LOW timing profiles so asymmetric blink sketches run correctly.
- Added A0 analog input paths and controller-ground references for practical sensor wiring.
- Replaced threshold-only sensor behavior with LDR divider, potentiometer ADC, HC-SR04 echo-time, and DHT11 sampling models.
- Allowed complete powered sensor circuits to run without requiring an LED path.
- Removed an incompatible debug-tools dependency that prevented desktop debug builds.
- Increased regression coverage from 21 to 36 tests.

## 0.4.0 - 2026-08-25

- Hardened Arduino sketch analysis so comments and string literals are ignored.
- Prevented overflowing numeric pin values from crashing the live code editor.
- Added regression coverage for sketch parsing edge cases.
- Aligned application, UI, documentation, CI artifact, and macOS bundle versions.
- Added a repeatable signed macOS application-bundle packaging workflow.
