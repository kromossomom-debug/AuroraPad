using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AuroraPad.Core.Haptics;

public enum HapticsMode { Off, RumbleToHaptics, AudioToHaptics, Both }

/// <summary>
/// Drives the DualSense voice-coil actuators through its USB audio interface: the controller
/// is a 4-channel playback device and channels 3/4 feed the left/right haptic actuators.
/// Sources: synthetic waves from game rumble, and/or low-passed system audio (loopback).
/// Only works over USB; over Bluetooth the controller has no audio endpoint.
/// </summary>
public sealed class HapticsEngine : IDisposable
{
    private readonly MMDevice _device;
    private readonly WasapiOut _out;
    private readonly HapticsMixer _mixer;
    private WasapiLoopbackCapture? _loopback;
    private BufferedWaveProvider? _loopBuffer;

    // Read once: MMDevice.FriendlyName opens the COM property store on every access and is slow
    // enough (tens of ms) to stall a UI that polls it every frame.
    public string DeviceName { get; private set; } = "";

    /// <summary>Last failure from <see cref="TryCreate"/>, for diagnostics.</summary>
    public static Exception? LastException { get; private set; }

    private HapticsEngine(MMDevice device)
    {
        _device = device;
        DeviceName = device.FriendlyName;
        // Shared-mode WASAPI only accepts the device's own mix format (WaveFormatExtensible with a
        // channel mask), so samples are rendered in that exact format instead of a plain float format.
        var mix = device.AudioClient.MixFormat;
        MixFormatDescription = mix.ToString();
        _mixer = new HapticsMixer(mix.SampleRate, mix.Channels);
        _out = new WasapiOut(device, AudioClientShareMode.Shared, true, 20);
        _out.Init(new NativeFormatProvider(_mixer, mix));
        _out.Play();
    }

    public string MixFormatDescription { get; }

    /// <summary>Converts float samples to the device mix format (32-bit float or 16/24-bit PCM).</summary>
    private sealed class NativeFormatProvider(ISampleProvider source, WaveFormat format) : IWaveProvider
    {
        private float[] _buf = [];
        public WaveFormat WaveFormat { get; } = format;

        public int Read(byte[] buffer, int offset, int count)
        {
            int bytesPerSample = WaveFormat.BitsPerSample / 8;
            int samples = count / bytesPerSample;
            if (_buf.Length < samples) _buf = new float[samples];
            int read = source.Read(_buf, 0, samples);
            switch (WaveFormat.BitsPerSample)
            {
                case 32 when IsFloat(WaveFormat):
                    Buffer.BlockCopy(_buf, 0, buffer, offset, read * 4);
                    break;
                case 16:
                    for (int i = 0; i < read; i++)
                        BitConverter.TryWriteBytes(buffer.AsSpan(offset + i * 2), (short)(Math.Clamp(_buf[i], -1f, 1f) * short.MaxValue));
                    break;
                case 24:
                    for (int i = 0; i < read; i++)
                    {
                        int v = (int)(Math.Clamp(_buf[i], -1f, 1f) * 8388607);
                        buffer[offset + i * 3] = (byte)v; buffer[offset + i * 3 + 1] = (byte)(v >> 8); buffer[offset + i * 3 + 2] = (byte)(v >> 16);
                    }
                    break;
                default: // 32-bit integer PCM
                    for (int i = 0; i < read; i++)
                        BitConverter.TryWriteBytes(buffer.AsSpan(offset + i * 4), (int)(Math.Clamp(_buf[i], -1f, 1f) * int.MaxValue));
                    break;
            }
            return read * bytesPerSample;
        }

        private static bool IsFloat(WaveFormat f) =>
            f.Encoding == WaveFormatEncoding.IeeeFloat
            || (f is WaveFormatExtensible ext && ext.SubFormat == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT);
    }

    /// <summary>Finds the n-th DualSense playback endpoint (0 = first) and starts output.</summary>
    public static HapticsEngine? TryCreate(int index, out string? error)
    {
        error = null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Where(d => d.FriendlyName.Contains("DualSense", StringComparison.OrdinalIgnoreCase)
                            || d.FriendlyName.Contains("Wireless Controller", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (devices.Count == 0) { error = "nenhuma saída de áudio do DualSense (conecte por USB)"; return null; }
            var dev = devices[Math.Min(index, devices.Count - 1)];
            if (dev.AudioClient.MixFormat.Channels < 4) { error = $"{dev.FriendlyName} tem só {dev.AudioClient.MixFormat.Channels} canais"; return null; }
            return new HapticsEngine(dev);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            LastException = ex;
            return null;
        }
    }

    public void Configure(HapticsMode mode, double gain, int lowPassHz)
    {
        _mixer.UseRumble = mode is HapticsMode.RumbleToHaptics or HapticsMode.Both;
        _mixer.Gain = (float)Math.Clamp(gain, 0, 4);
        bool wantAudio = mode is HapticsMode.AudioToHaptics or HapticsMode.Both;
        if (wantAudio && _loopback == null) StartLoopback(lowPassHz);
        else if (!wantAudio && _loopback != null) StopLoopback();
        _mixer.SetLowPass(lowPassHz);
    }

    /// <summary>
    /// Plays an audio file (wav, mp3, m4a…) once through the actuators, e.g. a haptic effect
    /// designed as audio. Returns an error message, or null on success.
    /// </summary>
    public string? PlayFile(string path)
    {
        try
        {
            var reader = new MediaFoundationReader(path);
            ISampleProvider src = reader.ToSampleProvider();
            if (src.WaveFormat.Channels > 1) src = new MonoMixer(src);
            if (src.WaveFormat.SampleRate != _mixer.WaveFormat.SampleRate)
                src = new WdlResamplingSampleProvider(src, _mixer.WaveFormat.SampleRate);
            _mixer.SetFile(src, reader);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>Rumble motor values (0-255) from the game.</summary>
    public void SetRumble(byte large, byte small) => _mixer.SetRumble(large / 255f, small / 255f);

    private void StartLoopback(int lowPassHz)
    {
        // Capture what the default speakers play (not the DualSense itself) and mix it down.
        _loopback = new WasapiLoopbackCapture();
        _loopBuffer = new BufferedWaveProvider(_loopback.WaveFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromMilliseconds(200),
        };
        _loopback.DataAvailable += (_, e) => _loopBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        ISampleProvider src = _loopBuffer.ToSampleProvider();
        if (src.WaveFormat.Channels > 1) src = new MonoMixer(src);
        if (src.WaveFormat.SampleRate != _mixer.WaveFormat.SampleRate)
            src = new WdlResamplingSampleProvider(src, _mixer.WaveFormat.SampleRate);
        _mixer.AudioSource = src;
        _mixer.SetLowPass(lowPassHz);
        _loopback.StartRecording();
    }

    private void StopLoopback()
    {
        _mixer.AudioSource = null;
        try { _loopback?.StopRecording(); } catch { }
        _loopback?.Dispose();
        _loopback = null;
        _loopBuffer = null;
    }

    public void Dispose()
    {
        StopLoopback();
        try { _out.Stop(); } catch { }
        _out.Dispose();
        _device.Dispose();
    }

    /// <summary>Averages all channels of a source into mono.</summary>
    private sealed class MonoMixer(ISampleProvider source) : ISampleProvider
    {
        private float[] _buf = [];
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            int ch = source.WaveFormat.Channels;
            if (_buf.Length < count * ch) _buf = new float[count * ch];
            int read = source.Read(_buf, 0, count * ch) / ch;
            for (int i = 0; i < read; i++)
            {
                float s = 0;
                for (int c = 0; c < ch; c++) s += _buf[i * ch + c];
                buffer[offset + i] = s / ch;
            }
            return read;
        }
    }

    /// <summary>Produces the 4+ channel stream: speakers silent, channels 3/4 = haptics.</summary>
    private sealed class HapticsMixer : ISampleProvider
    {
        private readonly int _channels;
        private readonly object _lock = new();
        private float _large, _small, _phaseL, _phaseS;
        private float[] _audio = [];
        private BiQuadFilter? _lowPass;

        public WaveFormat WaveFormat { get; }
        public bool UseRumble { get; set; }
        public float Gain { get; set; } = 1f;
        public ISampleProvider? AudioSource { get; set; }
        private ISampleProvider? _file;
        private IDisposable? _fileOwner;
        private float[] _fileBuf = [];

        public void SetFile(ISampleProvider src, IDisposable owner)
        {
            lock (_lock)
            {
                _fileOwner?.Dispose();
                _file = src;
                _fileOwner = owner;
            }
        }

        public HapticsMixer(int sampleRate, int channels)
        {
            _channels = channels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        }

        public void SetRumble(float large, float small) { _large = large; _small = small; }

        public void SetLowPass(int hz)
        {
            lock (_lock) _lowPass = BiQuadFilter.LowPassFilter(WaveFormat.SampleRate, Math.Clamp(hz, 40, 1000), 0.707f);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / _channels;
            Array.Clear(buffer, offset, frames * _channels);

            int audioFrames = 0;
            var src = AudioSource;
            if (src != null)
            {
                if (_audio.Length < frames) _audio = new float[frames];
                audioFrames = src.Read(_audio, 0, frames);
            }

            int fileFrames = 0;
            lock (_lock)
            {
                if (_file != null)
                {
                    if (_fileBuf.Length < frames) _fileBuf = new float[frames];
                    fileFrames = _file.Read(_fileBuf, 0, frames);
                    if (fileFrames < frames) { _fileOwner?.Dispose(); _file = null; _fileOwner = null; } // finished
                }
            }

            float rate = WaveFormat.SampleRate;
            // Large motor ≈ low thump, small motor ≈ higher buzz; actuators respond well to 50-300 Hz.
            float stepL = 2 * MathF.PI * 60 / rate, stepS = 2 * MathF.PI * 170 / rate;
            lock (_lock)
            {
                for (int i = 0; i < frames; i++)
                {
                    float h = 0;
                    if (UseRumble)
                    {
                        h += _large * MathF.Sin(_phaseL) * 0.9f + _small * MathF.Sin(_phaseS) * 0.6f;
                        _phaseL = (_phaseL + stepL) % (2 * MathF.PI);
                        _phaseS = (_phaseS + stepS) % (2 * MathF.PI);
                    }
                    if (i < fileFrames) h += _fileBuf[i];
                    if (i < audioFrames && _lowPass != null)
                        h += _lowPass.Transform(_audio[i]) * 3f;
                    h = Math.Clamp(h * Gain, -1f, 1f);
                    int b = offset + i * _channels;
                    buffer[b + 2] = h; // left actuator
                    buffer[b + 3] = h; // right actuator
                }
            }
            return frames * _channels;
        }
    }
}
