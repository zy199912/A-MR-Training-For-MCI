using UnityEngine;
using System;
using System.Collections;

public class SimpleIMUConnector : MonoBehaviour
{
    // 设备信息 - 尝试你的第二个地址
    public string deviceAddress = "19:6F:51:5D:D5:D6";
    
    // UUID信息
    public string serviceUUID = "ae30";
    public string notifyCharacteristicUUID = "ae02";
    
    // 连接状态
    private bool isConnecting = false;
    private bool isConnected = false;
    
    void Start()
    {
        // 稍微延迟以确保所有内容都已初始化
        Invoke("Initialize", 1.0f);
    }
    
    void Initialize()
    {
        Debug.Log("开始初始化蓝牙...");
        
        BluetoothLEHardwareInterface.Initialize(true, false, () => {
            Debug.Log("蓝牙初始化成功");
            
            // 启用蓝牙
            BluetoothLEHardwareInterface.BluetoothEnable(true);
            
            // 设置高性能模式
            BluetoothLEHardwareInterface.BluetoothScanMode(BluetoothLEHardwareInterface.ScanMode.LowLatency);
            BluetoothLEHardwareInterface.BluetoothConnectionPriority(BluetoothLEHardwareInterface.ConnectionPriority.High);
            
            // 开始连接
            Invoke("StartConnection", 2.0f);
            
        }, (error) => {
            Debug.LogError("蓝牙初始化失败: " + error);
            
            if (error.Contains("Bluetooth LE Not Enabled"))
            {
                Debug.Log("尝试启用蓝牙...");
                BluetoothLEHardwareInterface.BluetoothEnable(true);
                Invoke("Initialize", 3.0f);
            }
        });
    }
    
    void StartConnection()
    {
        if (isConnecting || isConnected)
            return;
            
        isConnecting = true;
        Debug.Log("开始连接到设备: " + deviceAddress);
        
        // 直接连接到设备
        BluetoothLEHardwareInterface.ConnectToPeripheral(deviceAddress, (address) => {
            // 连接成功
            Debug.Log("已连接到设备: " + address);
            isConnected = true;
            isConnecting = false;
            
        }, (address, serviceUUID) => {
            // 发现服务
            Debug.Log("发现服务: " + serviceUUID);
            
        }, (address, serviceUUID, characteristicUUID) => {
            // 发现特征
            Debug.Log("发现特征: " + serviceUUID + " -> " + characteristicUUID);
            
            // 检查是否是我们要的特征
            if (serviceUUID.ToLower().Contains(this.serviceUUID.ToLower()) && 
                characteristicUUID.ToLower().Contains(this.notifyCharacteristicUUID.ToLower()))
            {
                Debug.Log("找到IMU数据特征，准备订阅");
                
                // 订阅特征
                string fullServiceUUID = FullUUID(this.serviceUUID);
                string fullCharUUID = FullUUID(this.notifyCharacteristicUUID);
                
                Debug.Log("订阅: " + fullServiceUUID + " -> " + fullCharUUID);
                
                BluetoothLEHardwareInterface.SubscribeCharacteristicWithDeviceAddress(
                    address,
                    fullServiceUUID,
                    fullCharUUID,
                    (addr, characteristic) => {
                        Debug.Log("订阅成功");
                    },
                    (addr, characteristic, value) => {
                        string hexData = BitConverter.ToString(value);
                        Debug.Log("收到数据: " + hexData);
                    }
                );
            }
            
        }, (address) => {
            // 断开连接
            Debug.Log("设备断开连接: " + address);
            isConnected = false;
            isConnecting = false;
            
            // 尝试重新连接
            Invoke("StartConnection", 3.0f);
        });
    }
    
    string FullUUID(string uuid)
    {
        if (uuid.Length == 4)
            return "0000" + uuid + "-0000-1000-8000-00805f9b34fb";
        return uuid;
    }
    
    void OnApplicationQuit()
    {
        CancelInvoke();
        
        if (isConnected)
        {
            BluetoothLEHardwareInterface.DisconnectPeripheral(deviceAddress, null);
        }
        
        BluetoothLEHardwareInterface.DeInitialize(() => {
            BluetoothLEHardwareInterface.FinishDeInitialize();
        });
    }
}