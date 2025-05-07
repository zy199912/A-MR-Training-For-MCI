import asyncio
import json
import websockets
import logging
from bleak import BleakScanner, BleakClient

# 配置区域 - 根据你的设备修改
DEVICE_NAME = "im600-V3.11"  # 修改为扫描到的名称
SERVICE_UUID = "0000ae30-0000-1000-8000-00805f9b34fb"  # 服务UUID
CHARACTERISTIC_UUID = "0000ae02-0000-1000-8000-00805f9b34fb"  # 通知特征UUID
WEBSOCKET_PORT = 8765  # WebSocket服务器端口
DEVICE_ADDRESS = "19:6F:51:5D:D5:D6"  # 扫描到的设备地址

# 设置日志
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# 保存连接的WebSocket客户端
connected_clients = set()

# 处理IMU数据的函数
def process_imu_data(data):
    """处理来自IMU的原始数据并转换为JSON格式"""
    try:
        # 记录原始数据
        hex_data = data.hex('-')
        logger.info(f"收到原始数据: {hex_data}")
        
        # 根据你的IMU数据格式调整这里的解析逻辑
        # 假设格式与之前讨论的相似
        if len(data) >= 22:
            accel_x = int.from_bytes(data[10:12], byteorder='big', signed=True)
            accel_y = int.from_bytes(data[12:14], byteorder='big', signed=True)
            accel_z = int.from_bytes(data[14:16], byteorder='big', signed=True)
            
            gyro_x = int.from_bytes(data[16:18], byteorder='big', signed=True)
            gyro_y = int.from_bytes(data[18:20], byteorder='big', signed=True)
            gyro_z = int.from_bytes(data[20:22], byteorder='big', signed=True)
            
            # 转换为适当单位
            accel_scale = 1.0 / 32768.0 * 16.0  # 假设±16G量程
            gyro_scale = 1.0 / 32768.0 * 2000.0  # 假设±2000°/s量程
            
            processed_data = {
                "acceleration": {
                    "x": accel_x * accel_scale,
                    "y": accel_y * accel_scale,
                    "z": accel_z * accel_scale
                },
                "gyro": {
                    "x": gyro_x * gyro_scale,
                    "y": gyro_y * gyro_scale,
                    "z": gyro_z * gyro_scale
                },
                "raw": hex_data
            }
            
            return json.dumps(processed_data)
        else:
            logger.warning(f"数据长度不足: {len(data)} 字节")
            return None
    except Exception as e:
        logger.error(f"处理IMU数据出错: {e}")
        return None

# BLE通知回调
def notification_handler(sender, data):
    """处理来自BLE设备的通知"""
    processed_data = process_imu_data(data)
    if processed_data:
        # 发送到所有连接的WebSocket客户端
        websocket_send_task = asyncio.create_task(broadcast_message(processed_data))

async def broadcast_message(message):
    """广播消息到所有WebSocket客户端"""
    if connected_clients:
        await asyncio.gather(*[client.send(message) for client in connected_clients])

# WebSocket连接处理函数
async def websocket_handler(websocket):
    """处理WebSocket连接"""
    logger.info(f"WebSocket客户端连接: {websocket.remote_address}")
    connected_clients.add(websocket)
    try:
        async for message in websocket:
            # 处理来自Unity的消息
            try:
                data = json.loads(message)
                if "command" in data:
                    if data["command"] == "ping":
                        await websocket.send(json.dumps({"response": "pong"}))
            except json.JSONDecodeError:
                logger.warning(f"收到非JSON消息: {message}")
    except websockets.exceptions.ConnectionClosed:
        logger.info("WebSocket连接已关闭")
    finally:
        connected_clients.remove(websocket)

# 扫描并连接BLE设备
async def scan_and_connect():
    """扫描并连接到BLE设备"""
    logger.info(f"开始扫描BLE设备: {DEVICE_NAME}")
    
    device = None
    
    # 尝试直接使用地址连接
    logger.info(f"尝试直接使用地址连接: {DEVICE_ADDRESS}")
    device = await BleakScanner.find_device_by_address(DEVICE_ADDRESS)
    
    # 如果通过地址没找到，尝试扫描
    if not device:
        logger.info("通过地址未找到设备，开始扫描...")
        # 尝试扫描设备
        for _ in range(3):  # 尝试3次
            devices = await BleakScanner.discover()
            logger.info(f"发现了 {len(devices)} 个蓝牙设备")
            for d in devices:
                logger.info(f"发现设备: {d.name} ({d.address})")
                if d.address == DEVICE_ADDRESS or (d.name and DEVICE_NAME.lower() in d.name.lower()):
                    device = d
                    break
            
            if device:
                break
            
            logger.info("未找到设备，重试中...")
            await asyncio.sleep(2)
    
    if not device:
        logger.error(f"无法找到设备: {DEVICE_NAME} 或地址 {DEVICE_ADDRESS}")
        return
    
    logger.info(f"正在连接到设备: {getattr(device, 'name', 'Unknown')} ({device.address})")
    
    client = BleakClient(device)
    
    try:
        await client.connect()
        logger.info("已连接")
        
        # 获取设备服务和特征并存储特征的handle
        target_char_handle = None
        target_service_uuid = None
        
        for service in client.services:
            logger.info(f"发现服务: {service.uuid}")
            for char in service.characteristics:
                logger.info(f"  特征: {char.uuid}, 属性: {char.properties}, handle: {char.handle}")
                
                # 找到我们想要的特征（在正确的服务下）
                if service.uuid.lower() == SERVICE_UUID.lower() and char.uuid.lower() == CHARACTERISTIC_UUID.lower():
                    target_char_handle = char.handle
                    target_service_uuid = service.uuid
                    logger.info(f"找到目标特征，handle: {target_char_handle}")
        
        if target_char_handle is None:
            logger.error("未找到目标特征")
            return
            
        # 订阅特征
        logger.info(f"正在订阅特征: handle={target_char_handle}")
        await client.start_notify(target_char_handle, notification_handler)
        
        # 保持脚本运行
        while True:
            await asyncio.sleep(1)
            
    except Exception as e:
        logger.error(f"连接或通信错误: {e}")
    finally:
        await client.disconnect()
        logger.info("已断开连接")

# 主函数
async def main():
    """主函数"""
   # 启动WebSocket服务器
    websocket_server = await websockets.serve(websocket_handler, "localhost", WEBSOCKET_PORT)
    logger.info(f"WebSocket服务器已启动: ws://localhost:{WEBSOCKET_PORT}")
    
    # 扫描并连接BLE设备
    ble_task = asyncio.create_task(scan_and_connect())
    
    # 保持服务器运行
    await asyncio.gather(websocket_server.wait_closed(), ble_task)

# 运行主函数
if __name__ == "__main__":
    asyncio.run(main())