using System;
using System.Collections.Generic;
using Godot;

public static class ProceduralAudioBank
{
    public const int SampleRate = 44100;

    private static readonly Dictionary<string, AudioStream> Streams =
        new(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, AudioStream> All => Streams;

    public static AudioStream Get(string cueId)
    {
        EnsureBuilt();
        return Streams.TryGetValue(cueId, out AudioStream? stream)
            ? stream
            : throw new KeyNotFoundException($"Unknown audio cue '{cueId}'.");
    }

    public static bool Contains(string cueId)
    {
        EnsureBuilt();
        return Streams.ContainsKey(cueId);
    }

    public static void EnsureBuilt()
    {
        if (Streams.Count > 0)
        {
            return;
        }

        Streams[AudioCue.UiClick] = MakeTone(0.075, 920.0, 1380.0, 0.25, false, 17);
        Streams[AudioCue.UiConfirm] = MakeTone(0.13, 660.0, 990.0, 0.30, false, 19);
        Streams[AudioCue.UiError] = MakeTone(0.18, 185.0, 148.0, 0.34, false, 23);
        Streams[AudioCue.VoiceRadio] = MakeRadioChirp();
        Streams[AudioCue.WeaponMultitool] = MakeWeaponPulse();
        Streams[AudioCue.ResourceCollect] = MakeTone(0.17, 310.0, 620.0, 0.28, false, 31);
        Streams[AudioCue.CraftComplete] = MakeTone(0.28, 440.0, 880.0, 0.26, false, 37);
        Streams[AudioCue.DamageAlert] = MakeTone(0.20, 220.0, 110.0, 0.34, false, 39);
        Streams[AudioCue.LifeSupportAlarm] = MakeTone(0.42, 520.0, 740.0, 0.23, false, 40);
        Streams[AudioCue.VehicleEngine] = MakeEngineLoop();
        Streams[AudioCue.AmbientAtmosphere] = MakeNoiseLoop(3.0, 0.075, 46.0, 41);
        Streams[AudioCue.AmbientInterior] = MakeHarmonicLoop(3.0, 58.0, 0.105, 43);
        Streams[AudioCue.AmbientWater] = MakeNoiseLoop(3.0, 0.055, 72.0, 47);
        Streams[AudioCue.WeatherWind] = MakeNoiseLoop(2.5, 0.115, 31.0, 53);
        Streams[AudioCue.MusicMenu] = MakeMusicLoop(6.0, 110.0, 1.0, 59);
        Streams[AudioCue.MusicSurface] = MakeMusicLoop(6.0, 146.83, 0.84, 61);
        Streams[AudioCue.MusicSpace] = MakeMusicLoop(6.0, 82.41, 0.66, 67);
        Streams[AudioCue.MusicInterior] = MakeMusicLoop(6.0, 130.81, 0.74, 71);
        Streams[AudioCue.MusicCombat] = MakeMusicLoop(4.0, 164.81, 1.42, 73);
    }

    private static AudioStreamWav MakeTone(
        double seconds,
        double frequencyA,
        double frequencyB,
        double amplitude,
        bool loop,
        uint seed)
    {
        int frames = Math.Max(1, (int)Math.Round(seconds * SampleRate));
        byte[] data = new byte[frames * 2];
        double phaseNoise = seed * 0.173;
        for (int i = 0; i < frames; i++)
        {
            double t = i / (double)SampleRate;
            double envelope = loop
                ? 1.0
                : Math.Pow(Math.Max(0.0, 1.0 - (i / (double)frames)), 1.7);
            double signal =
                (Math.Sin(Math.Tau * frequencyA * t) * 0.72) +
                (Math.Sin((Math.Tau * frequencyB * t) + phaseNoise) * 0.28);
            WriteSample(data, i, signal * amplitude * envelope);
        }
        return CreateWav(data, frames, loop);
    }

    private static AudioStreamWav MakeRadioChirp()
    {
        const double seconds = 0.24;
        int frames = (int)(seconds * SampleRate);
        byte[] data = new byte[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            double t = i / (double)SampleRate;
            double sweep = 360.0 + (820.0 * (i / (double)frames));
            double carrier = Math.Sin(Math.Tau * sweep * t);
            double harmonic = Math.Sin(Math.Tau * sweep * 2.03 * t) * 0.22;
            double gate = 0.62 + (0.38 * Math.Sin(Math.Tau * 19.0 * t));
            double envelope = Math.Sin(Math.PI * Math.Clamp(i / (double)frames, 0.0, 1.0));
            WriteSample(data, i, (carrier + harmonic) * gate * envelope * 0.24);
        }
        return CreateWav(data, frames, false);
    }

    private static AudioStreamWav MakeWeaponPulse()
    {
        const double seconds = 0.16;
        int frames = (int)(seconds * SampleRate);
        byte[] data = new byte[frames * 2];
        uint state = 0xC0FFEEu;
        double phase = 0.0;
        for (int i = 0; i < frames; i++)
        {
            double progress = i / (double)frames;
            double frequency = 520.0 - (360.0 * progress);
            phase += Math.Tau * frequency / SampleRate;
            double tone = Math.Sin(phase) * 0.65;
            double noise = NextSigned(ref state) * 0.35;
            double envelope = Math.Pow(1.0 - progress, 2.2);
            WriteSample(data, i, (tone + noise) * envelope * 0.48);
        }
        return CreateWav(data, frames, false);
    }

    private static AudioStreamWav MakeEngineLoop()
    {
        const double seconds = 2.0;
        int frames = (int)(seconds * SampleRate);
        byte[] data = new byte[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            double t = i / (double)SampleRate;
            double pulse =
                Math.Sin(Math.Tau * 52.0 * t) * 0.52 +
                Math.Sin(Math.Tau * 104.0 * t) * 0.24 +
                Math.Sin(Math.Tau * 156.0 * t) * 0.10;
            double turbine = Math.Sin(Math.Tau * 311.0 * t) * 0.08;
            // Periodic high-frequency components retain a noise-like texture while
            // returning to the exact same phase at the two-second loop boundary.
            double texture =
                Math.Sin(Math.Tau * 433.5 * t + 0.7) * 0.035 +
                Math.Sin(Math.Tau * 571.0 * t + 1.9) * 0.025;
            WriteSample(data, i, (pulse + turbine + texture) * 0.36);
        }
        return CreateWav(data, frames, true);
    }

    private static AudioStreamWav MakeNoiseLoop(
        double seconds,
        double amplitude,
        double lowFrequency,
        uint seed)
    {
        int frames = Math.Max(1, (int)(seconds * SampleRate));
        byte[] data = new byte[frames * 2];
        uint state = seed == 0 ? 1u : seed;
        const int componentCount = 10;
        int[] harmonics = new int[componentCount];
        double[] phases = new double[componentCount];
        double[] gains = new double[componentCount];
        for (int component = 0; component < componentCount; component++)
        {
            state = (state * 1664525u) + 1013904223u;
            harmonics[component] = 9 + (int)(state % 360u);
            state = (state * 1664525u) + 1013904223u;
            phases[component] = Math.Tau * ((state & 0xFFFFu) / 65535.0);
            gains[component] = 1.0 / Math.Sqrt(component + 1.0);
        }

        double baseCycle = 1.0 / Math.Max(0.05, seconds);
        double quantizedLow = Math.Max(1.0, Math.Round(lowFrequency * seconds)) * baseCycle;
        for (int i = 0; i < frames; i++)
        {
            double t = i / (double)SampleRate;
            double texture = 0.0;
            for (int component = 0; component < componentCount; component++)
            {
                texture += Math.Sin(
                    Math.Tau * harmonics[component] * baseCycle * t + phases[component]) *
                    gains[component];
            }
            texture /= 4.2;
            double modulation = 0.78 +
                (0.22 * Math.Sin(Math.Tau * 2.0 * baseCycle * t));
            double low = Math.Sin(Math.Tau * quantizedLow * t) * 0.11;
            WriteSample(data, i, (texture + low) * amplitude * modulation);
        }
        return CreateWav(data, frames, true);
    }

    private static AudioStreamWav MakeHarmonicLoop(
        double seconds,
        double rootFrequency,
        double amplitude,
        uint seed)
    {
        int frames = Math.Max(1, (int)(seconds * SampleRate));
        byte[] data = new byte[frames * 2];
        double phase = seed * 0.013;
        double frequencyA = QuantizeLoopFrequency(rootFrequency, seconds);
        double frequencyB = QuantizeLoopFrequency(rootFrequency * 2.0, seconds);
        double frequencyC = QuantizeLoopFrequency(rootFrequency * 3.01, seconds);
        double breathingFrequency = 1.0 / seconds;
        for (int i = 0; i < frames; i++)
        {
            double t = i / (double)SampleRate;
            double signal =
                Math.Sin(Math.Tau * frequencyA * t) * 0.58 +
                Math.Sin((Math.Tau * frequencyB * t) + phase) * 0.22 +
                Math.Sin((Math.Tau * frequencyC * t) + phase * 0.5) * 0.08;
            double breathing = 0.82 + (0.18 * Math.Sin(Math.Tau * breathingFrequency * t));
            WriteSample(data, i, signal * amplitude * breathing);
        }
        return CreateWav(data, frames, true);
    }

    private static AudioStreamWav MakeMusicLoop(
        double seconds,
        double rootFrequency,
        double brightness,
        uint seed)
    {
        int frames = Math.Max(1, (int)(seconds * SampleRate));
        byte[] data = new byte[frames * 2];
        double phase = seed * 0.021;
        double[] ratios = { 1.0, 1.25, 1.5, 2.0 };
        double[] frequencies = new double[ratios.Length];
        for (int voice = 0; voice < ratios.Length; voice++)
        {
            frequencies[voice] = QuantizeLoopFrequency(
                rootFrequency * ratios[voice], seconds);
        }
        double subFrequency = QuantizeLoopFrequency(rootFrequency * 0.5, seconds);
        for (int i = 0; i < frames; i++)
        {
            double t = i / (double)SampleRate;
            double cycle = i / (double)frames;
            double slow = 0.60 + (0.40 * Math.Pow(Math.Sin(Math.PI * cycle), 2.0));
            double signal = 0.0;
            for (int voice = 0; voice < ratios.Length; voice++)
            {
                double voiceGain = 0.46 / (voice + 1.0);
                signal += Math.Sin(
                    (Math.Tau * frequencies[voice] * t) +
                    (phase * voice)) * voiceGain;
            }
            signal += Math.Sin(Math.Tau * subFrequency * t) * 0.18;
            WriteSample(data, i, signal * 0.16 * slow * brightness);
        }
        return CreateWav(data, frames, true);
    }

    private static double QuantizeLoopFrequency(double frequency, double seconds)
    {
        double safeSeconds = Math.Max(0.05, seconds);
        return Math.Max(1.0, Math.Round(frequency * safeSeconds)) / safeSeconds;
    }

    private static AudioStreamWav CreateWav(byte[] data, int frames, bool loop)
    {
        AudioStreamWav wav = new()
        {
            Data = data,
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SampleRate,
            Stereo = false,
            LoopMode = loop
                ? AudioStreamWav.LoopModeEnum.Forward
                : AudioStreamWav.LoopModeEnum.Disabled,
            LoopBegin = 0,
            LoopEnd = loop ? frames : 0
        };
        return wav;
    }

    private static void WriteSample(byte[] data, int frame, double value)
    {
        short sample = (short)Math.Round(
            Math.Clamp(value, -0.98, 0.98) * short.MaxValue);
        int offset = frame * 2;
        data[offset] = (byte)(sample & 0xFF);
        data[offset + 1] = (byte)((sample >> 8) & 0xFF);
    }

    private static double NextSigned(ref uint state)
    {
        state = (state * 1664525u) + 1013904223u;
        return ((state >> 8) / 8388607.5) - 1.0;
    }
}

public static class AudioCue
{
    public const string UiClick = "audio.cue.ui_click";
    public const string UiConfirm = "audio.cue.ui_confirm";
    public const string UiError = "audio.cue.ui_error";
    public const string VoiceRadio = "voice.radio";
    public const string WeaponMultitool = "sfx.weapon.multitool";
    public const string ResourceCollect = "sfx.resource.collect";
    public const string CraftComplete = "sfx.craft.complete";
    public const string DamageAlert = "audio.cue.damage_alert";
    public const string LifeSupportAlarm = "audio.cue.life_support_alarm";
    public const string VehicleEngine = "vehicle.engine";
    public const string AmbientAtmosphere = "ambient.atmosphere";
    public const string AmbientInterior = "ambient.interior";
    public const string AmbientWater = "ambient.water";
    public const string WeatherWind = "weather.wind";
    public const string MusicMenu = "music.menu";
    public const string MusicSurface = "music.surface";
    public const string MusicSpace = "music.space";
    public const string MusicInterior = "music.interior";
    public const string MusicCombat = "music.combat";
}
