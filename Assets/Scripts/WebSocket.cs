using UnityEngine;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class SimpleWebSocketTest : MonoBehaviour
{
    public string serverUrl = "ws://localhost:8765";
    private ClientWebSocket webSocket;
    private CancellationTokenSource cts;
    private bool isConnected = false;
    
    void Start()
    {
        ConnectToServer();
    }
    
    async void ConnectToServer()
    {
        Debug.Log("尝试连接到服务器...");
        webSocket = new ClientWebSocket();
        cts = new CancellationTokenSource();
        
        try
        {
            await webSocket.ConnectAsync(new Uri(serverUrl), cts.Token);
            isConnected = true;
            Debug.Log("连接成功！");
            ReceiveMessages();
        }
        catch (Exception e)
        {
            Debug.LogError($"连接失败: {e.GetType().Name} - {e.Message}");
            if (e.InnerException != null)
            {
                Debug.LogError($"内部异常: {e.InnerException.Message}");
            }
        }
    }
    
    async void ReceiveMessages()
    {
        var buffer = new byte[1024];
        
        while (webSocket.State == WebSocketState.Open)
        {
            try
            {
                var result = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), cts.Token);
                
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Debug.Log("服务器关闭了连接");
                    isConnected = false;
                    break;
                }
                
                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                Debug.Log($"收到消息: {message}");
            }
            catch (Exception e)
            {
                Debug.LogError($"接收消息时出错: {e.Message}");
                isConnected = false;
                break;
            }
        }
        
        Debug.Log("消息接收循环已结束");
    }
    
    void OnGUI()
    {
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 24;
        style.normal.textColor = isConnected ? Color.green : Color.red;
        
        GUI.Label(new Rect(10, 10, 300, 50), 
            isConnected ? "已连接" : "未连接", style);
    }
    
    void OnDestroy()
    {
        cts?.Cancel();
        cts?.Dispose();
        webSocket?.Dispose();
    }
}