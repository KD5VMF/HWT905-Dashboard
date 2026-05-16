using System;
using HWT905Dashboard.Hardware;

namespace HWT905Dashboard.Models;

public sealed class SensorSnapshot
{
    private readonly object _gate = new();

    public DateTime LastUpdateUtc { get; private set; } = DateTime.MinValue;
    public long Packets { get; private set; }
    public long BadPackets { get; private set; }
    public int LastFrameType { get; private set; }

    public double AccX { get; private set; }
    public double AccY { get; private set; }
    public double AccZ { get; private set; }

    public double GyroX { get; private set; }
    public double GyroY { get; private set; }
    public double GyroZ { get; private set; }

    public double Roll { get; private set; }
    public double Pitch { get; private set; }
    public double Yaw { get; private set; }

    public double MagX { get; private set; }
    public double MagY { get; private set; }
    public double MagZ { get; private set; }

    public double Q0 { get; private set; } = 1;
    public double Q1 { get; private set; }
    public double Q2 { get; private set; }
    public double Q3 { get; private set; }

    public double TempC { get; private set; } = double.NaN;
    public string VersionText { get; private set; } = string.Empty;
    public string LastRawHex { get; private set; } = string.Empty;

    public bool HasAccel { get; private set; }
    public bool HasGyro { get; private set; }
    public bool HasAngle { get; private set; }
    public bool HasMag { get; private set; }
    public bool HasQuat { get; private set; }
    public bool HasTemp => !double.IsNaN(TempC);

    public void MarkBadPacket()
    {
        lock (_gate) BadPackets++;
    }

    public void Apply(Hwt905Frame frame)
    {
        lock (_gate)
        {
            Packets++;
            LastUpdateUtc = DateTime.UtcNow;
            LastFrameType = frame.Type;
            LastRawHex = BitConverter.ToString(frame.Bytes).Replace('-', ' ');

            switch (frame.Type)
            {
                case 0x51:
                    AccX = frame.S0 / 32768.0 * 16.0;
                    AccY = frame.S1 / 32768.0 * 16.0;
                    AccZ = frame.S2 / 32768.0 * 16.0;
                    TempC = SmoothTemp(frame.S3 / 100.0);
                    HasAccel = true;
                    break;

                case 0x52:
                    GyroX = frame.S0 / 32768.0 * 2000.0;
                    GyroY = frame.S1 / 32768.0 * 2000.0;
                    GyroZ = frame.S2 / 32768.0 * 2000.0;
                    // Some WIT frames repeat temperature here. Use it only if it is sane.
                    TempC = SmoothTemp(frame.S3 / 100.0);
                    HasGyro = true;
                    break;

                case 0x53:
                    Roll = frame.S0 / 32768.0 * 180.0;
                    Pitch = frame.S1 / 32768.0 * 180.0;
                    Yaw = Normalize360(frame.S2 / 32768.0 * 180.0);
                    HasAngle = true;
                    break;

                case 0x54:
                    MagX = frame.S0;
                    MagY = frame.S1;
                    MagZ = frame.S2;
                    HasMag = true;
                    break;

                case 0x59:
                    Q0 = frame.S0 / 32768.0;
                    Q1 = frame.S1 / 32768.0;
                    Q2 = frame.S2 / 32768.0;
                    Q3 = frame.S3 / 32768.0;
                    HasQuat = true;
                    break;

                case 0x5F:
                    VersionText = frame.ToAsciiOrHex();
                    break;
            }
        }
    }

    public SensorReadout Copy()
    {
        lock (_gate)
        {
            return new SensorReadout
            {
                LastUpdateUtc = LastUpdateUtc,
                Packets = Packets,
                BadPackets = BadPackets,
                LastFrameType = LastFrameType,
                AccX = AccX, AccY = AccY, AccZ = AccZ,
                GyroX = GyroX, GyroY = GyroY, GyroZ = GyroZ,
                Roll = Roll, Pitch = Pitch, Yaw = Yaw,
                MagX = MagX, MagY = MagY, MagZ = MagZ,
                Q0 = Q0, Q1 = Q1, Q2 = Q2, Q3 = Q3,
                TempC = TempC,
                VersionText = VersionText,
                LastRawHex = LastRawHex,
                HasAccel = HasAccel, HasGyro = HasGyro, HasAngle = HasAngle, HasMag = HasMag, HasQuat = HasQuat
            };
        }
    }

    private double SmoothTemp(double c)
    {
        // HWT905 temperature should be normal sensor-board temperature. Reject corrupted-looking jumps.
        if (double.IsNaN(c) || c < -40 || c > 125)
            return TempC;

        if (double.IsNaN(TempC))
            return c;

        if (Math.Abs(c - TempC) > 8)
            return TempC;

        return TempC * 0.88 + c * 0.12;
    }

    private static double Normalize360(double d)
    {
        d %= 360.0;
        if (d < 0) d += 360.0;
        return d;
    }
}

public sealed class SensorReadout
{
    public DateTime LastUpdateUtc { get; init; }
    public long Packets { get; init; }
    public long BadPackets { get; init; }
    public int LastFrameType { get; init; }

    public double AccX { get; init; }
    public double AccY { get; init; }
    public double AccZ { get; init; }

    public double GyroX { get; init; }
    public double GyroY { get; init; }
    public double GyroZ { get; init; }

    public double Roll { get; init; }
    public double Pitch { get; init; }
    public double Yaw { get; init; }

    public double MagX { get; init; }
    public double MagY { get; init; }
    public double MagZ { get; init; }

    public double Q0 { get; init; }
    public double Q1 { get; init; }
    public double Q2 { get; init; }
    public double Q3 { get; init; }

    public double TempC { get; init; }
    public double TempF => double.IsNaN(TempC) ? double.NaN : TempC * 9.0 / 5.0 + 32.0;
    public string VersionText { get; init; }
    public string LastRawHex { get; init; }

    public bool HasAccel { get; init; }
    public bool HasGyro { get; init; }
    public bool HasAngle { get; init; }
    public bool HasMag { get; init; }
    public bool HasQuat { get; init; }
    public bool HasTemp => !double.IsNaN(TempC);

    public double MagStrength => Math.Sqrt(MagX * MagX + MagY * MagY + MagZ * MagZ);
}
