using UnityEngine;
using System;

public class IMUEventManager : MonoBehaviour
{
    // 单例模式
    public static IMUEventManager Instance { get; private set; }
    
    // 定义事件
    public event Action OnStompEvent;
    public event Action OnKickEvent;
    
    [Header("调试选项")]
    public bool printDebugLogs = true;
    
    private void Awake()
    {
        // 单例模式初始化
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
    
    // 触发跺脚事件
    public void TriggerStompEvent()
    {
        if (printDebugLogs)
            Debug.Log("IMU事件管理器: 触发跺脚事件");
            
        if (OnStompEvent == null && printDebugLogs)
            Debug.LogWarning("没有对象订阅跺脚事件");
        else
            OnStompEvent?.Invoke();
    }
    
    // 触发踢腿事件
    public void TriggerKickEvent()
    {
        if (printDebugLogs)
            Debug.Log("IMU事件管理器: 触发踢腿事件");
            
        if (OnKickEvent == null && printDebugLogs)
            Debug.LogWarning("没有对象订阅踢腿事件");
        else
            OnKickEvent?.Invoke();
    }
}