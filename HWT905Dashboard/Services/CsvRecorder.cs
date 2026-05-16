using System;
using System.Globalization;
using System.IO;
using HWT905Dashboard.Models;

namespace HWT905Dashboard.Services;

public sealed class CsvRecorder : IDisposable
{
    private StreamWriter _writer;
    private DateTime _lastWriteUtc = DateTime.MinValue;
    private DateTime _lastFlushUtc = DateTime.MinValue;
    private bool _disposed;

    public bool IsRecording => _writer != null;
    public string CurrentFile { get; private set; } = string.Empty;
    public string CurrentFolder { get; private set; } = string.Empty;

    public void Start(string folder)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CsvRecorder));
        Stop();
        Directory.CreateDirectory(folder);
        CurrentFolder = folder;
        CurrentFile = Path.Combine(folder, "HWT905_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".csv");
        _writer = new StreamWriter(CurrentFile, false) { AutoFlush = false };
        _writer.WriteLine("local_time,utc_time,packets,bad_packets,acc_x_g,acc_y_g,acc_z_g,gyro_x_dps,gyro_y_dps,gyro_z_dps,roll_deg,pitch_deg,yaw_deg,mag_x,mag_y,mag_z,temp_c,temp_f,q0,q1,q2,q3,last_frame_type");
        _writer.Flush();
        _lastWriteUtc = DateTime.MinValue;
        _lastFlushUtc = DateTime.UtcNow;
    }

    public void Stop()
    {
        var writer = _writer;
        _writer = null;
        if (writer != null)
        {
            try { writer.Flush(); } catch { }
            writer.Dispose();
        }
    }

    public void MaybeWrite(SensorReadout s)
    {
        var writer = _writer;
        if (writer == null) return;

        var now = DateTime.UtcNow;
        if ((now - _lastWriteUtc).TotalMilliseconds < 100) return;
        _lastWriteUtc = now;

        string F(double d) => double.IsNaN(d) ? "" : d.ToString("0.######", CultureInfo.InvariantCulture);
        writer.WriteLine(string.Join(',', new[]
        {
            DateTime.Now.ToString("O"),
            now.ToString("O"),
            s.Packets.ToString(CultureInfo.InvariantCulture),
            s.BadPackets.ToString(CultureInfo.InvariantCulture),
            F(s.AccX), F(s.AccY), F(s.AccZ),
            F(s.GyroX), F(s.GyroY), F(s.GyroZ),
            F(s.Roll), F(s.Pitch), F(s.Yaw),
            F(s.MagX), F(s.MagY), F(s.MagZ),
            F(s.TempC), F(s.TempF),
            F(s.Q0), F(s.Q1), F(s.Q2), F(s.Q3),
            s.LastFrameType.ToString("X2")
        }));

        if ((now - _lastFlushUtc).TotalSeconds >= 5)
        {
            writer.Flush();
            _lastFlushUtc = now;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
