using System.Net.Sockets;
using System.Threading;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using NLog;
using VSMonoDebugger.Services;

namespace VSMonoDebugger
{
    public class AttachCommandNotify
    {
        protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static AttachCommandNotify s_Instance;
        public static AttachCommandNotify Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    s_Instance = new AttachCommandNotify();
                    s_Instance.Init();
                }
                return s_Instance;
            }
        }

        public static void DestroyInstance()
        {
            if(s_Instance != null)
            {
                s_Instance.Destroy();
                s_Instance = null;

            }
        }
        
        public int NotifyPort { get { return 9001; } }
        public string NotifyServer { get { return "127.0.0.1"; } }
        public int ReconnectMaxCount = 3;
        public int ReconnectCurrentCount = 0;
        public const int DataHeaderSize = 4;
        public int CurrentDataOffset = 0;
        public int CurrentDataLength = 0;
        public TcpClient Client { get; private set; }
        // public List<byte[]> WaittingToSendDatas = new List<byte[]>();
        public LinkedList<byte[]> WaittingToSendDatas = new LinkedList<byte[]>();
        public readonly object LockObject = new object();
        public bool bInited { get; private set; } = false;
        public bool bIsReceiving = false;
        public bool bStop = false;
        public bool IsConnected
        {
            get { if (Client == null) return false; return Client.Connected; }
        }
        

        private void Init()
        {
            if (this.bInited)
                return;
            this.bInited = true;
            Thread Worker = new Thread(this.Work);
            Worker.Start();
        }


        private void Connect()
        {
            NLogService.TraceEnteringMethod(Logger);
            if (this.IsConnected)
                return;
            if (this.ReconnectCurrentCount >= ReconnectMaxCount)
            {
                Logger.Error("Reconnect max count");
                return;
            }
            try
            {
                this.Client = new TcpClient();
                this.Client.NoDelay = true;
                this.Client.ReceiveTimeout = 10000;
                this.Client.SendTimeout = 10000;
                this.Client.Connect(this.NotifyServer, this.NotifyPort);
                ReconnectCurrentCount = 0;
            }
            catch (Exception e)
            {
                NLogService.LogError(Logger, e);
                this.Clear();
                ReconnectCurrentCount += 1;
            }
        }

        public void Send(byte[] Data)
        {
            NLogService.TraceEnteringMethod(Logger);
            this.ReconnectCurrentCount = 0;
            lock (LockObject)
            {
                WaittingToSendDatas.AddLast(Data);
            }
        }
        private void Send()
        {
            NLogService.TraceEnteringMethod(Logger);
            if (!this.IsConnected)
                return;
            lock (LockObject)
            {
                try
                {
                    while (WaittingToSendDatas.Count > 0)
                    {
                        byte[] Data = WaittingToSendDatas.First.Value;
                        NetworkStream Stream = this.Client.GetStream();
                        byte[] LengthBytes = BitConverter.GetBytes(Data.Length);
                        if (!BitConverter.IsLittleEndian)
                        {
                            Array.Reverse(LengthBytes);
                            Array.Reverse(Data);
                        }
                        Stream.Write(LengthBytes, 0, LengthBytes.Length);
                        Stream.Write(Data, 0, Data.Length);
                        WaittingToSendDatas.RemoveFirst();
                    }
                }
                catch (Exception e)
                {
                    NLogService.LogError(Logger, e);
                    Clear();
                }
            }
        }

        private void Receive()
        {
            NLogService.TraceEnteringMethod(Logger);
            try
            {
                if (!this.IsConnected)
                    return;
                if (bIsReceiving)
                    return;
                byte[] Data = null;

                int DataCount = this.CurrentDataLength;
                int Offset = this.CurrentDataOffset;
                if (DataCount == 0)
                {
                    DataCount = DataHeaderSize;
                }
                int Length = DataCount - this.CurrentDataOffset;
               
                this.Client.GetStream().BeginRead(Data, Offset, Length, this.OnReceive, null);
            }catch(Exception e)
            {
                NLogService.LogError(Logger, e);
                Clear();
            }
        }

        private void OnReceive(IAsyncResult ar)
        {
            NLogService.TraceEnteringMethod(Logger);
            this.bIsReceiving = false;

        }
        
        private void Clear()
        {
            NLogService.TraceEnteringMethod(Logger);
            this.bIsReceiving = false;
            this.WaittingToSendDatas.Clear();
            if (this.Client != null)
            {
                this.Client.Close();
                this.Client = null;
            }
        }

        private void Destroy()
        {
            bStop = true;
            Clear();
        }

        private void Work()
        {
            while(true)
            {
                if (bStop)
                    break;
                Connect();
                Send();
                Receive();
            }
        }

        public void NotifyToEngineAttach(bool bStartPlay)
        {
            NLogService.TraceEnteringMethod(Logger);
            string Message = string.Format("cmd:Attach;value:{0}", bStartPlay);
            byte[] Data = System.Text.UnicodeEncoding.Unicode.GetBytes(Message);
            Send(Data);
        }

        public void NotifyToEngineDetach(bool bStopPlay)
        {
            NLogService.TraceEnteringMethod(Logger);
            string Message = string.Format("cmd:Detach;value:{0}", bStopPlay);
            byte[] Data = System.Text.UnicodeEncoding.Unicode.GetBytes(Message);
            Send(Data);
        }

    
    }
}
