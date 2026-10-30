using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Varta.Core;

namespace Varta.Platform
{
    // ---- Windows Core Audio (WASAPI) COM-інтерфейси ----

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorCom { }

    internal enum EDataFlow { Render = 0, Capture = 1, All = 2 }
    internal enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
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
        [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint streamFlags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint streamFlags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl session);
    }

    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid param);
        [PreserveSig] int SetGroupingParam(ref Guid param, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notifications);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notifications);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid param);
        [PreserveSig] int SetGroupingParam(ref Guid param, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notifications);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notifications);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    public sealed class AudioStatus
    {
        public bool DeviceAvailable;
        public int DeviceCount;
        public float MasterVolume;
        public bool Muted;
        public string Error = "";
    }

    /// <summary>
    /// Приглушує гучність усіх інших програм (музика) на час оголошення та повертає її назад.
    /// Початкові рівні зберігаються у файлі відновлення — якщо програму буде закрито аварійно,
    /// наступний запуск поверне музику на місце.
    /// </summary>
    public sealed class AudioDucker
    {
        const uint ClsCtxAll = 23;
        const uint DeviceStateActive = 1;

        readonly object _lock = new object();
        readonly Dictionary<string, float> _byInstance = new Dictionary<string, float>();
        readonly Dictionary<string, float> _byIdent = new Dictionary<string, float>();
        float _savedMaster = -1f;
        bool _savedMute;
        bool _masterTouched;
        static readonly int MyPid = Process.GetCurrentProcess().Id;

        public bool IsDucked { get; private set; }

        static T Activate<T>(IMMDevice dev) where T : class
        {
            Guid iid = typeof(T).GUID;
            object o;
            if (dev.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out o) != 0) return null;
            return o as T;
        }

        static IMMDeviceEnumerator Enumerator()
        {
            return (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        }

        static List<IMMDevice> ActiveRenderDevices(IMMDeviceEnumerator en)
        {
            var list = new List<IMMDevice>();
            IMMDeviceCollection col;
            if (en.EnumAudioEndpoints(EDataFlow.Render, DeviceStateActive, out col) != 0 || col == null) return list;
            uint n;
            if (col.GetCount(out n) != 0) return list;
            for (uint i = 0; i < n; i++)
            {
                IMMDevice d;
                if (col.Item(i, out d) == 0 && d != null) list.Add(d);
            }
            return list;
        }

        delegate void SessionAction(ISimpleAudioVolume vol, string instanceId, string identId, uint pid);

        static void ForEachSession(IMMDevice dev, SessionAction act)
        {
            var mgr = Activate<IAudioSessionManager2>(dev);
            if (mgr == null) return;
            IAudioSessionEnumerator sessions;
            if (mgr.GetSessionEnumerator(out sessions) != 0 || sessions == null) return;
            int count;
            if (sessions.GetCount(out count) != 0) return;
            for (int i = 0; i < count; i++)
            {
                IAudioSessionControl ctl;
                if (sessions.GetSession(i, out ctl) != 0 || ctl == null) continue;
                var c2 = ctl as IAudioSessionControl2;
                var vol = ctl as ISimpleAudioVolume;
                if (c2 == null || vol == null) continue;
                uint pid;
                c2.GetProcessId(out pid);
                string inst, ident;
                c2.GetSessionInstanceIdentifier(out inst);
                c2.GetSessionIdentifier(out ident);
                act(vol, inst ?? "", ident ?? "", pid);
            }
        }

        /// <summary>Знижує гучність інших програм. Безпечно викликати повторно (підхоплює нові сесії).</summary>
        public string Duck(float keepRatio, bool boostMaster, float minMaster, bool unmute)
        {
            lock (_lock)
            {
                try
                {
                    IMMDeviceEnumerator en = Enumerator();
                    bool persist = false;

                    if (boostMaster)
                    {
                        IMMDevice def;
                        if (en.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out def) == 0 && def != null)
                        {
                            var ep = Activate<IAudioEndpointVolume>(def);
                            if (ep != null)
                            {
                                float cur; bool muted;
                                ep.GetMasterVolumeLevelScalar(out cur);
                                ep.GetMute(out muted);
                                if (!_masterTouched) { _savedMaster = cur; _savedMute = muted; _masterTouched = true; persist = true; }
                                Guid g = Guid.Empty;
                                if (muted && unmute) ep.SetMute(false, ref g);
                                if (cur < minMaster) ep.SetMasterVolumeLevelScalar(minMaster, ref g);
                            }
                        }
                    }

                    foreach (IMMDevice dev in ActiveRenderDevices(en))
                    {
                        ForEachSession(dev, delegate (ISimpleAudioVolume vol, string inst, string ident, uint pid)
                        {
                            if (pid == (uint)MyPid || pid == 0) return;
                            float cur;
                            if (vol.GetMasterVolume(out cur) != 0) return;
                            float orig;
                            if (!_byInstance.TryGetValue(inst, out orig))
                            {
                                orig = cur;
                                _byInstance[inst] = orig;
                                if (ident.Length > 0 && !_byIdent.ContainsKey(ident)) _byIdent[ident] = orig;
                                persist = true;
                            }
                            float target = Math.Max(0f, Math.Min(1f, orig * keepRatio));
                            Guid g2 = Guid.Empty;
                            vol.SetMasterVolume(target, ref g2);
                        });
                    }

                    IsDucked = true;
                    if (persist) SaveRecovery();
                    return null;
                }
                catch (Exception ex)
                {
                    return ex.Message;
                }
            }
        }

        public void Restore()
        {
            lock (_lock)
            {
                try { RestoreCore(_byInstance, _byIdent, _masterTouched, _savedMaster, _savedMute); }
                catch (Exception ex) { Log.Warn("Не вдалося повернути гучність: " + ex.Message); }
                finally
                {
                    _byInstance.Clear();
                    _byIdent.Clear();
                    _masterTouched = false;
                    _savedMaster = -1f;
                    IsDucked = false;
                    DeleteRecovery();
                }
            }
        }

        static void RestoreCore(Dictionary<string, float> byInstance, Dictionary<string, float> byIdent, bool master, float masterVol, bool masterMute)
        {
            IMMDeviceEnumerator en = Enumerator();
            foreach (IMMDevice dev in ActiveRenderDevices(en))
            {
                ForEachSession(dev, delegate (ISimpleAudioVolume vol, string inst, string ident, uint pid)
                {
                    float orig;
                    if (byInstance.TryGetValue(inst, out orig) || (ident.Length > 0 && byIdent.TryGetValue(ident, out orig)))
                    {
                        Guid g = Guid.Empty;
                        vol.SetMasterVolume(orig, ref g);
                    }
                });
            }
            if (master)
            {
                IMMDevice def;
                if (en.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out def) == 0 && def != null)
                {
                    var ep = Activate<IAudioEndpointVolume>(def);
                    if (ep != null)
                    {
                        Guid g = Guid.Empty;
                        if (masterVol >= 0f) ep.SetMasterVolumeLevelScalar(masterVol, ref g);
                        ep.SetMute(masterMute, ref g);
                    }
                }
            }
        }

        /// <summary>Аварійне відновлення після попереднього збою (викликається при старті програми).</summary>
        public void RecoverIfNeeded()
        {
            try
            {
                if (!File.Exists(Paths.RecoveryFile)) return;
                var root = MiniJson.Obj(MiniJson.Parse(File.ReadAllText(Paths.RecoveryFile)));
                var inst = new Dictionary<string, float>();
                var ident = new Dictionary<string, float>();
                Fill(MiniJson.Obj(MiniJson.Get(root, "instances")), inst);
                Fill(MiniJson.Obj(MiniJson.Get(root, "idents")), ident);
                bool master = MiniJson.Get(root, "masterTouched") is bool && (bool)MiniJson.Get(root, "masterTouched");
                float mv = ToFloat(MiniJson.Get(root, "master"), -1f);
                bool mm = MiniJson.Get(root, "mute") is bool && (bool)MiniJson.Get(root, "mute");
                RestoreCore(inst, ident, master, mv, mm);
                Log.Warn("Виявлено незавершене приглушення музики після попереднього збою — гучність повернуто.");
            }
            catch (Exception ex) { Log.Warn("Не вдалося виконати аварійне відновлення гучності: " + ex.Message); }
            finally { DeleteRecovery(); }
        }

        static void Fill(Dictionary<string, object> src, Dictionary<string, float> dst)
        {
            if (src == null) return;
            foreach (var kv in src) dst[kv.Key] = ToFloat(kv.Value, 1f);
        }

        static float ToFloat(object o, float def)
        {
            try { return o == null ? def : Convert.ToSingle(o, System.Globalization.CultureInfo.InvariantCulture); }
            catch { return def; }
        }

        void SaveRecovery()
        {
            try
            {
                var d = new Dictionary<string, object>();
                var i = new Dictionary<string, object>();
                var n = new Dictionary<string, object>();
                foreach (var kv in _byInstance) i[kv.Key] = kv.Value;
                foreach (var kv in _byIdent) n[kv.Key] = kv.Value;
                d["instances"] = i;
                d["idents"] = n;
                d["masterTouched"] = _masterTouched;
                d["master"] = _savedMaster;
                d["mute"] = _savedMute;
                File.WriteAllText(Paths.RecoveryFile, MiniJson.Serialize(d, false));
            }
            catch { }
        }

        static void DeleteRecovery()
        {
            try { if (File.Exists(Paths.RecoveryFile)) File.Delete(Paths.RecoveryFile); } catch { }
        }

        /// <summary>Стан пристрою виводу для вкладки «Стан».</summary>
        public static AudioStatus Query()
        {
            var st = new AudioStatus();
            try
            {
                IMMDeviceEnumerator en = Enumerator();
                st.DeviceCount = ActiveRenderDevices(en).Count;
                IMMDevice def;
                if (en.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out def) == 0 && def != null)
                {
                    st.DeviceAvailable = true;
                    var ep = Activate<IAudioEndpointVolume>(def);
                    if (ep != null)
                    {
                        float v; bool m;
                        if (ep.GetMasterVolumeLevelScalar(out v) == 0) st.MasterVolume = v;
                        if (ep.GetMute(out m) == 0) st.Muted = m;
                    }
                }
            }
            catch (Exception ex) { st.Error = ex.Message; }
            return st;
        }
    }
}
