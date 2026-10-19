using AuroraPad.Core.Protocol;

namespace AuroraPad.Core.Processing;

/// <summary>
/// Removes gyro drift. It keeps learning the sensor's resting offset whenever the controller sits
/// still for a moment (continuous calibration) and can also be calibrated on demand.
/// Output is in degrees per second (raw / 16).
/// </summary>
public sealed class GyroCalibrator
{
    private const double StillThreshold = 6;   // raw units of deviation that still count as "not moving"
    private const double StillSeconds = 1.0;

    private double _biasX, _biasY, _biasZ;
    private double _sumX, _sumY, _sumZ;
    private int _samples;
    private double _stillSince = -1;
    private double _refX, _refY, _refZ;
    private TaskCompletionSource<bool>? _manual;

    public bool AutoCalibrate { get; set; } = true;
    public bool IsCalibrated { get; private set; }

    /// <summary>Corrected angular velocity in degrees per second: pitch (X), yaw (Y), roll (Z).</summary>
    public (double X, double Y, double Z) Dps { get; private set; }

    public void Update(DualSenseInput i, double now)
    {
        double gx = i.GyroX, gy = i.GyroY, gz = i.GyroZ;
        bool still = Math.Abs(gx - _refX) < StillThreshold && Math.Abs(gy - _refY) < StillThreshold && Math.Abs(gz - _refZ) < StillThreshold;
        if (!still || _stillSince < 0)
        {
            _refX = gx; _refY = gy; _refZ = gz;
            _stillSince = now;
            _sumX = _sumY = _sumZ = 0; _samples = 0;
        }
        else
        {
            _sumX += gx; _sumY += gy; _sumZ += gz; _samples++;
            bool learn = (AutoCalibrate || _manual != null) && now - _stillSince >= StillSeconds && _samples > 50;
            if (learn)
            {
                _biasX = _sumX / _samples; _biasY = _sumY / _samples; _biasZ = _sumZ / _samples;
                IsCalibrated = true;
                _manual?.TrySetResult(true);
                _manual = null;
                // Keep averaging over a fresh window so slow temperature drift is followed too.
                _stillSince = now; _sumX = _sumY = _sumZ = 0; _samples = 0;
            }
        }
        Dps = ((gx - _biasX) / 16.0, (gy - _biasY) / 16.0, (gz - _biasZ) / 16.0);
    }

    /// <summary>Waits until the controller has rested still long enough to learn the offset.</summary>
    public Task<bool> CalibrateAsync(TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _manual = tcs;
        _stillSince = -1;
        _ = Task.Delay(timeout).ContinueWith(_ => tcs.TrySetResult(false));
        return tcs.Task;
    }
}
