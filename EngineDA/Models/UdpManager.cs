using DataConvertLib;
using EngineDA.Models;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace EngineDA.Services
{
    public class UDPManager
    {
        private UdpEx? _udpClient;
        private readonly ConcurrentQueue<byte[]> _dataQueue = new();
        private readonly ManualResetEventSlim _dataAvailable = new(false);
        private readonly object _queueLock = new();

        private readonly int _processIntervalMs;
        private readonly int _maxQueueSize;
        private readonly int _maxBatchSize;
        private readonly int _maxReceivePerSecond;

        private CancellationTokenSource _cts = new();
        private bool _isDecodeTaskRunning = false;

        public bool IsConnected { get; private set; }
        public IPAddress LocalIP { get; private set; }
        public IPAddress MulticastAddress { get; private set; }
        public int MulticastPort { get; private set; }

        public event EventHandler<short[]>? DataUpdated;

        public event EventHandler<UDPValues>? StructuredDataUpdated;

        private readonly int[]? _structCount;
        private readonly UDPValues? _udpValues;

        private DateTime _lastReceiveTime = DateTime.MinValue;
        private int _receiveCountInSecond = 0;

        /// <summary>
        /// 用于普通 short[] 解析的构造器
        /// </summary>
        public UDPManager(string local, string multicast, int port,
                int processIntervalMs = 100, int maxQueueSize = 100,
                int maxBatchSize = 20, int maxReceivePerSecond = 50)
        {
            LocalIP = IPAddress.Parse(local);
            MulticastAddress = IPAddress.Parse(multicast);
            MulticastPort = port;

            _processIntervalMs = processIntervalMs;
            _maxQueueSize = maxQueueSize;
            _maxBatchSize = maxBatchSize;
            _maxReceivePerSecond = maxReceivePerSecond;
        }

        /// <summary>
        /// 用于结构化（按 Struct_count 切分 DI/DQ/AQ/AI）解析的构造器
        /// </summary>
        public UDPManager(string local, string multicast, int port, int[] structCount,
            int processIntervalMs = 150, int maxQueueSize = 1000,
            int maxBatchSize = 20, int maxReceivePerSecond = 50)
            : this(local, multicast, port, processIntervalMs, maxQueueSize, maxBatchSize, maxReceivePerSecond)
        {
            _structCount = structCount ?? throw new ArgumentNullException(nameof(structCount));
            if (_structCount.Length != 7) throw new ArgumentException("structCount 必须包含 7 个元素");
            _udpValues = new UDPValues
            {
                DI_value = new bool[_structCount[1] * 8],
                AQ_value = new short[_structCount[3] / 2],
                PLC_status = new bool[_structCount[4] * 8],
                AI_value = new short[_structCount[5] / 2],
                HIGH_AI_value = new short[_structCount[6] / 2]
            };
        }

        public async Task StartAsync()
        {
            _cts = new CancellationTokenSource();
            await Task.Run(Connect, _cts.Token);
            _ = Task.Run(ProcessQueueLoop, _cts.Token);
        }

        private bool Connect()
        {
            try
            {
                _udpClient = new UdpEx(LocalIP, MulticastPort);
                _udpClient.JoinMulticastGroup(MulticastAddress, MulticastPort);
                _udpClient.DataArrived += OnDataArrived;
                IsConnected = true;
                return true;
            }
            catch
            {
                IsConnected = false;
                return false;
            }
        }

        private void OnDataArrived(object? sender, UdpTransmissionEventArgs e)
        {
            var now = DateTime.Now;
            if (now - _lastReceiveTime > TimeSpan.FromSeconds(1))
            {
                _lastReceiveTime = now;
                _receiveCountInSecond = 0;
            }
            if (_receiveCountInSecond >= _maxReceivePerSecond) return;
            _receiveCountInSecond++;

            lock (_queueLock)
            {
                if (_dataQueue.Count >= _maxQueueSize)
                    _dataQueue.TryDequeue(out _);

                _dataQueue.Enqueue(e.Data);
                _dataAvailable.Set();
            }
        }

        private async Task ProcessQueueLoop()
        {
            if (_isDecodeTaskRunning) return;
            _isDecodeTaskRunning = true;

            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    if (_dataAvailable.Wait(_processIntervalMs, _cts.Token))
                    {
                        int processedCount = 0;
                        while (_dataQueue.TryDequeue(out var bytes) && processedCount < _maxBatchSize)
                        {
                            if (_structCount != null)
                                ParseStructuredData(bytes);
                            else
                                ParseRawData(bytes);

                            processedCount++;
                        }

                        _dataAvailable.Reset();
                    }

                    await Task.Delay(_processIntervalMs, _cts.Token);
                }
            }
            catch (TaskCanceledException) { }
            catch (ObjectDisposedException) { }
            finally
            {
                _isDecodeTaskRunning = false;
            }
        }

        private void ParseRawData(byte[] bytes)
        {
            if (bytes.Length <= 2) return;
            byte[] ai = new byte[bytes.Length - 2];
            Array.Copy(bytes, 2, ai, 0, ai.Length);
            var values = ShortLib.GetShortArrayFromByteArray(ai);
            DataUpdated?.Invoke(this, values);
        }

        private void ParseStructuredData(byte[] bytes)
        {
            if (_udpValues == null || _structCount == null) return;

            int expectedLen = 2 + _structCount[1] + _structCount[2] + _structCount[3] + _structCount[5] + _structCount[6];
            if (bytes.Length < expectedLen)
            {
            }

            byte[] DI = new byte[_structCount[1]];
            byte[] AQ = new byte[_structCount[3]];
            byte[] DQ = new byte[_structCount[2]];
            byte[] AI = new byte[_structCount[5]];
            byte[] HIGH_AI = new byte[_structCount[6]];

            if (MulticastPort == 8061 || MulticastPort == 8060)
            {
                if (bytes.Length >= 2 + _structCount[6])
                {
                    Array.Copy(bytes, 2, HIGH_AI, 0, _structCount[6]);
                    _udpValues.HIGH_AI_value = ShortLib.GetShortArrayFromByteArray(HIGH_AI);
                }
            }
            else
            {
                if (bytes.Length >= 2 + _structCount[1])
                    Array.Copy(bytes, 2, DI, 0, Math.Min(_structCount[1], bytes.Length - 2));

                if (bytes.Length >= 2 + _structCount[1] + _structCount[2])
                    Array.Copy(bytes, 2 + _structCount[1], DQ, 0, Math.Min(_structCount[2], bytes.Length - (2 + _structCount[1])));

                if (bytes.Length >= 2 + _structCount[1] + _structCount[2] + _structCount[3])
                    Array.Copy(bytes, 2 + _structCount[1] + _structCount[2], AQ, 0, Math.Min(_structCount[3], bytes.Length - (2 + _structCount[1] + _structCount[2])));

                if (bytes.Length >= 2 + _structCount[1] + _structCount[2] + _structCount[3] + _structCount[5])
                    Array.Copy(bytes, 2 + _structCount[1] + _structCount[2] + _structCount[3], AI, 0, Math.Min(_structCount[5], bytes.Length - (2 + _structCount[1] + _structCount[2] + _structCount[3])));

                try
                {
                    _udpValues.DI_value = BitLib.GetBitArrayFromByteArray(DI);
                    _udpValues.AI_value = ShortLib.GetShortArrayFromByteArray(AI);
                    _udpValues.AQ_value = ShortLib.GetShortArrayFromByteArray(AQ);
                    _udpValues.DQ_value = BitLib.GetBitArrayFromByteArray(DQ);
                }
                catch
                {
                }
            }

            StructuredDataUpdated?.Invoke(this, _udpValues);
        }

        public void Stop()
        {
            try
            {
                _cts.Cancel();
                _udpClient?.Close();
                _udpClient = null;
                _dataAvailable.Dispose();
                IsConnected = false;
            }
            catch { }
        }
    }
}
