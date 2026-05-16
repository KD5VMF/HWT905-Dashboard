# HWT905 Dashboard REV13

A GitHub-ready C#/.NET 8 WPF dashboard for the WitMotion HWT905-TTL serial sensor.

REV13 is the **factory-trust / read-only** build. The HWT905 modules come factory calibrated, so this version removes the dangerous hardware-control pages and trusts the factory settings instead of trying to rewrite calibration or device registers.

## REV13 focus

- Removes calibration and setup/control pages from the UI.
- Removes hardware write command source code from the project.
- Removes serial transmit command methods from the serial service.
- Leaves normal read-only serial receiving, parsing, graphs, dashboard visuals, and CSV recording intact.
- Keeps Auto Detect + Lock for finding the HWT905 COM port and baud rate.
- Adds **Factory View Reset**, which is local only:
  - clears any old dashboard yaw/forward offset from earlier versions,
  - clears graph history,
  - returns the display to Overview,
  - **does not send any command to the HWT905**.

## What it does

- Connects to a serial HWT905-TTL through USB-TTL/COM port.
- Auto Detect + Lock scans COM ports and common WIT baud rates.
- Defaults to 9600 baud, which is the common/default HWT905-TTL speed.
- Parses WIT 11-byte `0x55` packets.
- Handles acceleration, gyroscope, angle, magnetometer, quaternion/register-style packets when streamed by the sensor.
- Shows temperature in Fahrenheit with smoothing and sanity filtering.
- Shows a cockpit-style AHRS attitude display, 3D orientation display, compass/magnetometer view, live decoded values, live graphs, serial-health counters, and CSV recording.
- Saves only local app settings such as selected port, selected baud rate, and CSV save folder.

## What REV13 intentionally does **not** do

REV13 does not send commands to the HWT905. These hardware-write features were removed on purpose:

- Accelerometer calibration
- Magnetic calibration start/end
- Output-rate register writes
- Baud-rate register writes
- Core-output register writes
- Sleep command
- Unlock/save commands
- Any register-write command path

This keeps the dashboard from accidentally changing or overwriting the factory calibration/settings.

## Run it

Double-click:

```bat
RUN_ME.cmd
```

That avoids the PowerShell script-signing issue by launching PowerShell with a temporary bypass for this one run.

## Manual build/run

From this folder:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\build_and_run.ps1
```

Or directly:

```powershell
cd .\HWT905Dashboard
dotnet restore
dotnet run -c Release
```

## Requirements

- Windows 10/11
- .NET 8 SDK installed
- USB-to-TTL adapter or serial interface connected to the HWT905-TTL
- Correct wiring: sensor TX to adapter RX, sensor RX to adapter TX, GND to GND, and proper sensor supply voltage per your module/adapter

## Factory View Reset

Factory View Reset is safe to use because it is local to the dashboard. It clears app-side display offsets and graph history only. It does not write to the serial device and cannot change HWT905 factory calibration.

REV13 also clears old local-forward offsets from prior REV12 settings at startup, so yaw/heading is shown directly from the factory-calibrated sensor stream.

## Project layout

```text
HWT905Dashboard/
  App.xaml
  MainWindow.xaml
  MainWindow.xaml.cs
  Controls/
    AttitudeIndicator.cs
    CompassGauge.cs
    Orientation3DControl.cs
    LineGraphControl.cs
  Hardware/
    Hwt905Frame.cs
    Hwt905Parser.cs
  Models/
    SensorSnapshot.cs
  Services/
    AppSettings.cs
    CsvRecorder.cs
    SerialHwt905Service.cs
```

## Safety/reliability kept from REV12

- Raw serial hex dump is disabled for speed.
- Parser health counters remain visible in the footer and serial-health panel.
- Bounded parser buffer helps recover from noisy cable/wrong baud conditions.
- Bounded UI log/raw queues prevent high-rate serial data from flooding WPF.
- Safer serial disconnect/dispose path with event unhooking and race protection.
- UI timer is guarded so one bad update cannot crash the dashboard.
- CSV write failures are caught and recording stops cleanly instead of crashing the app.
- X/Y display swap remains active, as requested.

## License

MIT License is recommended for this project if you are publishing it to GitHub.
