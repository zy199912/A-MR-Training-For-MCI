using System.Collections;
using System.Collections.Generic;
using UnityEngine;



public class GarbageMover : MonoBehaviour
{
    public Transform targetPosition; // Ŀ��λ�ö�Ӧ�� Transform���� LeftFoot �� Transform
    public Transform trashCanPosition; // ����Ͱ��λ��
    public float moveSpeed; // �����ƶ��ٶ�
    public GameObject highlightObject; // ������ʾ�Ķ��󣬼� Can-Highlight
    public GarbageShapeType shapeType; // ���������״���ͱ���

    private bool isHighlighted = false;
    private bool isMovingToTrashCan = false;
    private float parabolaTime = 0f;
    private Vector3 startPosition;
    private float parabolaDuration = 1f; // �������ƶ��ĳ���ʱ��
    public float parabolaHeight = 3f; // �����ߵĸ߶�

    void Update()
    {
        if (targetPosition != null && !isMovingToTrashCan)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPosition.position, moveSpeed * Time.deltaTime);
            float distance = Vector3.Distance(transform.position, targetPosition.position);
            if (distance < 0.8f && !isHighlighted)
            {
                Debug.Log("���� SetHighlighted ����");
                SetHighlighted();
            }

            // ������״���ʹ���������
            if (isHighlighted)
            {
                if (shapeType == GarbageShapeType.Cylinder && Input.GetKeyDown(KeyCode.W))
                {
                    StartMovingToTrashCan();
                }
                else if (shapeType == GarbageShapeType.Square && Input.GetKeyDown(KeyCode.S))
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
        Debug.Log("SetHighlighted ����ִ����ϣ�isHighlighted ������Ϊ true");
    }

    void StartMovingToTrashCan()
    {
        if (trashCanPosition == null)
        {
            Debug.LogError("û����������Ͱλ�ã�����Inspector������trashCanPosition");
            return;
        }

        isMovingToTrashCan = true;
        startPosition = transform.position;
        parabolaTime = 0f;
        Debug.Log($"��ʼ������Ͱ�ƶ���Ŀ��λ��: {trashCanPosition.position}");
    }

    void MoveToTrashCan()
    {
        parabolaTime += Time.deltaTime;

        if (parabolaTime <= parabolaDuration)
        {
            float t = parabolaTime / parabolaDuration;
            Vector3 start = startPosition;
            Vector3 end = trashCanPosition.position;

            // �����������е㣬ȷ���ڿ���
            float maxY = Mathf.Max(start.y, end.y);
            Vector3 mid = new Vector3(
                (start.x + end.x) / 2f,
                maxY + parabolaHeight,
                (start.z + end.z) / 2f
            );

            // ���α���������ʵ��������
            Vector3 newPosition = (1 - t) * (1 - t) * start + 2 * (1 - t) * t * mid + t * t * end;
            transform.position = newPosition;
        }
        else
        {
            isMovingToTrashCan = false;
            // ��������Ͱ����������
            Destroy(gameObject);
        }
    }
}