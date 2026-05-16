using System;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HWT905Dashboard.Hardware;
using HWT905Dashboard.Models;

namespace HWT905Dashboard.Services;

public sealed class SerialHwt905Service : IDisposable
{
    private readonly Hwt905Parser _parser = new();
    private readonly byte[] _readBuffer = new byte[4096];
    private readonly SensorSnapshot _snapshot;
    private readonly object _writeGate = new();
    private readonly object _readGate = new();

    private SerialPort _port;
    private bool _disposed;
    private int _readErrorStreak;
    private DateTime _lastReadErrorLogUtc = DateTime.MinValue;

    public SerialHwt905Service(SensorSnapshot snapshot)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _parser.FrameReceived += Parser_FrameReceived;
        _parser.BadFrameReceived += Parser_BadFrameReceived;
    }

    public event Action<Hwt905Frame> FrameReceived;
    public event Action<string> Log;

    public bool IsConnected => _port?.IsOpen == true;
    public string PortName => _port?.PortName ?? string.Empty;
    public int BaudRate => _port?.BaudRate ?? 0;
    public long ParserFramesOk => _parser.FramesOk;
    public long ParserFramesBad => _parser.FramesBad;
    public long ParserBytesDiscarded => _parser.BytesDiscarded;

    private void Parser_FrameReceived(Hwt905Frame frame)
    {
        _readErrorStreak = 0;
        _snapshot.Apply(frame);
        FrameReceived?.Invoke(frame);
    }

    private void Parser_BadFrameReceived(byte[] bytes)
    {
        _snapshot.MarkBadPacket();
    }

    public static string[] GetPorts()
    {
        try
        {
            return SerialPort.GetPortNames().OrderBy(NaturalPortSortKey).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public void Connect(string portName, int baudRate)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SerialHwt905Service));
        Disconnect();
        _parser.Reset();
        _readErrorStreak = 0;

        var newPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 100,
            WriteTimeout = 500,
            DtrEnable = true,
            RtsEnable = true,
            ReadBufferSize = 16384,
            WriteBufferSize = 4096
        };

        try
        {
            newPort.DataReceived += Port_DataReceived;
            newPort.Open();
            _port = newPort;
            Log?.Invoke($"Connected to {portName} @{baudRate}");
        }
        catch
        {
            try { newPort.DataReceived -= Port_DataReceived; } catch { }
            try { newPort.Dispose(); } catch { }
            throw;
        }
    }

    public void Disconnect()
    {
        SerialPort oldPort;
        lock (_writeGate)
        {
            oldPort = _port;
            _port = null;
        }

        if (oldPort == null) return;

        try
        {
            oldPort.DataReceived -= Port_DataReceived;
            if (oldPort.IsOpen) oldPort.Close();
            Log?.Invoke("Disconnected");
        }
        catch (Exception ex)
        {
            Log?.Invoke("Disconnect warning: " + ex.Message);
        }
        finally
        {
            try { oldPort.Dispose(); } catch { }
        }
    }

    private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (_disposed) return;

        lock (_readGate)
        {
            var port = sender as SerialPort ?? _port;
            if (port == null || !port.IsOpen) return;

            try
            {
                int safetyLoops = 0;
                while (port.IsOpen && port.BytesToRead > 0 && safetyLoops++ < 64)
                {
                    int toRead = Math.Min(port.BytesToRead, _readBuffer.Length);
                    int read = port.Read(_readBuffer, 0, toRead);
                    if (read <= 0) break;
                    _parser.Push(_readBuffer, read);
                }
            }
            catch (ObjectDisposedException)
            {
                // Normal during fast disconnect/reconnect.
            }
            catch (InvalidOperationException)
            {
                // Normal if the port closes while a DataReceived event is already queued.
            }
            catch (Exception ex)
            {
                _readErrorStreak++;
                var now = DateTime.UtcNow;
                if ((now - _lastReadErrorLogUtc).TotalSeconds >= 2)
                {
                    _lastReadErrorLogUtc = now;
                    Log?.Invoke($"Serial read warning ({_readErrorStreak}): {ex.Message}");
                }
            }
        }
    }
    public async Task<AutoDetectResult> AutoDetectAsync(CancellationToken token)
    {
        int[] bauds = { 9600, 115200, 19200, 38400, 57600, 230400, 460800, 921600, 4800 };
        string[] ports = GetPorts();
        foreach (string port in ports)
        {
            foreach (int baud in bauds)
            {
                token.ThrowIfCancellationRequested();
                Log?.Invoke($"Scanning {port} @{baud}...");
                if (await LooksLikeHwt905Async(port, baud, token).ConfigureAwait(false))
                    return new AutoDetectResult { Found = true, PortName = port, BaudRate = baud };
            }
        }
        return new AutoDetectResult { Found = false };
    }

    private static async Task<bool> LooksLikeHwt905Async(string portName, int baudRate, CancellationToken token)
    {
        using var tempPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 50,
            WriteTimeout = 100,
            DtrEnable = true,
            RtsEnable = true,
            ReadBufferSize = 8192
        };

        var parser = new Hwt905Parser();
        int good = 0;
        int usefulTypes = 0;
        parser.FrameReceived += f =>
        {
            good++;
            if (f.Type is 0x51 or 0x52 or 0x53 or 0x54 or 0x59) usefulTypes++;
        };

        try
        {
            tempPort.Open();
            byte[] buffer = new byte[512];
            var deadline = DateTime.UtcNow.AddMilliseconds(1250);
            while (DateTime.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();
                int count = tempPort.BytesToRead;
                if (count > 0)
                {
                    int read = tempPort.Read(buffer, 0, Math.Min(buffer.Length, count));
                    if (read > 0) parser.Push(buffer, read);
                    if (good >= 4 && usefulTypes >= 2) return true;
                }
                await Task.Delay(25, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
        return good >= 4 && usefulTypes >= 1;
    }

    private static string NaturalPortSortKey(string s)
    {
        string digits = new string(s.Where(char.IsDigit).ToArray());
        if (int.TryParse(digits, out int n))
            return $"{s.TrimEnd('0','1','2','3','4','5','6','7','8','9')}{n:000000}";
        return s;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
        _parser.FrameReceived -= Parser_FrameReceived;
        _parser.BadFrameReceived -= Parser_BadFrameReceived;
    }
}

public sealed class AutoDetectResult
{
    public bool Found { get; init; }
    public string PortName { get; init; } = string.Empty;
    public int BaudRate { get; init; }
}
