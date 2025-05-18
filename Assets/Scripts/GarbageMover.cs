using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI; 

// 移除重复的枚举定义
// GarbageShapeType 已经在其他地方定义

public class GarbageMover : MonoBehaviour
{
    public Transform targetPosition;
    public Transform trashCanPosition;
    public float moveSpeed;
    public GameObject highlightObject;
    public GarbageShapeType shapeType;

    private bool isHighlighted = false;
    private bool isMovingToTrashCan = false;
    private float parabolaTime = 0f;
    private Vector3 startPosition;
    private float parabolaDuration = 1f;
    public float parabolaHeight = 3f;

    // 添加调试输出，查看是否正确订阅事件
    void Start()
    {
        Debug.Log($"GarbageMover 启动: 形状类型 = {shapeType}");
        
        // 验证 IMUEventManager 是否存在
        if (IMUEventManager.Instance == null)
        {
            Debug.LogError("IMUEventManager.Instance 为空，无法在 Start 时订阅事件");
        }
    }

    void Update()
    {
        if (targetPosition != null && !isMovingToTrashCan)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPosition.position, moveSpeed * Time.deltaTime);
            float distance = Vector3.Distance(transform.position, targetPosition.position);
            if (distance < 0.8f && !isHighlighted)
            {
                Debug.Log("调用 SetHighlighted 方法");
                SetHighlighted();
            }

            // 根据形状类型使用相应的按键
            if (isHighlighted)
            {
                if (shapeType == GarbageShapeType.Cylinder && IMUEventManager.GetKeyDown(KeyCode.W))
                {
                    Debug.Log("检测到 W 键按下或踢腿动作，开始移向垃圾桶");
                    StartMovingToTrashCan();
                }
                else if (shapeType == GarbageShapeType.Square && IMUEventManager.GetKeyDown(KeyCode.S))
                {
                    Debug.Log("检测到 S 键按下或跺脚动作，销毁物体");
                    Destroy(gameObject);
                }
            }
        }

        if (isMovingToTrashCan)
        {
            MoveToTrashCan();
        }
    }

    void SetHighlighted()
    {
        if (highlightObject != null)
        {
            highlightObject.SetActive(true);
        }
        isHighlighted = true;
        Debug.Log("SetHighlighted 方法执行完毕，isHighlighted 设置为 true");
    }

    void StartMovingToTrashCan()
    {
        if (trashCanPosition == null)
        {
            Debug.LogError("没有设置垃圾桶位置，请在Inspector中设置trashCanPosition");
            return;
        }

        isMovingToTrashCan = true;
        startPosition = transform.position;
        parabolaTime = 0f;
        Debug.Log($"开始向垃圾桶移动，目标位置: {trashCanPosition.position}");
    }

    void MoveToTrashCan()
    {
        parabolaTime += Time.deltaTime;

        if (parabolaTime <= parabolaDuration)
        {
            float t = parabolaTime / parabolaDuration;
            Vector3 start = startPosition;
            Vector3 end = trashCanPosition.position;

            float maxY = Mathf.Max(start.y, end.y);
            Vector3 mid = new Vector3(
                (start.x + end.x) / 2f,
                maxY + parabolaHeight,
                (start.z + end.z) / 2f
            );

            Vector3 newPosition = (1 - t) * (1 - t) * start + 2 * (1 - t) * t * mid + t * t * end;
            transform.position = newPosition;
        }
        else
        {
            isMovingToTrashCan = false;
            Destroy(gameObject);
        }
    }

    void OnEnable()
    {
        Debug.Log($"GarbageMover.OnEnable 被调用: 形状类型 = {shapeType}");
        
        if (IMUEventManager.Instance != null)
        {
            if (shapeType == GarbageShapeType.Cylinder)
            {
                Debug.Log("订阅踢腿事件");
                IMUEventManager.Instance.OnKickDetected += OnKickDetected;
            }
            else if (shapeType == GarbageShapeType.Square)
            {
                Debug.Log("订阅跺脚事件");
                IMUEventManager.Instance.OnStompDetected += OnStompDetected;
            }
        }
        else
        {
            Debug.LogError("IMUEventManager.Instance 为空，无法在 OnEnable 时订阅事件");
        }
    }

    void OnDisable()
    {
        Debug.Log($"GarbageMover.OnDisable 被调用: 形状类型 = {shapeType}");
        
        if (IMUEventManager.Instance != null)
        {
            if (shapeType == GarbageShapeType.Cylinder)
            {
                Debug.Log("取消订阅踢腿事件");
                IMUEventManager.Instance.OnKickDetected -= OnKickDetected;
            }
            else if (shapeType == GarbageShapeType.Square)
            {
                Debug.Log("取消订阅跺脚事件");
                IMUEventManager.Instance.OnStompDetected -= OnStompDetected;
            }
        }
    }

    void OnKickDetected()
    {
        Debug.Log("GarbageMover: OnKickDetected 被调用");
        if (isHighlighted && !isMovingToTrashCan)
        {
            Debug.Log("检测到踢腿动作，开始移向垃圾桶");
            StartMovingToTrashCan();
        }
        else
        {
            Debug.Log($"未处理踢腿事件: isHighlighted={isHighlighted}, isMovingToTrashCan={isMovingToTrashCan}");
        }
    }

    void OnStompDetected()
    {
        Debug.Log("GarbageMover: OnStompDetected 被调用");
        if (isHighlighted && !isMovingToTrashCan)
        {
            Debug.Log("检测到跺脚动作，销毁物体");
            Destroy(gameObject);
        }
        else
        {
            Debug.Log($"未处理跺脚事件: isHighlighted={isHighlighted}, isMovingToTrashCan={isMovingToTrashCan}");
        }
    }
}