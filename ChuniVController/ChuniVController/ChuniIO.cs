using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO.MemoryMappedFiles;
using System.Windows.Documents;

namespace ChuniVController
{
    public enum ChuniMessageSources
    {
        Game = 0,
        Controller = 1
    }

    public enum ChuniMessageTypes
    {
        CoinInsert = 0,
        SliderPress = 1,
        SliderRelease = 2,
        LedSet = 3,
        CabinetTest = 4,
        CabinetService = 5,
        IrBlocked = 6,
        IrUnblocked = 7
    }

    public struct ChuniIoMessage
    {
        public byte Source;
        public byte Type;
        public byte Target;
        public byte LedColorRed;
        public byte LedColorGreen;
        public byte LedColorBlue;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct IPCMemoryInfo
    {
        public fixed byte airIoStatus[6];
        public fixed byte sliderIoStatus[32];
        public fixed byte ledRgbData[32 * 3];
        public byte testBtn;
        public byte serviceBtn;
        public byte coinInsertion;
        public byte cardRead;
        public byte remoteCardRead;
        public byte remoteCardType;
        public fixed byte remoteCardId[10];
    }

    public class ChuniIO
    {
        private bool _running;
        private readonly RecvCallback _recvCallback;
        private Thread _recvThread;
        private MemoryMappedFile _ipcFile;
        private MemoryMappedViewAccessor _ipc;

        private struct RecvContext
        {
            public MemoryMappedViewAccessor ipc;
            public RecvCallback callback;
        }

        public delegate void RecvCallback(ChuniIoMessage message);

        public ChuniIO(RecvCallback recvCallback)
        {
            _running = false;
            _recvCallback = recvCallback;
        }

        ~ChuniIO()
        {
            _ipc.Dispose();
            _ipcFile.Dispose();
        }

        private static void RecvThread(object c)
        {
            unsafe
            {
                RecvContext context = (RecvContext) c;
                byte* rgbState = (byte*)Marshal.AllocHGlobal(96).ToPointer();

                try
                {
                    while (true)
                    {
                        IPCMemoryInfo info;
                        context.ipc.Read(0, out info);

                        bool same = true;
                        int target = 0;
                        for (int i = 0; i < 96; i++) 
                        {
                            if (rgbState[i] != info.ledRgbData[i])
                            {
                                target = i / 3;
                                var msg = new ChuniIoMessage();
                                msg.Source = (byte)ChuniMessageSources.Game;
                                msg.Type = (byte)ChuniMessageTypes.LedSet;
                                msg.Target = (byte)(i / 6);
                                msg.LedColorRed = info.ledRgbData[3 * target + 1];
                                msg.LedColorGreen = info.ledRgbData[3 * target + 2];
                                msg.LedColorBlue = info.ledRgbData[3 * target];
                                context.callback(msg);
                            }
                        }

                        Buffer.MemoryCopy(info.ledRgbData, rgbState, 96, 96);
                    }
                }
                catch 
                {
                    // noting, just exit.
                }
            }
        }

        public bool Start()
        {
            if (_running) return false;
            try
            {
                _ipcFile = MemoryMappedFile.OpenExisting("Local\\BROKENITHM_SHARED_BUFFER", MemoryMappedFileRights.ReadWrite);
                _ipc = _ipcFile.CreateViewAccessor();
            }
            catch
            {
                return false;
            }

            RecvContext c = new RecvContext();
            c.ipc = _ipc;
            c.callback = _recvCallback;

            _recvThread = new Thread(RecvThread);
            _recvThread.Start(c);

            _running = true;
            return true;
        }

        public bool Stop()
        {
            if (!_running) return false;
            try
            {
                _ipc.Dispose();
                _ipcFile.Dispose();
            }
            catch 
            {
                return false;
            }
            return true;
        }

        public void Join()
        {
            _recvThread.Join();
        }

        private void SendCallback(IAsyncResult ar)
        {
            // don't care
        }

        public void Send(ChuniIoMessage msg)
        {
            int sliderIndex = 6 + 2 * msg.Target;
            int airIndex = msg.Target;
            if (airIndex % 2 == 0) airIndex++; else airIndex--;

            switch ((ChuniMessageTypes)msg.Type) {
                case ChuniMessageTypes.SliderPress:
                    if (msg.Target > 15) return;

                    _ipc.Write(sliderIndex, (byte)128);
                    _ipc.Write(sliderIndex + 1, (byte)128);
                    break;
                case ChuniMessageTypes.SliderRelease:
                    if (msg.Target > 15) return;

                    _ipc.Write(sliderIndex, (byte)0);
                    _ipc.Write(sliderIndex + 1, (byte)0);
                    break;
                case ChuniMessageTypes.IrBlocked:
                    if (msg.Target > 5) return;

                    _ipc.Write(airIndex, (byte)1);
                    break;
                case ChuniMessageTypes.IrUnblocked:
                    if (msg.Target > 5) return;

                    _ipc.Write(airIndex, (byte)0);
                    break;
            }
        }
    }
}
