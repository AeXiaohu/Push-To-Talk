using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PushToTalk
{
    public sealed class MicrophoneInfo
    {
        public string Id { get; private set; }
        public string Name { get; private set; }

        internal MicrophoneInfo(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public override string ToString() { return Name; }
    }

    public static class AudioDevices
    {
        // All methods use Windows Core Audio control APIs only; no audio is recorded.
        public static List<MicrophoneInfo> EnumerateMicrophones()
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDeviceCollection collection = null;
            var result = new List<MicrophoneInfo>();
            try
            {
                enumerator = CoreAudio.CreateEnumerator();
                CoreAudio.Check(enumerator.EnumAudioEndpoints(EDataFlow.Capture, CoreAudio.DeviceStateActive, out collection), Localization.Text("无法读取麦克风列表", "Unable to read the microphone list"));
                uint count;
                CoreAudio.Check(collection.GetCount(out count), Localization.Text("无法读取麦克风数量", "Unable to read the microphone count"));
                for (uint index = 0; index < count; index++)
                {
                    IMMDevice device = null;
                    try
                    {
                        CoreAudio.Check(collection.Item(index, out device), Localization.Text("无法读取麦克风", "Unable to read the microphone"));
                        uint state;
                        CoreAudio.Check(device.GetState(out state), Localization.Text("无法读取麦克风状态", "Unable to read the microphone status"));
                        if ((state & CoreAudio.DeviceStateActive) == 0) continue;
                        string id = CoreAudio.ReadId(device);
                        result.Add(new MicrophoneInfo(id, CoreAudio.ReadName(device, id)));
                    }
                    catch (COMException)
                    {
                        // A device may disappear after the enumeration snapshot was taken.
                    }
                    finally { CoreAudio.Release(device); }
                }
                return result;
            }
            finally
            {
                CoreAudio.Release(collection);
                CoreAudio.Release(enumerator);
            }
        }

        // Communications is preferred for voice chat; console is the fallback.
        // Null means that neither role currently has a default microphone.
        public static string GetDefaultMicrophoneId()
        {
            IMMDeviceEnumerator enumerator = null;
            try
            {
                enumerator = CoreAudio.CreateEnumerator();
                ERole[] roles = { ERole.Communications, ERole.Console };
                foreach (ERole role in roles)
                {
                    IMMDevice device = null;
                    try
                    {
                        int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.Capture, role, out device);
                        if (hr == CoreAudio.NotFound) continue;
                        CoreAudio.Check(hr, Localization.Text("无法读取默认麦克风", "Unable to read the default microphone"));
                        return CoreAudio.ReadId(device);
                    }
                    finally { CoreAudio.Release(device); }
                }
                return null;
            }
            finally { CoreAudio.Release(enumerator); }
        }
    }

    // Keep a session on the UI thread and dispose it before changing devices.
    // Construction and disposal leave the microphone's mute state untouched.
    public sealed class AudioSession : IDisposable
    {
        private IAudioEndpointVolume volume;
        public string DeviceId { get; private set; }

        public AudioSession(string endpointId)
        {
            if (String.IsNullOrWhiteSpace(endpointId)) throw new ArgumentException(Localization.Text("请选择麦克风。", "Select a microphone."), "endpointId");
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            object activated = null;
            try
            {
                enumerator = CoreAudio.CreateEnumerator();
                CoreAudio.Check(enumerator.GetDevice(endpointId, out device), Localization.Text("找不到所选麦克风，请刷新设备列表", "The selected microphone was not found. Refresh the device list"));
                uint state;
                CoreAudio.Check(device.GetState(out state), Localization.Text("无法读取麦克风状态", "Unable to read the microphone status"));
                if ((state & CoreAudio.DeviceStateActive) == 0)
                    throw new InvalidOperationException(Localization.Text("所选麦克风已断开或被禁用，请重新选择。", "The selected microphone is disconnected or disabled. Select another microphone."));

                // The IMMEndpoint cast shares the device RCW; release that RCW once below.
                EDataFlow flow;
                CoreAudio.Check(((IMMEndpoint)device).GetDataFlow(out flow), Localization.Text("无法确认麦克风类型", "Unable to verify the microphone type"));
                if (flow != EDataFlow.Capture)
                    throw new InvalidOperationException(Localization.Text("所选设备不是麦克风。", "The selected device is not a microphone."));

                Guid iid = typeof(IAudioEndpointVolume).GUID;
                CoreAudio.Check(device.Activate(ref iid, 23, IntPtr.Zero, out activated), Localization.Text("无法控制麦克风，请检查设备或系统权限", "Unable to control the microphone. Check the device and system permissions"));
                volume = (IAudioEndpointVolume)activated;
                activated = null;
                DeviceId = endpointId;
            }
            finally
            {
                CoreAudio.Release(activated);
                CoreAudio.Release(device);
                CoreAudio.Release(enumerator);
            }
        }

        public bool GetMute()
        {
            ThrowIfDisposed();
            bool muted;
            CoreAudio.Check(volume.GetMute(out muted), Localization.Text("无法读取静音状态，麦克风可能已断开", "Unable to read the mute status. The microphone may be disconnected"));
            return muted;
        }

        public void SetMute(bool muted)
        {
            ThrowIfDisposed();
            CoreAudio.Check(volume.SetMute(muted, IntPtr.Zero), Localization.Text("无法设置静音状态，麦克风可能已断开", "Unable to set the mute status. The microphone may be disconnected"));
        }

        private void ThrowIfDisposed()
        {
            if (volume == null) throw new ObjectDisposedException("AudioSession");
        }

        public void Dispose()
        {
            IAudioEndpointVolume previous = volume;
            volume = null;
            CoreAudio.Release(previous);
        }
    }

    internal static class CoreAudio
    {
        internal const uint DeviceStateActive = 1;
        internal const int NotFound = unchecked((int)0x80070490);
        private static readonly PropertyKey FriendlyName = new PropertyKey(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

        internal static IMMDeviceEnumerator CreateEnumerator()
        {
            return (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        }

        internal static void Check(int hr, string context)
        {
            if (hr < 0) throw new COMException(context + " (0x" + hr.ToString("X8") + ")" + Localization.Text("。", "."), hr);
        }

        internal static string ReadId(IMMDevice device)
        {
            IntPtr pointer = IntPtr.Zero;
            try
            {
                Check(device.GetId(out pointer), Localization.Text("无法读取麦克风标识", "Unable to read the microphone ID"));
                string id = Marshal.PtrToStringUni(pointer);
                if (String.IsNullOrEmpty(id)) throw new COMException(Localization.Text("麦克风返回了空的设备标识。", "The microphone returned an empty device ID."));
                return id;
            }
            finally { if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer); }
        }

        internal static string ReadName(IMMDevice device, string fallback)
        {
            IPropertyStore store = null;
            PropVariant value = new PropVariant();
            try
            {
                Check(device.OpenPropertyStore(0, out store), Localization.Text("无法读取麦克风名称", "Unable to read the microphone name"));
                PropertyKey key = FriendlyName;
                Check(store.GetValue(ref key, out value), Localization.Text("无法读取麦克风名称", "Unable to read the microphone name"));
                if (value.VariantType == 31 && value.PointerValue != IntPtr.Zero)
                {
                    string name = Marshal.PtrToStringUni(value.PointerValue);
                    if (!String.IsNullOrWhiteSpace(name)) return name;
                }
                return fallback;
            }
            catch (COMException) { return fallback; }
            finally
            {
                PropVariantClear(ref value);
                Release(store);
            }
        }

        internal static void Release(object instance)
        {
            if (instance != null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
        }

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant value);
    }

    internal enum EDataFlow { Render, Capture, All }
    internal enum ERole { Console, Multimedia, Communications }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey
    {
        internal Guid FormatId;
        internal uint PropertyId;
        internal PropertyKey(Guid formatId, uint propertyId) { FormatId = formatId; PropertyId = propertyId; }
    }

    // A counted pointer makes the union the correct size on both x86 and x64.
    [StructLayout(LayoutKind.Sequential)]
    internal struct PropVariantBlob
    {
        internal uint Count;
        internal IntPtr Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct PropVariant
    {
        [FieldOffset(0)] internal ushort VariantType;
        [FieldOffset(8)] internal IntPtr PointerValue;
        [FieldOffset(8)] internal PropVariantBlob Blob;
        [FieldOffset(8)] internal long LongValue;
    }

    // Interface GUIDs and method order follow Microsoft's Windows SDK headers:
    // https://github.com/microsoft/win32metadata/tree/main/generation/WinSDK/RecompiledIdlHeaders/um
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
        [PreserveSig] int GetId(out IntPtr id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("1BE09788-6894-4089-8586-9A2A6C265AC5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMEndpoint
    {
        [PreserveSig] int GetDataFlow(out EDataFlow flow);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, IntPtr context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, IntPtr context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, IntPtr context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, IntPtr context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, IntPtr context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(IntPtr context);
        [PreserveSig] int VolumeStepDown(IntPtr context);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
        [PreserveSig] int GetVolumeRange(out float minimum, out float maximum, out float increment);
    }
}
