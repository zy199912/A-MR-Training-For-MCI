using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using NativeWebSocket;
using Newtonsoft.Json;

public class BLEDataReceiver : MonoBehaviour
{
    [Header("WebSocket设置")]
    public string serverUrl = "ws://localhost:8765";
    public bool autoConnect = true;
    public float reconnectInterval = 5f;
    
    [Header("动作检测相关")]
    public float stompThreshold = 5.0f;  // 跺脚阈值
    public float kickThreshold = 3.0f;   // 踢腿阈值
    private float lastStompTime = 0f;
    private float lastKickTime = 0f;
    private float cooldownTime = 0.5f;
    
    [Header("传感器轴向映射")]
    [Tooltip("脚踝设备垂直向上的轴向")]
    public AxisDirection verticalAxis = AxisDirection.X;
    [Tooltip("脚踝设备前后方向的轴向")]
    public AxisDirection forwardAxis = AxisDirection.Z;
    [Tooltip("脚踝设备左右方向的轴向")]
    public AxisDirection lateralAxis = AxisDirection.Y;
    
    // 加速度方向枚举
    public enum AxisDirection { X, Y, Z, NegativeX, NegativeY, NegativeZ }
    
    // 传感器数据
    private Vector3 acceleration = Vector3.zero;
    private Vector3 gyro = Vector3.zero;
    private Vector3 baselineAcceleration = Vector3.zero;
    private bool isCalibrated = false;
    
    // 校准相关
    private float calibrationTimer = 0f;
    private float calibrationTime = 3.0f;
    private List<Vector3> calibrationSamples = new List<Vector3>();
    
    // WebSocket相关
    private WebSocket websocket;
    private bool isConnected = false;
    private bool isReconnecting = false;
    
    // 调试显示
    [Header("调试")]
    public bool showDebugInfo = true;
    public GUIStyle debugTextStyle;
    
    [Serializable]
    private class IMUData
    {
        [Serializable]
        public class Vector3Data
        {
            public float x;
            public float y;
            public float z;
        }
        
        public Vector3Data acceleration;
        public Vector3Data gyro;
        public string raw;
    }
    
    void Start()
    {
        // 初始化调试显示样式
        if (debugTextStyle == null)
        {
            debugTextStyle = new GUIStyle();
            debugTextStyle.normal.textColor = Color.white;
            debugTextStyle.fontSize = 16;
            debugTextStyle.wordWrap = true;
        }
        
        if (autoConnect)
            ConnectToServer();
    }
    
    async void ConnectToServer()
    {
        if (websocket != null)
        {
            await websocket.Close();
        }
        
        Debug.Log("正在连接到WebSocket服务器: " + serverUrl);
        
        websocket = new WebSocket(serverUrl);
        
        websocket.OnOpen += () => {
            Debug.Log("WebSocket连接已打开");
            isConnected = true;
            isReconnecting = false;
            
            // 开始校准
            StartCalibration();
        };
        
        websocket.OnMessage += (bytes) => {
            string message = System.Text.Encoding.UTF8.GetString(bytes);
            ProcessWebSocketMessage(message);
        };
        
        websocket.OnError += (e) => {
            Debug.LogError("WebSocket错误: " + e);
        };
        
        websocket.OnClose += (e) => {
            Debug.Log("WebSocket连接已关闭");
            isConnected = false;
            
            if (!isReconnecting)
                StartCoroutine(ReconnectAfterDelay());
        };
        
        // 连接到服务器
        await websocket.Connect();
    }
    
    IEnumerator ReconnectAfterDelay()
    {
        if (isReconnecting)
            yield break;
            
        isReconnecting = true;
        
        Debug.Log($"尝试在 {reconnectInterval} 秒后重新连接...");
        yield return new WaitForSeconds(reconnectInterval);
        
        ConnectToServer();
    }
    
    void ProcessWebSocketMessage(string message)
    {
        try
        {
            IMUData imuData = JsonConvert.DeserializeObject<IMUData>(message);
            
            // 更新传感器数据
            if (imuData.acceleration != null)
            {
                acceleration.x = imuData.acceleration.x;
                acceleration.y = imuData.acceleration.y;
                acceleration.z = imuData.acceleration.z;
            }
            
            if (imuData.gyro != null)
            {
                gyro.x = imuData.gyro.x;
                gyro.y = imuData.gyro.y;
                gyro.z = imuData.gyro.z;
            }
            
            // 校准或检测动作
            if (!isCalibrated)
            {
                CalibrateIMU();
            }
            else
            {
                DetectMotions();
            }
        }
        catch (Exception e)
        {
            Debug.LogError("处理WebSocket消息错误: " + e.Message);
        }
    }
    
    void StartCalibration()
    {
        // 开始校准
        calibrationTimer = 0f;
        isCalibrated = false;
        calibrationSamples.Clear();
        Debug.Log("开始IMU校准。请保持静止3秒...");
    }
    
    private void CalibrateIMU()
    {
        calibrationTimer += Time.deltaTime;
        calibrationSamples.Add(acceleration);
        
        if (calibrationTimer >= calibrationTime)
        {
            // 计算基准值
            Vector3 sum = Vector3.zero;
            foreach (var sample in calibrationSamples)
            {
                sum += sample;
            }
            baselineAcceleration = sum / calibrationSamples.Count;
            
            Debug.Log($"校准完成。基准加速度: {baselineAcceleration}");
            isCalibrated = true;
        }
    }
    
    // 获取指定轴的加速度值
    private float GetAxisAcceleration(AxisDirection axis, Vector3 acceleration)
    {
        switch (axis)
        {
            case AxisDirection.X: return acceleration.x;
            case AxisDirection.Y: return acceleration.y;
            case AxisDirection.Z: return acceleration.z;
            case AxisDirection.NegativeX: return -acceleration.x;
            case AxisDirection.NegativeY: return -acceleration.y;
            case AxisDirection.NegativeZ: return -acceleration.z;
            default: return 0f;
        }
    }
    
    // 获取指定轴的陀螺仪值
    private float GetAxisGyro(AxisDirection axis, Vector3 gyro)
    {
        switch (axis)
        {
            case AxisDirection.X: return gyro.x;
            case AxisDirection.Y: return gyro.y;
            case AxisDirection.Z: return gyro.z;
            case AxisDirection.NegativeX: return -gyro.x;
            case AxisDirection.NegativeY: return -gyro.y;
            case AxisDirection.NegativeZ: return -gyro.z;
            default: return 0f;
        }
    }
    
    private void DetectMotions()
    {
        // 计算相对于基准的加速度
        Vector3 relativeAccel = acceleration - baselineAcceleration;
        
        // 根据传感器轴向映射获取正确的加速度值
        float verticalAccel = GetAxisAcceleration(verticalAxis, relativeAccel);
        float forwardAccel = GetAxisAcceleration(forwardAxis, relativeAccel);
        float lateralAccel = GetAxisAcceleration(lateralAxis, relativeAccel);
        
        // 获取对应轴的陀螺仪值
        float verticalGyro = GetAxisGyro(verticalAxis, gyro);
        float forwardGyro = GetAxisGyro(forwardAxis, gyro);
        float lateralGyro = GetAxisGyro(lateralAxis, gyro);
        
        // 检测跺脚（垂直方向的强烈加速度变化）
        if (Mathf.Abs(verticalAccel) > stompThreshold && Time.time > lastStompTime + cooldownTime)
        {
            Debug.Log($"检测到跺脚！垂直加速度: {verticalAccel:F2}");
            lastStompTime = Time.time;
            
            // 通过事件管理器触发跺脚事件
            if (IMUEventManager.Instance != null)
                IMUEventManager.Instance.TriggerStompEvent();
        }
        
        // 检测踢腿（前后方向的加速度变化与一些角速度变化）
        if (forwardAccel > kickThreshold && 
            (Mathf.Abs(lateralGyro) > kickThreshold/2 || Mathf.Abs(verticalGyro) > kickThreshold/2) && 
            Time.time > lastKickTime + cooldownTime)
        {
            Debug.Log($"检测到踢腿！前后加速度: {forwardAccel:F2}, 侧向陀螺仪: {lateralGyro:F2}, 垂直陀螺仪: {verticalGyro:F2}");
            lastKickTime = Time.time;
            
            // 通过事件管理器触发踢腿事件
            if (IMUEventManager.Instance != null)
                IMUEventManager.Instance.TriggerKickEvent();
        }
    }
    
    void Update()
    {
        if (websocket != null)
        {
            #if !UNITY_WEBGL || UNITY_EDITOR
            websocket.DispatchMessageQueue();
            #endif
        }
    }
    
    void OnGUI()
    {
        if (showDebugInfo && isConnected)
        {
            float y = 10;
            float lineHeight = 20;
            
            GUI.Label(new Rect(10, y, 400, lineHeight), $"连接状态: {(isConnected ? "已连接" : "未连接")}", debugTextStyle);
            y += lineHeight;
            
            GUI.Label(new Rect(10, y, 400, lineHeight), $"校准状态: {(isCalibrated ? "已校准" : "校准中...")}", debugTextStyle);
            y += lineHeight;
            
            GUI.Label(new Rect(10, y, 400, lineHeight), $"加速度: X={acceleration.x:F2}, Y={acceleration.y:F2}, Z={acceleration.z:F2}", debugTextStyle);
            y += lineHeight;
            
            GUI.Label(new Rect(10, y, 400, lineHeight), $"陀螺仪: X={gyro.x:F2}, Y={gyro.y:F2}, Z={gyro.z:F2}", debugTextStyle);
            y += lineHeight;
            
            if (isCalibrated)
            {
                Vector3 relativeAccel = acceleration - baselineAcceleration;
                GUI.Label(new Rect(10, y, 400, lineHeight), $"相对加速度: X={relativeAccel.x:F2}, Y={relativeAccel.y:F2}, Z={relativeAccel.z:F2}", debugTextStyle);
                y += lineHeight;
                
                float verticalAccel = GetAxisAcceleration(verticalAxis, relativeAccel);
                float forwardAccel = GetAxisAcceleration(forwardAxis, relativeAccel);
                float lateralAccel = GetAxisAcceleration(lateralAxis, relativeAccel);
                
                GUI.Label(new Rect(10, y, 400, lineHeight), $"映射加速度: 垂直={verticalAccel:F2}, 前后={forwardAccel:F2}, 横向={lateralAccel:F2}", debugTextStyle);
            }
        }
    }
    
    async void OnApplicationQuit()
    {
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            await websocket.Close();
        }
    }
}