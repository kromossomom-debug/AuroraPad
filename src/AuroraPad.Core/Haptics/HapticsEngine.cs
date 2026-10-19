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
/// Over Bluetooth there is no audio endpoint, so the same mix is down-sampled to 3 kHz and sent as
/// HID output report 0x32 (format documented by the SAxense proof of concept).
/// </summary>
public sealed class HapticsEngine : IDisposable
{
    private readonly MMDevice? _device;
    private readonly WasapiOut? _out;
    private readonly Thread? _btThread;
    private readonly Action<byte[]>? _btWrite;
    private volatile bool _stopped;
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

    /// <summary>True when this engine streams over Bluetooth HID instead of the USB sound card.</summary>
    public bool IsBluetooth => _btWrite != null;

    // ---- Bluetooth backend -------------------------------------------------------

    private const int BtSampleRate = 3000;
    private const int BtFramesPerReport = 32;         // 32 stereo frames = 64 bytes = 10.67 ms
    private const int BtReportSize = 141;

    private HapticsEngine(Action<byte[]> writeReport, string name)
    {
        _btWrite = writeReport;
        DeviceName = name;
        MixFormatDescription = "Bluetooth HID 0x32, 8-bit stereo @ 3 kHz";
        _mixer = new HapticsMixer(BtSampleRate, 4);
        _btThread = new Thread(BluetoothLoop) { IsBackground = true, Name = "Bluetooth haptics", Priority = ThreadPriority.AboveNormal };
        _btThread.Start();
    }

    /// <summary>Haptics for a DualSense connected over Bluetooth; <paramref name="writeReport"/> sends a raw output report.</summary>
    public static HapticsEngine CreateBluetooth(Action<byte[]> writeReport, string name) => new(writeReport, name);

    [System.Runtime.InteropServices.DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);
    [System.Runtime.InteropServices.DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint ms);

    private void BluetoothLoop()
    {
        // Report layout: [0]=0x32, [1]=seq<<4 | tag, then packets {header(pid | 0x80 "sized"), length, data}:
        //   0x11 (7 bytes): FE 00 00 00 00 FF <counter>, 0x12 (64 bytes): interleaved signed 8-bit L/R samples,
        // zero padding, and CRC-32 (seeded with 0xA2) over bytes 0..136 stored little-endian at 137.
        var report = new byte[BtReportSize];
        var frames = new float[BtFramesPerReport * 4];
        byte seq = 0, counter = 0;
        double period = BtFramesPerReport * 1000.0 / BtSampleRate;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        double next = 0;
        timeBeginPeriod(1);
        try
        {
            while (!_stopped)
            {
                _mixer.Read(frames, 0, frames.Length);
                Array.Clear(report);
                report[0] = 0x32;
                report[1] = (byte)(seq << 4);
                seq = (byte)((seq + 1) & 0x0F);
                report[2] = 0x80 | 0x11; report[3] = 7;
                report[4] = 0xFE; report[9] = 0xFF; report[10] = counter++;
                report[11] = 0x80 | 0x12; report[12] = 64;
                for (int i = 0; i < BtFramesPerReport; i++)
                {
                    report[13 + i * 2] = (byte)(sbyte)Math.Clamp(frames[i * 4 + 2] * 127f, -127f, 127f); // left actuator
                    report[14 + i * 2] = (byte)(sbyte)Math.Clamp(frames[i * 4 + 3] * 127f, -127f, 127f); // right actuator
                }
                uint crc = Protocol.Crc32.Compute(0xA2, report.AsSpan(0, BtReportSize - 4));
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(BtReportSize - 4), crc);
                try { _btWrite!(report); } catch { /* controller asleep or link busy: drop this block */ }

                next += period;
                // Sleep only (1 ms timer resolution): busy-waiting for sub-ms precision cost ~10% of a CPU core,
                // and the controller buffers enough that ±1 ms of jitter is not noticeable.
                double wait = next - clock.Elapsed.TotalMilliseconds;
                if (wait >= 1) Thread.Sleep((int)Math.Round(wait));
                if (clock.Elapsed.TotalMilliseconds - next > 100) next = clock.Elapsed.TotalMilliseconds; // resync after a stall
            }
        }
        finally { timeEndPeriod(1); }
    }

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

    public string? SpeakerError { get; private set; }

    /// <param name="speaker">Mirror the PC's sound to the controller's speaker / headset jack (USB only).</param>
    public void Configure(HapticsMode mode, double gain, int lowPassHz, bool speaker = false, double speakerVolume = 1)
    {
        _mixer.UseRumble = mode is HapticsMode.RumbleToHaptics or HapticsMode.Both;
        _mixer.UseAudioHaptics = mode is HapticsMode.AudioToHaptics or HapticsMode.Both;
        _mixer.Gain = (float)Math.Clamp(gain, 0, 4);
        SpeakerError = null;
        if (speaker && IsBluetooth) { speaker = false; SpeakerError = "o som pelo controle só funciona com o cabo USB"; }
        if (speaker && DefaultOutputIsThisController()) { speaker = false; SpeakerError = "o próprio controle já é a saída de som padrão do Windows"; }
        _mixer.Speaker = speaker;
        _mixer.SpeakerVolume = (float)Math.Clamp(speakerVolume, 0, 1);
        bool wantLoopback = _mixer.UseAudioHaptics || speaker;
        if (wantLoopback && _loopback == null) StartLoopback(lowPassHz);
        else if (!wantLoopback && _loopback != null) StopLoopback();
        _mixer.SetLowPass(lowPassHz);
    }

    /// <summary>Capturing the controller's own output and playing it back would loop forever.</summary>
    private bool DefaultOutputIsThisController()
    {
        if (_device == null) return false;
        try
        {
            using var en = new MMDeviceEnumerator();
            using var def = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return def.ID == _device.ID;
        }
        catch { return false; }
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
        ISampleProvider src = new StereoMixer(_loopBuffer.ToSampleProvider());
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
        _stopped = true;
        StopLoopback();
        _btThread?.Join(200);
        try { _out?.Stop(); } catch { }
        _out?.Dispose();
        _device?.Dispose();
    }

    /// <summary>Turns any channel layout into stereo (mono is duplicated, surround keeps front L/R).</summary>
    private sealed class StereoMixer(ISampleProvider source) : ISampleProvider
    {
        private float[] _buf = [];
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            int ch = source.WaveFormat.Channels;
            int frames = count / 2;
            if (ch == 2) return source.Read(buffer, offset, frames * 2);
            if (_buf.Length < frames * ch) _buf = new float[frames * ch];
            int read = source.Read(_buf, 0, frames * ch) / ch;
            for (int i = 0; i < read; i++)
            {
                float l = _buf[i * ch], r = ch > 1 ? _buf[i * ch + 1] : l;
                buffer[offset + i * 2] = l;
                buffer[offset + i * 2 + 1] = r;
            }
            return read * 2;
        }
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
        public bool UseAudioHaptics { get; set; }
        public bool Speaker { get; set; }
        public float SpeakerVolume { get; set; } = 1f;
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
            // Keep the cut-off below Nyquist (the Bluetooth stream runs at only 3 kHz).
            lock (_lock) _lowPass = BiQuadFilter.LowPassFilter(WaveFormat.SampleRate, Math.Clamp(hz, 40, Math.Min(1000, WaveFormat.SampleRate / 2 - 100)), 0.707f);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / _channels;
            Array.Clear(buffer, offset, frames * _channels);

            int audioFrames = 0;
            var src = AudioSource;
            if (src != null)
            {
                if (_audio.Length < frames * 2) _audio = new float[frames * 2];
                audioFrames = src.Read(_audio, 0, frames * 2) / 2; // stereo
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
                    int b = offset + i * _channels;
                    if (i < audioFrames)
                    {
                        float l = _audio[i * 2], r = _audio[i * 2 + 1];
                        if (UseAudioHaptics && _lowPass != null) h += _lowPass.Transform((l + r) * 0.5f) * 3f;
                        if (Speaker) { buffer[b] = l * SpeakerVolume; buffer[b + 1] = r * SpeakerVolume; } // speaker / headset
                    }
                    h = Math.Clamp(h * Gain, -1f, 1f);
                    buffer[b + 2] = h; // left actuator
                    buffer[b + 3] = h; // right actuator
                }
            }
            return frames * _channels;
        }
    }
}
