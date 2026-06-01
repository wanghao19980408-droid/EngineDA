using EngineDA.Models;
using System;

namespace EngineDA.Services
{
    public class UdpDataService : IDisposable
    {
        private UDPManager? _manager;
        public bool IsConnected => _manager?.IsConnected ?? false;

        /// <summary>
        /// 原始数据(short[]) 事件
        /// </summary>
        public event EventHandler<short[]>? DataReceived;

        /// <summary>
        /// 结构化数据事件
        /// </summary>
        public event EventHandler<UDPValues>? StructuredDataReceived;

        /// <summary>
        /// 初始化 UDP 服务
        /// </summary>
        /// <param name="localIp">本地IP地址</param>
        /// <param name="multicastIp">组播IP地址</param>
        /// <param name="port">端口号</param>
        /// <param name="extraParams">可选的结构参数数组。如果为 null，则默认为 short[] 模式。</param>
        public void Initialize(string localIp, string multicastIp, int port, int[]? extraParams = null)
        {
            if (extraParams != null)
            {
                _manager = new UDPManager(localIp, multicastIp, port, extraParams);
                _manager.StructuredDataUpdated += (_, data) => StructuredDataReceived?.Invoke(this, data);
            }
            else
            {
                _manager = new UDPManager(localIp, multicastIp, port);
                _manager.DataUpdated += (_, data) => DataReceived?.Invoke(this, data);
            }

            _ = _manager.StartAsync();
        }

        /// <summary>
        /// 停止UDP服务
        /// </summary>
        public void Stop()
        {
            if (_manager != null)
            {
                _manager.Stop();
                _manager = null;
            }
        }

        public void Dispose() => Stop();
    }
}
