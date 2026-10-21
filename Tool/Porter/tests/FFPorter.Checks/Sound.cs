using System.IO;
using System.Runtime.InteropServices;
using FFPorter.Core;
using FFPorter.Core.Common.Tools;
using FFPorter.Core.T7.Sound;

internal static class SoundChecks
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Info
    {
        public long Frames;
        public int SampleRate;
        public int Channels;
        public int Format;
        public int Sections;
        public int Seekable;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private delegate nint OpenFile(string path, int mode, ref Info info);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long WriteShort(nint file, short[] samples, long frames);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CloseFile(nint file);

    private static short[] Tone(int frames, int channels, int seed)
    {
        var dice = new Random(seed);
        var pcm = new short[frames * channels];
        for (int frame = 0; frame < frames; frame++)
        {
            for (int channel = 0; channel < channels; channel++)
                pcm[frame * channels + channel] = (short)(9000 * Math.Sin(2 * Math.PI * (3 + channel) * frame / frames) + dice.Next(-400, 400));
        }
        return pcm;
    }

    public static void Run(string scratch)
    {
        Console.WriteLine("== sound: very short looping sounds");
        Workspace workspace = Workspace.Locate();
        string library = ToolData.Locate(workspace, "audio/libsndfile_x64.dll");
        if (!File.Exists(library))
        {
            Program.Check(false, $"the audio library is there to test with ({library})");
            return;
        }
        nint module = NativeLibrary.Load(library);
        var open = Marshal.GetDelegateForFunctionPointer<OpenFile>(NativeLibrary.GetExport(module, "sf_wchar_open"));
        var write = Marshal.GetDelegateForFunctionPointer<WriteShort>(NativeLibrary.GetExport(module, "sf_writef_short"));
        var close = Marshal.GetDelegateForFunctionPointer<CloseFile>(NativeLibrary.GetExport(module, "sf_close"));
        string folder = Path.Combine(scratch, "sound");
        Directory.CreateDirectory(folder);

        byte[] Flac(short[] pcm, int channels)
        {
            string path = Path.Combine(folder, $"loop{pcm.Length}_{channels}.flac");
            var info = new Info { SampleRate = T7SoundConvert.OutputRate, Channels = channels, Format = 0x170002 };
            nint file = open(path, 0x20, ref info);
            if (file == 0)
                throw new IOException("the test FLAC could not be written");
            write(file, pcm, pcm.Length / channels);
            close(file);
            return File.ReadAllBytes(path);
        }

        using Lame lame = Lame.Load(workspace);
        byte[] Convert(short[] pcm, int channels, out T7SoundConvert.Result result)
        {
            var bank = new T7SoundBank();
            bank.Entries.Add(new T7SoundBank.Entry
            {
                Id = 7, Data = Flac(pcm, channels), FrameCount = (uint)(pcm.Length / channels), RateIndex = 6, Channels = (byte)channels, Looping = 1,
                Format = T7SoundBank.FormatFlac, Name = "test_loop",
            });
            result = T7SoundConvert.Convert(bank, workspace, lame);
            return result.Bank.Entries[0].Data;
        }
        byte[] Expected(short[] feed, int channels, int loopFrames)
        {
            byte[] encoded = lame.Encode(feed, channels, T7SoundConvert.OutputRate);
            List<(int Offset, int Length)> frames = T7SoundConvert.Mp3Frames(encoded);
            return encoded.AsSpan(frames[1].Offset, frames[loopFrames + 1].Offset - frames[1].Offset).ToArray();
        }

        const int frame = T7SoundConvert.FrameSamples;
        short[] four = Tone(4 * frame, 1, 1);
        var old = new short[576 + four.Length + 2304];
        four.AsSpan(four.Length - 576, 576).CopyTo(old);
        four.CopyTo(old.AsSpan(576));
        four.AsSpan(0, 2304).CopyTo(old.AsSpan(576 + four.Length));
        byte[] converted = Convert(four, 1, out T7SoundConvert.Result fourResult);
        Program.Check(converted.AsSpan().SequenceEqual(Expected(old, 1, 4)) && T7SoundConvert.Mp3Frames(converted).Count == 4 && fourResult.Retimed == 0,
            $"a loop of normal length converts to the same bytes as before the fix ({converted.Length:N0} bytes, 4 frames)");

        short[] one = Tone(frame, 1, 2);
        var wrapped = new short[576 + frame + 2304];
        one.AsSpan(frame - 576, 576).CopyTo(wrapped);
        one.CopyTo(wrapped.AsSpan(576));
        one.CopyTo(wrapped.AsSpan(576 + frame));
        one.CopyTo(wrapped.AsSpan(576 + 2 * frame));
        Exception? failure = null;
        converted = [];
        try
        {
            converted = Convert(one, 1, out _);
        }
        catch (Exception error)
        {
            failure = error;
        }
        Program.Check(failure == null && converted.AsSpan().SequenceEqual(Expected(wrapped, 1, 1)) && T7SoundConvert.Mp3Frames(converted).Count == 1,
            $"a loop of exactly one MP3 frame, which used to stop the whole sound bank, now converts to that one frame repeated ({failure?.GetType().Name ?? "ok"})");

        foreach ((int frames, int channels) in ((int, int)[])[(1000, 1), (300, 1), (900, 2), (1700, 2)])
        {
            failure = null;
            T7SoundConvert.Result? result = null;
            try
            {
                converted = Convert(Tone(frames, channels, frames), channels, out result);
            }
            catch (Exception error)
            {
                failure = error;
            }
            Program.Check(failure == null && T7SoundConvert.Mp3Frames(converted).Count == 1 && result is { Retimed: 1 },
                $"a {frames}-sample {(channels == 1 ? "mono" : "stereo")} loop converts to a single frame ({failure?.Message ?? $"{converted.Length} bytes"})");
        }
        Program.Check(T7SoundConvert.RulesVersion == "t7-sound-3", "the sound rules version is unchanged, so sound that was already converted is not converted again");
    }
}
