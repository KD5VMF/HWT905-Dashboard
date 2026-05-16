using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HWT905Dashboard.Hardware;
using HWT905Dashboard.Models;
using HWT905Dashboard.Services;
using WinForms = System.Windows.Forms;
using TextBox = System.Windows.Controls.TextBox;

namespace HWT905Dashboard;

public partial class MainWindow : System.Windows.Window
{
    private readonly SensorSnapshot _snapshot = new();
    private readonly SerialHwt905Service _serial;
    private readonly CsvRecorder _recorder = new();
    private readonly DispatcherTimer _uiTimer = new();
    private readonly Stopwatch _uptime = new();
    private readonly Process _process = Process.GetCurrentProcess();

    private AppSettings _settings;
    private CancellationTokenSource _detectCts;
    private long _lastPackets;
    private DateTime _lastRateUtc = DateTime.UtcNow;
    private double _dataRate;
    private TimeSpan _lastCpuTime;
    private DateTime _lastCpuUtc = DateTime.UtcNow;
    private double _cpuPercent;

    private readonly ConcurrentQueue<string> _pendingLogLines = new();
    private readonly ConcurrentQueue<RawDisplayLine> _pendingRawLines = new();
    private int _pendingLogCount;
    private int _pendingRawCount;
    private int _rawFrameCounter;
    private long _lastRawQueueTicks;
    private long _lastGraphPackets;
    private DateTime _lastUiErrorLogUtc = DateTime.MinValue;
    private int _rawDisplayByteCounter;
    private DateTime _lastSerialHealthUpdateUtc = DateTime.MinValue;

    private const int MaxQueuedLogLines = 160;
    private const int MaxQueuedRawLines = 24;
    private const int MaxDrainLogLinesPerTick = 16;
    private const int MaxDrainRawLinesPerTick = 2;
    private const int MaxLogChars = 24000;
    private const int MaxRawChars = 1600;
    private const int RawDisplayResetBytes = 512;

    private readonly struct RawDisplayLine
    {
        public RawDisplayLine(string text, int byteCount)
        {
            Text = text ?? string.Empty;
            ByteCount = Math.Max(0, byteCount);
        }

        public string Text { get; }
        public int ByteCount { get; }
    }

    public MainWindow()
    {
        InitializeComponent();
        _serial = new SerialHwt905Service(_snapshot);
        _serial.Log += Serial_Log;
        _serial.FrameReceived += Serial_FrameReceived;

        _uiTimer.Interval = TimeSpan.FromMilliseconds(100);
        _uiTimer.Tick += UiTimer_Tick;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = AppSettings.Load();
        // REV13 trusts the HWT905 factory-calibrated stream. Clear any old REV12 local-forward offset on startup.
        _settings.UseForwardOffset = false;
        _settings.ForwardYawOffset = 0;
        Directory.CreateDirectory(_settings.SaveFolder);
        SavePathText.Text = _settings.SaveFolder;

        BaudCombo.ItemsSource = new[] { 9600, 115200, 19200, 38400, 57600, 230400, 460800, 921600, 4800 };
        BaudCombo.SelectedItem = _settings.BaudRate;


        AccelGraph.SeriesAName = "X";
        AccelGraph.SeriesBName = "Y";
        AccelGraph.SeriesCName = "Z";
        GyroGraph.SeriesAName = "X";
        GyroGraph.SeriesBName = "Y";
        GyroGraph.SeriesCName = "Z";
        AngleGraph.SeriesAName = "Roll";
        AngleGraph.SeriesBName = "Pitch";
        AngleGraph.SeriesCName = "Yaw";

        RefreshPorts();
        _uptime.Start();
        _uiTimer.Start();
        SetDashboardMode("Overview", false);
        RawBox.Text = "REV13 read-only factory-trust mode. Raw serial dump is OFF for speed. Parser health is shown here and in the footer. No hardware write/calibration commands are available.";
        AddLog("REV13 ready. Factory-trust/read-only mode is active; no hardware write commands are available.");
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            _uiTimer.Stop();
            _uiTimer.Tick -= UiTimer_Tick;
            SaveSettings();
            _detectCts?.Cancel();
            _detectCts?.Dispose();
            _detectCts = null;
            _serial.Log -= Serial_Log;
            _serial.FrameReceived -= Serial_FrameReceived;
            _recorder.Dispose();
            _serial.Dispose();
            _process.Dispose();
        }
        catch
        {
            // App is closing; avoid throwing from cleanup.
        }
    }

    private void Serial_Log(string message) => QueueBounded(_pendingLogLines, message, ref _pendingLogCount, MaxQueuedLogLines);

    private void Serial_FrameReceived(Hwt905Frame frame)
    {
        // REV13: do not dump raw serial bytes into the WPF UI. The decoded parser
        // still runs normally, and parser health counters are shown in the footer.
        if (frame?.Bytes == null) return;
        Interlocked.Increment(ref _rawFrameCounter);
    }

    private static void QueueBounded(ConcurrentQueue<string> queue, string line, ref int count, int maxCount)
    {
        queue.Enqueue(line ?? string.Empty);
        int current = Interlocked.Increment(ref count);
        while (current > maxCount && queue.TryDequeue(out _))
            current = Interlocked.Decrement(ref count);
    }

    private void QueueBoundedRaw(RawDisplayLine line)
    {
        _pendingRawLines.Enqueue(line);
        int current = Interlocked.Increment(ref _pendingRawCount);
        while (current > MaxQueuedRawLines && _pendingRawLines.TryDequeue(out _))
            current = Interlocked.Decrement(ref _pendingRawCount);
    }

    private void UiTimer_Tick(object sender, EventArgs e)
    {
        try
        {
            DrainQueuedText();
            UpdateSerialHealthBox();
            UpdateUi();
        }
        catch (Exception ex)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastUiErrorLogUtc).TotalSeconds >= 2)
            {
                _lastUiErrorLogUtc = now;
                AddLog("UI update warning: " + ex.Message);
            }
        }
    }

    private void DrainQueuedText()
    {
        int drained = 0;
        while (drained++ < MaxDrainLogLinesPerTick && _pendingLogLines.TryDequeue(out string logLine))
        {
            Interlocked.Decrement(ref _pendingLogCount);
            AddLog(logLine);
        }

        drained = 0;
        while (drained++ < MaxDrainRawLinesPerTick && _pendingRawLines.TryDequeue(out RawDisplayLine rawLine))
        {
            Interlocked.Decrement(ref _pendingRawCount);
            AddRaw(rawLine.Text, rawLine.ByteCount);
        }
    }


    private void UpdateSerialHealthBox()
    {
        if (RawBox == null) return;
        var now = DateTime.UtcNow;
        if ((now - _lastSerialHealthUpdateUtc).TotalMilliseconds < 500) return;
        _lastSerialHealthUpdateUtc = now;

        string text =
            "RAW BYTE DUMP: OFF FOR SPEED\n" +
            "\n" +
            $"Parser OK frames : {_serial.ParserFramesOk:N0}\n" +
            $"Bad frames       : {_serial.ParserFramesBad:N0}\n" +
            $"Bytes discarded  : {_serial.ParserBytesDiscarded:N0}\n" +
            $"Frames observed  : {Math.Max(0, _rawFrameCounter):N0}\n" +
            "\n" +
            "Decoded values, graphs, and CSV recording continue to work.\n" +
            "Hardware write commands are removed in REV13 to preserve factory settings.\n" +
            "This box no longer appends raw hex data, preventing WPF text buildup.";
        if (!string.Equals(RawBox.Text, text, StringComparison.Ordinal))
            RawBox.Text = text;
    }

    private void RefreshPorts()
    {
        var ports = SerialHwt905Service.GetPorts();
        PortCombo.ItemsSource = ports;
        if (!string.IsNullOrWhiteSpace(_settings?.PortName) && ports.Contains(_settings.PortName))
            PortCombo.SelectedItem = _settings.PortName;
        else if (ports.Length > 0)
            PortCombo.SelectedIndex = 0;
    }

    private void UpdateUi()
    {
        var s = _snapshot.Copy();
        UpdateRate(s.Packets);
        UpdateCpu();
        try
        {
            _recorder.MaybeWrite(s);
        }
        catch (Exception ex)
        {
            AddLog("CSV recording stopped after write error: " + ex.Message);
            _recorder.Stop();
        }

        double displayYaw = Normalize360(s.Yaw);

        // User-requested display mapping: swap X and Y throughout the dashboard.
        double displayAccX = s.AccY;
        double displayAccY = s.AccX;
        double displayAccZ = s.AccZ;

        double displayGyroX = s.GyroY;
        double displayGyroY = s.GyroX;
        double displayGyroZ = s.GyroZ;

        double displayRoll = s.Pitch;
        double displayPitch = s.Roll;

        double displayMagX = s.MagY;
        double displayMagY = s.MagX;
        double displayMagZ = s.MagZ;

        UptimeText.Text = _uptime.Elapsed.ToString(@"hh\:mm\:ss");
        DataRateText.Text = $"{_dataRate:0} Hz";
        DataRateTileText.Text = $"{_dataRate:0} Hz";
        PacketText.Text = s.Packets.ToString("N0", CultureInfo.CurrentCulture);
        CpuText.Text = $"{_cpuPercent:0}%";

        if (s.LastUpdateUtc == DateTime.MinValue)
            LastUpdateText.Text = "never";
        else
        {
            var age = DateTime.UtcNow - s.LastUpdateUtc;
            LastUpdateText.Text = age.TotalSeconds < 1 ? $"{age.TotalMilliseconds:0} ms ago" : $"{age.TotalSeconds:0.0} s ago";
        }

        string tempF = double.IsNaN(s.TempF) ? "-- °F" : $"{s.TempF:0.0} °F";
        TempText.Text = tempF;
        TempTileText.Text = tempF;

        RollText.Text = $"{displayRoll:0.0}°";
        PitchText.Text = $"{displayPitch:0.0}°";
        YawText.Text = $"{displayYaw:0.0}°";
        RollBoxText.Text = $"{displayRoll:0.0}°";
        PitchBoxText.Text = $"{displayPitch:0.0}°";
        YawBoxText.Text = $"{displayYaw:0.0}°";

        MagXText.Text = s.HasMag ? displayMagX.ToString("0", CultureInfo.InvariantCulture) : "--";
        MagYText.Text = s.HasMag ? displayMagY.ToString("0", CultureInfo.InvariantCulture) : "--";
        MagZText.Text = s.HasMag ? displayMagZ.ToString("0", CultureInfo.InvariantCulture) : "--";
        MagStrengthText.Text = s.HasMag ? s.MagStrength.ToString("0", CultureInfo.InvariantCulture) : "--";

        AccelText.Text = $"X {displayAccX:0.000}   Y {displayAccY:0.000}   Z {displayAccZ:0.000}";
        GyroText.Text = $"X {displayGyroX:0.00}   Y {displayGyroY:0.00}   Z {displayGyroZ:0.00}";
        QuatText.Text = $"W {s.Q0:0.0000}   X {s.Q1:0.0000}\nY {s.Q2:0.0000}   Z {s.Q3:0.0000}";

        SetStatus(StatusAccelText, "Accelerometer", s.HasAccel);
        SetStatus(StatusGyroText, "Gyroscope", s.HasGyro);
        SetStatus(StatusMagText, "Magnetometer", s.HasMag);
        SetStatus(StatusAhrsText, "AHRS", s.HasAngle);

        AttitudeView.SetValues(displayRoll, displayPitch, displayYaw);
        OrientationView.SetValues(displayRoll, displayPitch, displayYaw);
        CompassView.SetValues(displayYaw, displayMagX, displayMagY, displayMagZ);

        if (s.Packets != _lastGraphPackets)
        {
            _lastGraphPackets = s.Packets;
            AccelGraph.Add(displayAccX, displayAccY, displayAccZ);
            GyroGraph.Add(displayGyroX, displayGyroY, displayGyroZ);
            AngleGraph.Add(displayRoll, displayPitch, displayYaw > 180 ? displayYaw - 360 : displayYaw);
        }

        UpdateConnectionWidgets();
        UpdateRecorderWidgets();
    }

    private void UpdateConnectionWidgets()
    {
        if (_serial.IsConnected)
        {
            ConnectionDot.Fill = BrushFromRgb(116, 255, 54);
            ConnectionText.Text = "Connected";
            ConnectionText.Foreground = BrushFromRgb(116, 255, 54);
            PortBaudText.Text = $"{_serial.PortName} @ {_serial.BaudRate}";
            BtnConnect.Content = "Disconnect";
            LockText.Text = "Port locked";
            LockText.Foreground = BrushFromRgb(116, 255, 54);
            FooterText.Text = $"Connected to {_serial.PortName} @ {_serial.BaudRate}   OK:{_serial.ParserFramesOk:N0}  BAD:{_serial.ParserFramesBad:N0}  DROP:{_serial.ParserBytesDiscarded:N0}  READ-ONLY FACTORY MODE";
        }
        else
        {
            ConnectionDot.Fill = BrushFromRgb(255, 66, 56);
            ConnectionText.Text = "Disconnected";
            ConnectionText.Foreground = BrushFromRgb(255, 66, 56);
            PortBaudText.Text = "No port";
            BtnConnect.Content = "Connect";
            LockText.Text = "Port unlocked";
            LockText.Foreground = BrushFromRgb(159, 177, 194);
        }
    }

    private void UpdateRecorderWidgets()
    {
        if (_recorder.IsRecording)
        {
            RecordDot.Fill = BrushFromRgb(255, 66, 56);
            RecordText.Text = "Recording...";
            RecordText.Foreground = BrushFromRgb(255, 94, 75);
            BtnRecord.Content = "Stop";
            SaveFileText.Text = Path.GetFileName(_recorder.CurrentFile);
            SavePathText.Text = _recorder.CurrentFolder;
        }
        else
        {
            RecordDot.Fill = BrushFromRgb(100, 120, 135);
            RecordText.Text = "Off";
            RecordText.Foreground = BrushFromRgb(159, 177, 194);
            BtnRecord.Content = "Start";
            SaveFileText.Text = "not recording";
            SavePathText.Text = _settings.SaveFolder;
        }
    }

    private void UpdateRate(long packets)
    {
        var now = DateTime.UtcNow;
        double dt = (now - _lastRateUtc).TotalSeconds;
        if (dt >= 1.0)
        {
            long delta = packets - _lastPackets;
            _dataRate = Math.Max(0, delta / dt);
            _lastPackets = packets;
            _lastRateUtc = now;
        }
    }

    private void UpdateCpu()
    {
        var now = DateTime.UtcNow;
        var cpu = _process.TotalProcessorTime;
        double dt = (now - _lastCpuUtc).TotalMilliseconds;
        if (dt >= 1000)
        {
            double cpuMs = (cpu - _lastCpuTime).TotalMilliseconds;
            _cpuPercent = Math.Max(0, Math.Min(100, cpuMs / (dt * Environment.ProcessorCount) * 100.0));
            _lastCpuUtc = now;
            _lastCpuTime = cpu;
        }
    }

    private static void SetStatus(TextBlock text, string name, bool ok)
    {
        text.Text = ok ? $"● {name}   OK" : $"● {name}   --";
        text.Foreground = ok ? BrushFromRgb(116, 255, 54) : BrushFromRgb(159, 177, 194);
    }


    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string mode)
            SetDashboardMode(mode, true);
    }

    private void SetDashboardMode(string mode, bool logChange = true)
    {
        mode = string.IsNullOrWhiteSpace(mode) ? "Overview" : mode;

        // Reset panel visibility first.
        TopMetricsPanel.Visibility = Visibility.Visible;
        MainVisualPanel.Visibility = Visibility.Visible;
        SummaryTilesPanel.Visibility = Visibility.Visible;
        GraphsPanel.Visibility = Visibility.Visible;
        BottomDataPanel.Visibility = Visibility.Visible;
        ModeInfoPanel.Visibility = Visibility.Collapsed;

        MetricsRow.Height = new GridLength(78);
        VisualRow.Height = new GridLength(3.05, GridUnitType.Star);
        TilesRow.Height = new GridLength(74);
        GraphsRow.Height = new GridLength(1.25, GridUnitType.Star);
        DataRow.Height = new GridLength(166);

        string description;
        switch (mode)
        {
            case "Attitude":
                VisualRow.Height = new GridLength(1, GridUnitType.Star);
                TilesRow.Height = new GridLength(86);
                GraphsRow.Height = new GridLength(0);
                DataRow.Height = new GridLength(0);
                GraphsPanel.Visibility = Visibility.Collapsed;
                BottomDataPanel.Visibility = Visibility.Collapsed;
                description = "Attitude focus: AHRS horizon, 3D orientation, compass, and main roll/pitch/yaw tiles.";
                break;

            case "Sensors":
                VisualRow.Height = new GridLength(1, GridUnitType.Star);
                TilesRow.Height = new GridLength(86);
                GraphsRow.Height = new GridLength(0);
                DataRow.Height = new GridLength(170);
                GraphsPanel.Visibility = Visibility.Collapsed;
                description = "Sensors focus: live decoded acceleration, gyro, magnetometer, quaternion, status, log, and raw stream.";
                break;

            case "Graphs":
                VisualRow.Height = new GridLength(0);
                TilesRow.Height = new GridLength(82);
                GraphsRow.Height = new GridLength(1, GridUnitType.Star);
                DataRow.Height = new GridLength(170);
                MainVisualPanel.Visibility = Visibility.Collapsed;
                description = "Graphs focus: acceleration, gyroscope, and roll/pitch/yaw history get the most screen space.";
                break;

            case "About":
                VisualRow.Height = new GridLength(1, GridUnitType.Star);
                TilesRow.Height = new GridLength(0);
                GraphsRow.Height = new GridLength(0);
                DataRow.Height = new GridLength(0);
                MainVisualPanel.Visibility = Visibility.Collapsed;
                SummaryTilesPanel.Visibility = Visibility.Collapsed;
                GraphsPanel.Visibility = Visibility.Collapsed;
                BottomDataPanel.Visibility = Visibility.Collapsed;
                ModeInfoTitle.Text = "HWT905 BEAUTIFUL DASHBOARD REV13";
                ModeInfoText.Text =
                    "REV13 is the factory-trust/read-only build.\n\n" +
                    "OVERVIEW: full cockpit dashboard.\n" +
                    "ATTITUDE: AHRS horizon, 3D orientation, compass, and main attitude tiles.\n" +
                    "SENSORS: live decoded sensor values plus logs and serial-health counters.\n" +
                    "GRAPHS: large acceleration, gyroscope, and angle history graphs.\n" +
                    "ABOUT: this page.\n\n" +
                    "Removed from REV13: accelerometer calibration, magnetic calibration, output-rate writes, baud-rate writes, core-output writes, sleep, save/unlock, and register-write command paths.\n\n" +
                    "Factory View Reset is local only. It clears old app-side forward offsets and graph history, then returns the dashboard to Overview. It never writes to the HWT905 or changes factory calibration.\n\n" +
                    "Safety/reliability kept: bounded queues, no live raw dump, parser counters, guarded UI updates, clean serial disposal, and CSV write protection.\n\n" +
                    "X/Y display swap remains active, as requested.";
                ModeInfoPanel.Visibility = Visibility.Visible;
                description = "About page.";
                break;

            default:
                mode = "Overview";
                description = "Overview: full cockpit dashboard with all panels visible.";
                break;
        }

        UpdateNavButtonState(mode);
        if (logChange)
            AddLog(description);
    }

    private void UpdateNavButtonState(string activeMode)
    {
        Button[] buttons =
        {
            BtnNavOverview, BtnNavAttitude, BtnNavSensors, BtnNavGraphs, BtnNavAbout
        };

        foreach (var button in buttons)
        {
            bool isActive = string.Equals(button.Tag as string, activeMode, StringComparison.OrdinalIgnoreCase);
            button.Background = isActive ? BrushFromRgb(12, 67, 128) : BrushFromRgb(16, 33, 52);
            button.BorderBrush = isActive ? BrushFromRgb(45, 140, 255) : BrushFromRgb(23, 50, 71);
            button.Foreground = isActive ? BrushFromRgb(244, 250, 255) : BrushFromRgb(215, 230, 245);
        }
    }

    private void BtnRefreshPorts_Click(object sender, RoutedEventArgs e) => RefreshPorts();

    private void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        if (_serial.IsConnected)
        {
            _serial.Disconnect();
            return;
        }

        string port = PortCombo.SelectedItem as string;
        int baud = BaudCombo.SelectedItem is int b ? b : 9600;
        if (string.IsNullOrWhiteSpace(port))
        {
            AddLog("No COM port selected.");
            return;
        }

        try
        {
            _serial.Connect(port, baud);
            _settings.PortName = port;
            _settings.BaudRate = baud;
            SaveSettings();
        }
        catch (Exception ex)
        {
            AddLog("Connect failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Connection failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void BtnAutoDetect_Click(object sender, RoutedEventArgs e)
    {
        if (_detectCts != null)
        {
            _detectCts.Cancel();
            _detectCts = null;
            BtnAutoDetect.Content = "Auto Detect + Lock";
            return;
        }

        _detectCts = new CancellationTokenSource();
        BtnAutoDetect.Content = "Cancel Scan";
        try
        {
            var result = await _serial.AutoDetectAsync(_detectCts.Token);
            if (result.Found)
            {
                AddLog($"HWT905-like stream found on {result.PortName} @{result.BaudRate}. Locking port.");
                _serial.Connect(result.PortName, result.BaudRate);
                RefreshPorts();
                PortCombo.SelectedItem = result.PortName;
                BaudCombo.SelectedItem = result.BaudRate;
                _settings.PortName = result.PortName;
                _settings.BaudRate = result.BaudRate;
                SaveSettings();
            }
            else
            {
                AddLog("Auto detect finished: no HWT905 stream found.");
            }
        }
        catch (OperationCanceledException)
        {
            AddLog("Auto detect cancelled.");
        }
        catch (Exception ex)
        {
            AddLog("Auto detect error: " + ex.Message);
        }
        finally
        {
            _detectCts = null;
            BtnAutoDetect.Content = "Auto Detect + Lock";
        }
    }

    private void BtnFactoryViewReset_Click(object sender, RoutedEventArgs e)
    {
        // Local-only reset. This intentionally sends nothing to the HWT905.
        _settings.UseForwardOffset = false;
        _settings.ForwardYawOffset = 0;
        SaveSettings();

        _lastGraphPackets = _snapshot.Copy().Packets;
        AccelGraph.Clear();
        GyroGraph.Clear();
        AngleGraph.Clear();
        SetDashboardMode("Overview", false);
        AddLog("Factory View Reset complete: local dashboard offsets/graphs cleared. No command was sent to the HWT905.");
    }

    private void BtnRecord_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_recorder.IsRecording)
            {
                _recorder.Stop();
                AddLog("CSV recording stopped.");
            }
            else
            {
                Directory.CreateDirectory(_settings.SaveFolder);
                _recorder.Start(_settings.SaveFolder);
                AddLog("CSV recording started: " + _recorder.CurrentFile);
            }
        }
        catch (Exception ex)
        {
            AddLog("Recording error: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Recording error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Choose folder for HWT905 CSV logs",
            SelectedPath = Directory.Exists(_settings.SaveFolder) ? _settings.SaveFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() == WinForms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            _settings.SaveFolder = dialog.SelectedPath;
            SavePathText.Text = _settings.SaveFolder;
            SaveSettings();
            AddLog("Save folder changed: " + _settings.SaveFolder);
        }
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_settings.SaveFolder);
            Process.Start(new ProcessStartInfo(_settings.SaveFolder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AddLog("Open folder failed: " + ex.Message);
        }
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        LogBox.Clear();
        _rawDisplayByteCounter = 0;
        while (_pendingRawLines.TryDequeue(out _))
            Interlocked.Decrement(ref _pendingRawCount);
        UpdateSerialHealthBox();
        _lastGraphPackets = _snapshot.Copy().Packets;
        AccelGraph.Clear();
        GyroGraph.Clear();
        AngleGraph.Clear();
    }

    private void AddLog(string message)
    {
        string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine;
        LogBox.AppendText(line);
        TrimTextBox(LogBox, MaxLogChars);
        LogBox.ScrollToEnd();
    }

    private void AddRaw(string hexLine, int sourceByteCount)
    {
        // REV13: intentionally no live hex append. This is a safe no-op so the UI
        // cannot be overloaded by high-rate raw serial display.
        _rawDisplayByteCounter = 0;
    }

    private static void TrimTextBox(TextBox box, int maxChars)
    {
        if (box.Text.Length <= maxChars) return;
        box.Text = box.Text.Substring(box.Text.Length - maxChars);
        box.CaretIndex = box.Text.Length;
    }

    private void SaveSettings()
    {
        if (_settings == null) return;
        _settings.PortName = PortCombo.SelectedItem as string ?? _settings.PortName;
        _settings.BaudRate = BaudCombo.SelectedItem is int b ? b : _settings.BaudRate;
        _settings.Save();
    }

    private static SolidColorBrush BrushFromRgb(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));

    private static double Normalize360(double d)
    {
        d %= 360.0;
        if (d < 0) d += 360.0;
        return d;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            ToggleMaximize();
        else
            DragMove();
    }

    private void BtnMin_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnMax_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
}
