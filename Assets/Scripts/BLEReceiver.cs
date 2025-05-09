using UnityEngine;
using System;
using System.Threading.Tasks;
using System.Net.WebSockets;
using System.Threading;
using System.Text;
using System.Collections;

// 主线程调度器（保持原有实现）
public class UnityMainThreadDispatcher : MonoBehaviour { /* 原有代码保持不变 */ }

[System.Serializable]
public class ActionEvent
{
    public string action; // "stomp" 或 "kick"
    public float timestamp;
}

public class BLESimpleReceiver : MonoBehaviour
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
    private ClientWebSocket _webSocket;
    private CancellationTokenSource _cts;
    private bool _isConnected;
    private bool _isConnecting;

    void Start()
    {
        if (autoConnect) ConnectToServer();
    }

    void OnDestroy()
    {
        Disconnect();
    }

    public void ConnectToServer()
    {
        if (_isConnected || _isConnecting) return;
        
        DebugLog("正在连接服务器...");
        Task.Run(WebSocketConnectLoop);
    }

    private async Task WebSocketConnectLoop()
    {
        _isConnecting = true;
        
        while (!_isConnected && Application.isPlaying)
        {
            try
            {
                using (_webSocket = new ClientWebSocket())
                using (_cts = new CancellationTokenSource())
                {
                    await _webSocket.ConnectAsync(
                        new Uri(serverUrl), 
                        _cts.Token
                    );

                    _isConnected = true;
                    DebugLog("连接成功");
                    await WebSocketReceiveLoop();
                }
            }
            catch (Exception e)
            {
                DebugLog($"连接失败: {e.Message}");
            }
            finally
            {
                _isConnected = false;
                if (Application.isPlaying)
                {
                    await Task.Delay((int)(reconnectInterval * 1000));
                }
            }
        }
        _isConnecting = false;
    }

    private async Task WebSocketReceiveLoop()
    {
        var buffer = new byte[1024];
        
        try
        {
            while (_webSocket.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), 
                    _cts.Token
                );

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure, 
                        "Close requested", 
                        _cts.Token
                    );
                    break;
                }

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                ProcessWebSocketMessage(json);
            }
        }
        catch (Exception e)
        {
            DebugLog($"接收中断: {e.Message}");
        }
        finally
        {
            Disconnect();
        }
    }

    // 修改138行附近代码（原错误位置）
private void ProcessWebSocketMessage(string json)
{
    try
    {
        var eventData = JsonUtility.FromJson<ActionEvent>(json);
        
        // 修改为使用属性访问方式
        UnityMainThreadDispatcher.Instance.Enqueue(() => 
        {
            switch (eventData.action.ToLower())
            {
                case "stomp":
                    OnStompDetected?.Invoke();
                    Debug.Log($"收到跺脚事件 @{eventData.timestamp}");
                    break;
                    
                case "kick":
                    OnKickDetected?.Invoke();
                    Debug.Log($"收到踢腿事件 @{eventData.timestamp}");
                    break;
            }
        });
    }
    catch (Exception e)
    {
        Debug.LogError($"消息解析失败: {e.Message}\n原始数据: {json}");
    }
}

    public void Disconnect()
    {
        _isConnected = false;
        _cts?.Cancel();
        DebugLog("连接已关闭");
    }

    private void DebugLog(string message)
    {
        if (debugLog) Debug.Log($"[BLEReceiver] {message}");
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
            normal = { textColor = _isConnected ? Color.green : Color.red }
        };

        GUI.Label(
            new Rect(20, 20, 400, 50), 
            _isConnected ? "已连接" : "未连接", 
            style
        );
    }
}