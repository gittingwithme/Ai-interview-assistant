using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Speech.Recognition;
using System.Threading;
using System.Threading.Tasks;

namespace GhostAI {
    // ══════════════════════════════════════════════════════════════════
    //  WASAPI COM Interfaces for capturing Computer Speakers (Loopback)
    // ══════════════════════════════════════════════════════════════════

    public enum EDataFlow {
        eRender = 0,
        eCapture = 1,
        eAll = 2
    }

    public enum ERole {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2
    }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator {
        int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IntPtr ppDevices);
        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
        int GetDevice(string pwstrId, out IMMDevice ppDevice);
        int RegisterEndpointNotificationCallback(IntPtr pClient);
        int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice {
        [PreserveSig]
        int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        int OpenPropertyStore(uint stgmAccess, out IntPtr ppProperties);
        int GetId(out IntPtr ppstrId);
        int GetState(out uint pdwState);
    }

    [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioClient {
        [PreserveSig]
        int Initialize(int shareMode, uint streamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr pFormat, ref Guid audioSessionGuid);
        [PreserveSig]
        int GetBufferSize(out uint pNumBufferFrames);
        [PreserveSig]
        int GetStreamLatency(out long phnsLatency);
        [PreserveSig]
        int GetCurrentPadding(out uint pNumPaddingFrames);
        [PreserveSig]
        int IsFormatSupported(int shareMode, IntPtr pFormat, out IntPtr ppClosestMatch);
        [PreserveSig]
        int GetMixFormat(out IntPtr ppDeviceFormat);
        [PreserveSig]
        int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
        [PreserveSig]
        int Start();
        [PreserveSig]
        int Stop();
        [PreserveSig]
        int Reset();
        [PreserveSig]
        int SetEventHandle(IntPtr eventHandle);
        [PreserveSig]
        int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    }

    [Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioCaptureClient {
        [PreserveSig]
        int GetBuffer(out IntPtr ppData, out uint pNumFramesToRead, out uint pdwFlags, out ulong pu64DevicePosition, out ulong pu64QPCPosition);
        [PreserveSig]
        int ReleaseBuffer(uint numFramesRead);
        [PreserveSig]
        int GetNextPacketSize(out uint pNumFramesInNextPacket);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WAVEFORMATEX {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WAVEFORMATEXTENSIBLE {
        public WAVEFORMATEX Format;
        public ushort wValidBitsPerSample;
        public uint dwChannelMask;
        public Guid SubFormat;
    }

    public class AudioLoopbackService {
        private static readonly Guid CLSID_MMDeviceEnumerator = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
        private static readonly Guid IID_IMMDeviceEnumerator = new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6");
        private static readonly Guid IID_IAudioClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
        private static readonly Guid IID_IAudioCaptureClient = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");

        private const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
        private const int AUDCLNT_SHAREMODE_SHARED = 0;

        private Thread _captureThread;
        private volatile bool _isCapturing;
        private MemoryStream _recordedPcm;
        private WAVEFORMATEXTENSIBLE _waveFormat;
        private bool _hasFormat;

        private SpeechRecognitionEngine _offlineRecognizer;
        public event Action<string> OnSpeechRecognized;
        public event Action<string> OnStatusChanged;

        public bool IsCapturing {
            get { return _isCapturing; }
        }

        public AudioLoopbackService() {
            InitOfflineSpeech();
        }

        private void InitOfflineSpeech() {
            try {
                // Initialize Windows offline speech recognizer
                _offlineRecognizer = new SpeechRecognitionEngine();
                _offlineRecognizer.SetInputToDefaultAudioDevice();
                _offlineRecognizer.LoadGrammar(new DictationGrammar());
                _offlineRecognizer.SpeechRecognized += (s, e) => {
                    if (e.Result != null && !string.IsNullOrWhiteSpace(e.Result.Text) && e.Result.Confidence > 0.3) {
                        if (OnSpeechRecognized != null) {
                            OnSpeechRecognized(e.Result.Text);
                        }
                    }
                };
            } catch {
                _offlineRecognizer = null;
            }
        }

        public void StartCapturing() {
            if (_isCapturing) return;

            _isCapturing = true;
            _recordedPcm = new MemoryStream();

            if (OnStatusChanged != null) OnStatusChanged("Listening to audio...");

            // Start offline speech recognition as well (listens to mic)
            try {
                if (_offlineRecognizer != null) {
                    _offlineRecognizer.RecognizeAsync(RecognizeMode.Multiple);
                }
            } catch { }

            // Start WASAPI loopback capture thread (captures what comes out of computer speakers)
            _captureThread = new Thread(CaptureLoopbackWorker) {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _captureThread.Start();
        }

        public byte[] StopCapturing() {
            _isCapturing = false;

            try {
                if (_offlineRecognizer != null) {
                    _offlineRecognizer.RecognizeAsyncStop();
                }
            } catch { }

            if (_captureThread != null) {
                _captureThread.Join(500);
                _captureThread = null;
            }

            if (OnStatusChanged != null) OnStatusChanged("Processing audio...");

            if (_recordedPcm != null && _recordedPcm.Length > 0 && _hasFormat) {
                return BuildWavFile(_recordedPcm.ToArray(), _waveFormat);
            }

            return null;
        }

        private void CaptureLoopbackWorker() {
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            IAudioClient audioClient = null;
            IAudioCaptureClient captureClient = null;
            IntPtr pFormat = IntPtr.Zero;

            try {
                Type type = Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator);
                enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(type);

                // Default playback device (speakers/headphones)
                int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device);
                if (hr != 0 || device == null) return;

                Guid iidClient = IID_IAudioClient;
                object clientObj;
                hr = device.Activate(ref iidClient, 1, IntPtr.Zero, out clientObj);
                if (hr != 0 || clientObj == null) return;

                audioClient = (IAudioClient)clientObj;
                hr = audioClient.GetMixFormat(out pFormat);
                if (hr != 0 || pFormat == IntPtr.Zero) return;

                _waveFormat = (WAVEFORMATEXTENSIBLE)Marshal.PtrToStructure(pFormat, typeof(WAVEFORMATEXTENSIBLE));
                _hasFormat = true;

                // 200ms buffer
                long hnsBufferDuration = 2000000;
                Guid sessionGuid = Guid.Empty;

                hr = audioClient.Initialize(AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_LOOPBACK, hnsBufferDuration, 0, pFormat, ref sessionGuid);
                if (hr != 0) return;

                Guid iidCapture = IID_IAudioCaptureClient;
                object captureObj;
                hr = audioClient.GetService(ref iidCapture, out captureObj);
                if (hr != 0 || captureObj == null) return;

                captureClient = (IAudioCaptureClient)captureObj;
                audioClient.Start();

                while (_isCapturing) {
                    uint packetSize;
                    hr = captureClient.GetNextPacketSize(out packetSize);
                    if (hr != 0) break;

                    while (packetSize > 0) {
                        IntPtr pData;
                        uint numFramesRead;
                        uint flags;
                        ulong u64DevicePosition;
                        ulong u64QPCPosition;

                        hr = captureClient.GetBuffer(out pData, out numFramesRead, out flags, out u64DevicePosition, out u64QPCPosition);
                        if (hr != 0) break;

                        if (numFramesRead > 0 && pData != IntPtr.Zero) {
                            int bytesCount = (int)(numFramesRead * _waveFormat.Format.nBlockAlign);
                            byte[] buffer = new byte[bytesCount];

                            if ((flags & 0x01) != 0) { // AUDCLNT_BUFFERFLAGS_SILENT
                                Array.Clear(buffer, 0, buffer.Length);
                            } else {
                                Marshal.Copy(pData, buffer, 0, bytesCount);
                            }

                            lock (_recordedPcm) {
                                _recordedPcm.Write(buffer, 0, buffer.Length);
                            }
                        }

                        captureClient.ReleaseBuffer(numFramesRead);
                        hr = captureClient.GetNextPacketSize(out packetSize);
                        if (hr != 0) break;
                    }

                    Thread.Sleep(20);
                }

                audioClient.Stop();
            } catch { }
            finally {
                if (pFormat != IntPtr.Zero) Marshal.FreeCoTaskMem(pFormat);
                if (captureClient != null) Marshal.ReleaseComObject(captureClient);
                if (audioClient != null) Marshal.ReleaseComObject(audioClient);
                if (device != null) Marshal.ReleaseComObject(device);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
        }

        private byte[] BuildWavFile(byte[] pcmData, WAVEFORMATEXTENSIBLE format) {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms)) {
                // RIFF header
                bw.Write(new char[] { 'R', 'I', 'F', 'F' });
                bw.Write((uint)(36 + pcmData.Length));
                bw.Write(new char[] { 'W', 'A', 'V', 'E' });

                // fmt chunk
                bw.Write(new char[] { 'f', 'm', 't', ' ' });
                bw.Write((uint)16); // Subchunk1Size for PCM
                bw.Write((ushort)format.Format.wFormatTag);
                bw.Write((ushort)format.Format.nChannels);
                bw.Write((uint)format.Format.nSamplesPerSec);
                bw.Write((uint)format.Format.nAvgBytesPerSec);
                bw.Write((ushort)format.Format.nBlockAlign);
                bw.Write((ushort)format.Format.wBitsPerSample);

                // data chunk
                bw.Write(new char[] { 'd', 'a', 't', 'a' });
                bw.Write((uint)pcmData.Length);
                bw.Write(pcmData);

                return ms.ToArray();
            }
        }
    }
}
