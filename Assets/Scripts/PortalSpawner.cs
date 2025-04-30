using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NewBehaviourScript : MonoBehaviour
{
    public GarbageData[] garbageDatas; // ����������ݵ����飬����Inspector��帳ֵ
    public Transform topPoint; // TopPoint��Transform���������������λ�ã�����Inspector�����ק��ֵ
    public Transform leftFoot; // LeftFoot��Transform������������ƶ�Ŀ��λ�ã�����Inspector�����ק��ֵ
    public float detectionRadius = 1f; // ���LeftFootλ���Ƿ��������İ뾶

    private float timer = 0f;
    public float spawnInterval = 3f; // ���ɼ��ʱ��

    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= spawnInterval && !IsGarbageAtLeftFoot())
        {
            SpawnGarbage();
            timer = 0f;
        }
    }

    bool IsGarbageAtLeftFoot()
    {
        Collider[] colliders = Physics.OverlapSphere(leftFoot.position, detectionRadius);
        foreach (Collider collider in colliders)
        {
            if (collider.CompareTag("Garbage")) // ������������ı�ǩΪ "Garbage"
            {
                return true;
            }
        }
        return false;
    }

    void SpawnGarbage()
    {
        if (garbageDatas.Length > 0)
        {
            int randomIndex = Random.Range(0, garbageDatas.Length);
            GarbageData currentGarbageData = garbageDatas[randomIndex];

            // ʵ��������Ԥ����
            GameObject spawnedGarbage = Instantiate(currentGarbageData.garbagePrefab, topPoint.position, Quaternion.identity);

            // Ϊ��������ƶ��ű������ò���
            GarbageMover garbageMover = spawnedGarbage.AddComponent<GarbageMover>();
            garbageMover.targetPosition = leftFoot;
            garbageMover.moveSpeed = currentGarbageData.moveSpeed;
        }
        else
        {
            Debug.LogError("garbageDatas����δ��ȷ��ֵ��û�п����ɵ��������ݣ�");
        }
    }
}
