using UnityEngine;
using System;
using System.Threading.Tasks;
using System.Net.WebSockets;
using System.Threading;
using System.Text;
using System.Collections;

[System.Serializable]
public class ActionEvent
{
    public string motion_type; // 修改为匹配 Python 端的字段名
    public float timestamp;
}

public class BLEReceiver : MonoBehaviour
{
    [Header("WebSocket设置")]
    public string serverUrl = "ws://localhost:8765";
    public bool autoConnect = true;
    public float reconnectInterval = 3f;

    [Header("调试")]
    public bool debugLog = true;
    public bool showConnectionStatus = true;

    // 事件定义
    public event Action OnStompDetected;
    public event Action OnKickDetected;

    // WebSocket相关
    private ClientWebSocket webSocket;
    private CancellationTokenSource cts;
    private bool isConnected = false;

    void Start()
    {
        if (autoConnect) ConnectToServer();
    }

    void OnDestroy()
    {
        Disconnect();
    }

    // 简化后的连接方法
    public async void ConnectToServer()
    {
        if (isConnected) return;
        
        DebugLog("尝试连接到服务器...");
        
        // 清理旧连接（如果有）
        Disconnect();
        
        webSocket = new ClientWebSocket();
        cts = new CancellationTokenSource();
        
        try
        {
            await webSocket.ConnectAsync(new Uri(serverUrl), cts.Token);
            isConnected = true;
            DebugLog("连接成功！");
            
            // 开始接收消息
            ReceiveMessages();
        }
        catch (Exception e)
        {
            DebugLog($"连接失败: {e.Message}");
            isConnected = false;
            
            // 安排重新连接
            StartCoroutine(ReconnectAfterDelay());
        }
    }
    
    // 新的消息接收方法
    private async void ReceiveMessages()
    {
        var buffer = new byte[4096];
        
        while (webSocket != null && webSocket.State == WebSocketState.Open && !cts.IsCancellationRequested)
        {
            try
            {
                var result = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), cts.Token);
                
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    DebugLog("服务器关闭了连接");
                    isConnected = false;
                    
                    // 重新连接
                    StartCoroutine(ReconnectAfterDelay());
                    break;
                }
                
                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                DebugLog($"收到消息: {message}");
                
                // 处理消息
                ProcessWebSocketMessage(message);
            }
            catch (Exception e)
            {
                DebugLog($"接收消息时出错: {e.Message}");
                isConnected = false;
                
                // 连接断开，尝试重新连接
                if (!cts.IsCancellationRequested)
                {
                    StartCoroutine(ReconnectAfterDelay());
                }
                break;
            }
        }
        
        DebugLog("消息接收循环已结束");
    }

    // 处理接收到的 WebSocket 消息
   // 处理接收到的 WebSocket 消息
private void ProcessWebSocketMessage(string json)
{
    try
    {
        var eventData = JsonUtility.FromJson<ActionEvent>(json);
        
        // 在调试日志中显示解析后的数据
        DebugLog($"解析JSON结果: motion_type={eventData.motion_type}, timestamp={eventData.timestamp}");
        
        // 处理动作事件
        switch (eventData.motion_type.ToLower())
        {
            case "stomp":
                DebugLog($"收到跺脚事件 @{eventData.timestamp}");
                
                // 触发自己的事件
                OnStompDetected?.Invoke();
                
                // 直接调用 IMUEventManager
                if (IMUEventManager.Instance != null)
                {
                    DebugLog("正在调用 IMUEventManager.TriggerStompEvent()");
                    IMUEventManager.Instance.TriggerStompEvent();
                }
                else
                {
                    DebugLog("错误: IMUEventManager.Instance 为空，无法触发跺脚事件", true);
                }
                break;
                
            case "kick":
                DebugLog($"收到踢腿事件 @{eventData.timestamp}");
                
                // 触发自己的事件
                OnKickDetected?.Invoke();
                
                // 直接调用 IMUEventManager
                if (IMUEventManager.Instance != null)
                {
                    DebugLog("正在调用 IMUEventManager.TriggerKickEvent()");
                    IMUEventManager.Instance.TriggerKickEvent();
                }
                else
                {
                    DebugLog("错误: IMUEventManager.Instance 为空，无法触发踢腿事件", true);
                }
                break;
                
            default:
                DebugLog($"未知的动作类型: {eventData.motion_type}");
                break;
        }
    }
    catch (Exception e)
    {
        DebugLog($"消息解析失败: {e.Message}\n原始数据: {json}", true);
        
        // 尝试使用更灵活的 JSON 解析方法
        try
        {
            // 使用手动解析，因为 JsonUtility 有时候会很严格
            if (json.Contains("\"motion_type\":\"stomp\""))
            {
                DebugLog("手动解析检测到跺脚动作");
                
                // 触发自己的事件
                OnStompDetected?.Invoke();
                
                // 直接调用 IMUEventManager
                if (IMUEventManager.Instance != null)
                {
                    DebugLog("正在调用 IMUEventManager.TriggerStompEvent()");
                    IMUEventManager.Instance.TriggerStompEvent();
                }
            }
            else if (json.Contains("\"motion_type\":\"kick\""))
            {
                DebugLog("手动解析检测到踢腿动作");
                
                // 触发自己的事件
                OnKickDetected?.Invoke();
                
                // 直接调用 IMUEventManager
                if (IMUEventManager.Instance != null)
                {
                    DebugLog("正在调用 IMUEventManager.TriggerKickEvent()");
                    IMUEventManager.Instance.TriggerKickEvent();
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog($"手动解析也失败: {ex.Message}", true);
        }
    }
}

// 修改 DebugLog 方法以支持错误日志
private void DebugLog(string message, bool isError = false)
{
    if (!debugLog) return;
    
    if (isError)
        Debug.LogError($"[BLEReceiver] {message}");
    else
        Debug.Log($"[BLEReceiver] {message}");
}

    // 重连逻辑
    private IEnumerator ReconnectAfterDelay()
    {
        DebugLog($"将在 {reconnectInterval} 秒后重新连接...");
        yield return new WaitForSeconds(reconnectInterval);
        ConnectToServer();
    }

    // 断开连接
    public void Disconnect()
    {
        if (cts != null)
        {
            cts.Cancel();
            cts.Dispose();
            cts = null;
        }
        
        if (webSocket != null)
        {
            if (webSocket.State == WebSocketState.Open)
            {
                try
                {
                    // 尝试优雅地关闭
                    var closeTask = webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Client disconnecting",
                        CancellationToken.None);
                    
                    // 等待关闭完成，但设置超时
                    var completedTask = Task.WaitAny(new Task[] { closeTask }, 1000);
                    if (completedTask == -1)
                    {
                        DebugLog("关闭连接超时");
                    }
                }
                catch (Exception e)
                {
                    DebugLog($"关闭连接时出错: {e.Message}");
                }
            }
            
            webSocket.Dispose();
            webSocket = null;
        }
        
        isConnected = false;
        DebugLog("连接已关闭");
    }

    // 记录调试信息
    private void DebugLog(string message)
    {
        if (debugLog) 
            Debug.Log($"[BLEReceiver] {message}");
    }

    #if UNITY_EDITOR
    // 编辑器调试按钮
    [ContextMenu("模拟跺脚事件")]
    private void EditorSimulateStomp()
    {
        OnStompDetected?.Invoke();
    }

    [ContextMenu("模拟踢腿事件")]
    private void EditorSimulateKick()
    {
        OnKickDetected?.Invoke();
    }
    #endif

    void OnGUI()
    {
        if (!showConnectionStatus) return;

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 24,
            normal = { textColor = isConnected ? Color.green : Color.red }
        };

        GUI.Label(
            new Rect(20, 20, 400, 50), 
            isConnected ? "已连接" : "未连接", 
            style
        );
        
        // 添加手动重连按钮
        if (!isConnected)
        {
            if (GUI.Button(new Rect(20, 70, 150, 40), "重新连接"))
            {
                ConnectToServer();
            }
        }
    }
}