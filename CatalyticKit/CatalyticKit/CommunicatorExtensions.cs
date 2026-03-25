namespace CatalyticKit;

public static class CommunicatorExtensions
{
    extension (ICommunicator communicator)
    {
        /// <summary>
        /// 使用枚举执行动作 (不返回结果，结果通过 PushEvent 上报)
        /// </summary>
        public Task ExecuteTask(int slotIndex, 
            string address,
            CommAction action,
            string payload,
            int timeoutMs,
            CancellationToken ct)
        {
            return communicator.ExecuteTask(slotIndex, address, action, payload, new ExecuteOptions { TimeoutMs = timeoutMs }, ct);
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        public Task SendAsync(int slotIndex, string address, string data, CancellationToken ct = default)
            => communicator.ExecuteTask(slotIndex, address, CommAction.Send, data, new ExecuteOptions { TimeoutMs = 5000 }, ct);

        /// <summary>
        /// 读取数据 (不返回结果，结果通过 PushEvent 上报)
        /// </summary>
        public Task ReadAsync(int slotIndex, string address, int timeoutMs, CancellationToken ct = default)
            => communicator.ExecuteTask(slotIndex, address, CommAction.Read, "", new ExecuteOptions { TimeoutMs = timeoutMs }, ct);

        /// <summary>
        /// 建立连接
        /// </summary>
        public Task ConnectAsync(int slotIndex, string address, int timeoutMs = 5000, CancellationToken ct = default)
            => communicator.ExecuteTask(slotIndex, address, CommAction.Connect, "", new ExecuteOptions { TimeoutMs = timeoutMs }, ct);

        /// <summary>
        /// 断开连接
        /// </summary>
        public Task DisconnectAsync(int slotIndex, string address, CancellationToken ct = default)
            => communicator.ExecuteTask(slotIndex, address, CommAction.Disconnect, "", new ExecuteOptions { TimeoutMs = 1000 }, ct);

        /// <summary>
        /// 查询连接状态 (不返回结果，结果通过 PushEvent 上报)
        /// </summary>
        public Task GetStatusAsync(int slotIndex, string address, CancellationToken ct = default)
            => communicator.ExecuteTask(slotIndex, address, CommAction.Status, "", new ExecuteOptions { TimeoutMs = 1000 }, ct);
    }
}