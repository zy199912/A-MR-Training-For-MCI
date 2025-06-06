using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GarbageMoverR : MonoBehaviour
{
    public Transform targetPosition; // 目标位置对应的 Transform，即 LeftFoot 的 Transform
    public Transform trashCanPosition; // 垃圾桶的位置
    public float moveSpeed; // 垃圾移动速度
    public GameObject highlightObject; // 高亮显示的对象，即 Can-Highlight
    public GarbageShapeType shapeType; // 添加垃圾形状类型变量

    private bool isHighlighted = false;
    private bool isMovingToTrashCan = false;
    private float parabolaTime = 0f;
    private Vector3 startPosition;
    private float parabolaDuration = 1f; // 抛物线移动的持续时间
    public float parabolaHeight = 3f; // 抛物线的高度

    void Update()
    {
        if (targetPosition != null && !isMovingToTrashCan)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPosition.position, moveSpeed * Time.deltaTime);
            float distance = Vector3.Distance(transform.position, targetPosition.position);
            if (distance < 0.8f && !isHighlighted)
            {
                Debug.Log("触发 SetHighlighted 方法");
                SetHighlighted();
            }

            // 根据形状类型处理按键输入
            if (isHighlighted)
            {
                if (shapeType == GarbageShapeType.Cylinder && Input.GetKeyDown(KeyCode.UpArrow))
                {
                    StartMovingToTrashCan();
                }
                else if (shapeType == GarbageShapeType.Square && Input.GetKeyDown(KeyCode.DownArrow))
                {
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
        Debug.Log("SetHighlighted 方法执行完毕，isHighlighted 已设置为 true");
    }

    void StartMovingToTrashCan()
    {
        if (trashCanPosition == null)
        {
            Debug.LogError("没有设置垃圾桶位置！请在Inspector中设置trashCanPosition");
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

            // 计算抛物线中点，确保在空中
            float maxY = Mathf.Max(start.y, end.y);
            Vector3 mid = new Vector3(
                (start.x + end.x) / 2f,
                maxY + parabolaHeight,
                (start.z + end.z) / 2f
            );

            // 二次贝塞尔曲线实现抛物线
            Vector3 newPosition = (1 - t) * (1 - t) * start + 2 * (1 - t) * t * mid + t * t * end;
            transform.position = newPosition;
        }
        else
        {
            isMovingToTrashCan = false;
            // 到达垃圾桶后销毁垃圾
            Destroy(gameObject);
        }
    }
}