using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using UnityEngine;

// ����������״���͵�ö��
public enum GarbageShapeType
{
    Cylinder,
    Square
}

// ��������Unity�˵��д������ʲ�
[CreateAssetMenu(fileName = "NewGarbageData", menuName = "Garbage Data", order = 1)]
public class GarbageData : ScriptableObject
{
    // ����Ԥ����
    public GameObject garbagePrefab;
    // ������������״����
    public GarbageShapeType shapeType;
    // ��������
    public string garbageName;
    // �����ƶ��ٶ�
    public float moveSpeed;
}