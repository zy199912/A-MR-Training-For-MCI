using UnityEngine;
using UnityEngine.UI; 
using System;
using System.Collections;

public class IMUEventManager : MonoBehaviour
{
    [Header("键盘映射")]
    [Tooltip("跺脚动作映射到的按键")]
    public KeyCode stompKey = KeyCode.S;
    
    [Tooltip("踢腿动作映射到的按键")]
    public KeyCode kickKey = KeyCode.W;
    
    [Header("按键模拟设置")]
    [Tooltip("按键模拟持续时间（秒）")]
    public float keyPressDuration = 0.1f;
    
    [Tooltip("动作冷却时间（秒）")]
    public float actionCooldown = 0.5f;

    [Header("调试")]
    public bool debugLog = true;

    // 单例模式
    public static IMUEventManager Instance { get; private set; }

    // 内部状态跟踪
    private float lastStompTime;
    private float lastKickTime;
    private bool isPressingStompKey;
    private bool isPressingKickKey;
    
    // 添加事件定义，使其与 GarbageMover 中的引用匹配
    public event Action OnStompDetected;
    public event Action OnKickDetected;

    private void Awake()
    {
        // 单例模式设置
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // 查找并订阅 BLEReceiver 的事件
        BLEReceiver receiver = FindObjectOfType<BLEReceiver>();
        if (receiver != null)
        {
            receiver.OnStompDetected += HandleStompDetected;
            receiver.OnKickDetected += HandleKickDetected;
            DebugLog("已成功订阅 BLEReceiver 事件");
        }
        else
        {
            DebugLog("警告: 未找到 BLEReceiver 组件，无法订阅事件", LogType.Warning);
        }
    }

    // 处理跺脚事件
    public void HandleStompDetected()
    {
        if (Time.time - lastStompTime > actionCooldown)
        {
            lastStompTime = Time.time;
            DebugLog($"检测到跺脚，模拟按下 {stompKey} 键");
            
            // 触发事件，让 GarbageMover 能够响应
            OnStompDetected?.Invoke();
            
            StartCoroutine(SimulateKeyPress(stompKey));
        }
    }

    // 处理踢腿事件
    public void HandleKickDetected()
    {
        if (Time.time - lastKickTime > actionCooldown)
        {
            lastKickTime = Time.time;
            DebugLog($"检测到踢腿，模拟按下 {kickKey} 键");
            
            // 触发事件，让 GarbageMover 能够响应
            OnKickDetected?.Invoke();
            
            StartCoroutine(SimulateKeyPress(kickKey));
        }
    }

    // 模拟按键协程
    private IEnumerator SimulateKeyPress(KeyCode key)
    {
        // 开始按键
        switch (key)
        {
            case KeyCode.S:
                isPressingStompKey = true;
                break;
            case KeyCode.W:
                isPressingKickKey = true;
                break;
        }
        
        // 等待指定时间
        yield return new WaitForSeconds(keyPressDuration);
        
        // 结束按键
        switch (key)
        {
            case KeyCode.S:
                isPressingStompKey = false;
                break;
            case KeyCode.W:
                isPressingKickKey = false;
                break;
        }
    }

    // 供其他脚本调用的触发方法
    public void TriggerStompEvent()
    {
        HandleStompDetected();
    }

    public void TriggerKickEvent()
    {
        HandleKickDetected();
    }

    // 模拟 Input.GetKey 的功能
    void Update()
    {
        // 这部分代码用于检测其他脚本是否正在查询这些键的状态
        if (Input.GetKeyDown(stompKey) && !isPressingStompKey)
        {
            DebugLog($"检测到真实键盘 {stompKey} 按下");
            // 当检测到真实键盘按下时，也触发对应事件
            OnStompDetected?.Invoke();
        }
        
        if (Input.GetKeyDown(kickKey) && !isPressingKickKey)
        {
            DebugLog($"检测到真实键盘 {kickKey} 按下");
            // 当检测到真实键盘按下时，也触发对应事件
            OnKickDetected?.Invoke();
        }
    }

    // 重写键盘输入处理
    public static bool GetKey(KeyCode key)
    {
        if (Instance == null) return Input.GetKey(key);
        
        switch (key)
        {
            case KeyCode.S:
                return Input.GetKey(key) || Instance.isPressingStompKey;
            case KeyCode.W:
                return Input.GetKey(key) || Instance.isPressingKickKey;
            default:
                return Input.GetKey(key);
        }
    }

    // 重写键盘按下检测
    public static bool GetKeyDown(KeyCode key)
    {
        if (Instance == null) return Input.GetKeyDown(key);
        
        // 这个实现比较简单，可能需要更复杂的逻辑来正确模拟 KeyDown 事件
        switch (key)
        {
            case KeyCode.S:
                return Input.GetKeyDown(key) || Instance.isPressingStompKey;
            case KeyCode.W:
                return Input.GetKeyDown(key) || Instance.isPressingKickKey;
            default:
                return Input.GetKeyDown(key);
        }
    }

    private void DebugLog(string message, LogType logType = LogType.Log)
    {
        if (!debugLog) return;
        
        switch (logType)
        {
            case LogType.Warning:
                Debug.LogWarning($"[IMUEventManager] {message}");
                break;
            case LogType.Error:
                Debug.LogError($"[IMUEventManager] {message}");
                break;
            default:
                Debug.Log($"[IMUEventManager] {message}");
                break;
        }
    }

    // 编辑器中的测试按钮
    #if UNITY_EDITOR
    [ContextMenu("测试跺脚 (S键)")]
    void TestStomp()
    {
        TriggerStompEvent();
    }
    
    [ContextMenu("测试踢腿 (W键)")]
    void TestKick()
    {
        TriggerKickEvent();
    }
    #endif
}