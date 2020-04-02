using System.Net.Sockets;
using System.Threading;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using NLog;
using VSMonoDebugger.Services;
using Mono.Debugging.VisualStudio;
using System.Net;

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
        
        public int NotifyPort { get { return XamarinEngine.DebugOptions.GetEngineNotifyPort(); } }
        public IPAddress NotifyServer { get { return XamarinEngine.DebugOptions.GetHostIP() ; } }
        public int ReconnectMaxCount = 4;
        public int ReconnectCurrentCount = 0;
        public const int DataHeaderSize = 4;
        public int CurrentDataLength = 0;
        public TcpClient Client { get; private set; }
        // public List<byte[]> WaittingToSendDatas = new List<byte[]>();
        public LinkedList<byte[]> WaittingToSendDatas = new LinkedList<byte[]>();
        public readonly object LockObject = new object();
        public bool bInited { get; private set; } = false;
        public bool bStop = false;
        // 如果没有发送数据了，停止Worker
        public bool bStopWorkerWhenNoSend = true;
        public bool IsConnected
        {
            get { if (Client == null) return false; return Client.Connected; }
        }
        

        private void Init()
        {
            if (this.bInited)
                return;
            this.bInited = true;
            System.Threading.Thread Worker = new System.Threading.Thread(this.Work);
            Worker.Start();
        }


        private void Connect()
        {
            // NLogService.TraceEnteringMethod(Logger);
            if (this.IsConnected)
                return;
            if (this.ReconnectCurrentCount >= ReconnectMaxCount)
            {
                HostOutputWindowEx.WriteLineLaunchErrorAsync("Reconnect max count");
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
                HostOutputWindowEx.WriteLineLaunchErrorAsync(e.ToString());
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
            // NLogService.TraceEnteringMethod(Logger);
            if (!this.IsConnected)
                return;
            lock (LockObject)
            {
                try
                {
                    while (WaittingToSendDatas.Count > 0)
                    {
                        byte[] Content = WaittingToSendDatas.First.Value;
                        NetworkStream Stream = this.Client.GetStream();
                        byte[] LengthBytes = BitConverter.GetBytes(Content.Length);
                        if (!BitConverter.IsLittleEndian)
                        {
                            Array.Reverse(LengthBytes);
                            Array.Reverse(Content);
                        }
                        byte[] Data = new byte[Content.Length + LengthBytes.Length];
                        Array.Copy(LengthBytes, 0, Data, 0, LengthBytes.Length);
                        Array.Copy(Content, 0, Data, LengthBytes.Length, Content.Length);
                        Stream.Write(Data, 0, Data.Length);
                        Stream.Flush();
                        WaittingToSendDatas.RemoveFirst();
                    }
                }
                catch (Exception e)
                {
                    HostOutputWindowEx.WriteLineLaunchErrorAsync(e.ToString());
                    NLogService.LogError(Logger, e);
                    Clear();
                }
            }
        }

        private void Receive()
        {
            // NLogService.TraceEnteringMethod(Logger);
            try
            {
                if (!this.IsConnected)
                    return;
                int DataCount = this.CurrentDataLength;
                if (DataCount == 0)
                {
                    DataCount = DataHeaderSize;
                }

                byte[] Data = new byte[DataCount];
                if (DataCount > 0 && this.Client.Available >= DataCount)
                {
                    this.Client.GetStream().Read(Data, 0, DataCount);
                    if(!BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(Data);
                    }
                    if(this.CurrentDataLength == 0)
                    {
                        this.CurrentDataLength = BitConverter.ToInt32(Data, 0);
                    }
                    else
                    {
                        this.CurrentDataLength = 0;
                        string DataText = System.Text.UTF8Encoding.UTF8.GetString(Data);
                        Logger.Debug(string.Format("Receive: {0}", DataText));
                        // todo 回调
                    }
                }
            }
            catch (Exception e)
            {
                HostOutputWindowEx.WriteLineLaunchErrorAsync(e.ToString());
                NLogService.LogError(Logger, e);
                Clear();
            }
        }
        
        private void Clear()
        {
            NLogService.TraceEnteringMethod(Logger);
            // this.WaittingToSendDatas.Clear();
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
