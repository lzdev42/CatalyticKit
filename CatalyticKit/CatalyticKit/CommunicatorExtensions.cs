namespace CatalyticKit;

public static class CommunicatorExtensions
{
    extension (ICommunicator communicator)
    {
        /// <summary>
        /// 使用枚举执行动作
        /// </summary>
        public Task<byte[]> ExecuteAsync(int slotIndex, 
            string address,
            CommAction action,
            byte[] payload,
            int timeoutMs,
            CancellationToken ct)
        {
            return communicator.ExecuteAsync(slotIndex, address, action.ToString().ToLowerInvariant(), payload, new ExecuteOptions { TimeoutMs = timeoutMs }, ct);
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        public Task SendAsync(int slotIndex, string address, byte[] data, CancellationToken ct = default)
            => communicator.ExecuteAsync(slotIndex, address, CommAction.Send, data, 0, ct);

        /// <summary>
        /// 读取可用数据
        /// </summary>
        public Task<byte[]> ReadAsync(int slotIndex, string address, int timeoutMs, CancellationToken ct = default)
            => communicator.ExecuteAsync(slotIndex, address, CommAction.Read, [], timeoutMs, ct);

        /// <summary>
        /// 建立连接
        /// </summary>
        public Task ConnectAsync(int slotIndex, string address, int timeoutMs = 5000, CancellationToken ct = default)
            => communicator.ExecuteAsync(slotIndex, address, CommAction.Connect, [], timeoutMs, ct);

        /// <summary>
        /// 断开连接
        /// </summary>
        public Task DisconnectAsync(int slotIndex, string address, CancellationToken ct = default)
            => communicator.ExecuteAsync(slotIndex, address, CommAction.Disconnect, [], 1000, ct);

        /// <summary>
        /// 查询连接状态
        /// </summary>
        public Task<byte[]> GetStatusAsync(int slotIndex, string address, CancellationToken ct = default)
            => communicator.ExecuteAsync(slotIndex, address, CommAction.Status, [], 1000, ct);
    }
}