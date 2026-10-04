using System.Runtime.InteropServices;

namespace PersonalMediaPlayer.App.Playback;

internal static class PlaybackAudio
{
    // LibVLC 3 on Windows writes MediaPlayer.Volume into the process audio session,
    // so every player gets louder or quieter together. DirectSound keeps that level
    // on each player's own buffer. volume-save would write one shared DirectSound level.
    private const string OutputOption = "--aout=directsound";
    private const string NoSavedOutputVolume = "--no-volume-save";
    private static int _sessionReady;

    public static string[] Options(params string[] more)
    {
        ReleaseSharedSessionVolume();
        var options = new string[more.Length + 2];
        Array.Copy(more, options, more.Length);
        options[more.Length] = OutputOption;
        options[more.Length + 1] = NoSavedOutputVolume;
        return options;
    }

    private static void ReleaseSharedSessionVolume()
    {
        if (Interlocked.Exchange(ref _sessionReady, 1) != 0)
        {
            return;
        }

        object? enumerator = null;
        object? device = null;
        object? manager = null;
        object? volume = null;
        try
        {
            enumerator = new MMDeviceEnumerator();
            var hr = ((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(0, 0, out var endpoint);
            if (hr < 0 || endpoint is null)
            {
                return;
            }

            device = endpoint;
            var sessionManagerId = typeof(IAudioSessionManager).GUID;
            hr = endpoint.Activate(ref sessionManagerId, 1, IntPtr.Zero, out var activated);
            if (hr < 0 || activated is null)
            {
                return;
            }

            manager = activated;
            hr = ((IAudioSessionManager)activated).GetSimpleAudioVolume(Guid.Empty, 0, out var simple);
            if (hr < 0 || simple is null)
            {
                return;
            }

            volume = simple;
            // An older build stored the slider in this session. Put it back to full
            // once, then leave the Windows mixer alone. Each slider is a buffer level.
            simple.SetMute(false, Guid.Empty);
            simple.SetMasterVolume(1f, Guid.Empty);
        }
        catch
        {
            // A missed reset must not stop playback. Buffer volume still applies.
        }
        finally
        {
            Release(volume);
            Release(manager);
            Release(device);
            Release(enumerator);
        }
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.ReleaseComObject(com);
        }
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, uint stateMask, out IMMDevice devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, uint classContext, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport]
    [Guid("BFA971F1-4D5E-40BB-935E-967039BFBEE4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager
    {
        [PreserveSig]
        int GetAudioSessionControl([MarshalAs(UnmanagedType.LPStruct)] Guid sessionId, uint streamFlags, [MarshalAs(UnmanagedType.IUnknown)] out object sessionControl);

        [PreserveSig]
        int GetSimpleAudioVolume([MarshalAs(UnmanagedType.LPStruct)] Guid sessionId, uint streamFlags, out ISimpleAudioVolume audioVolume);
    }

    [ComImport]
    [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        [PreserveSig]
        int SetMasterVolume(float level, [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);

        [PreserveSig]
        int GetMasterVolume(out float level);

        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);

        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
