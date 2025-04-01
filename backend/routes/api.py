from fastapi import APIRouter, WebSocket, Depends, HTTPException, BackgroundTasks
from typing import List, Dict, Any, Optional, Set
import socket
import asyncio
import psutil
from ..models.packet import PacketData, NetworkInterface, NetworkStats, CaptureStatus
from ..services.packet_capture import PacketCaptureService

router = APIRouter()
packet_capture_service = PacketCaptureService()
connected_websockets: Set[WebSocket] = set()


def ws_packet_callback(packet: PacketData):
    """Send packet data to all connected WebSocket clients."""
    if connected_websockets:

        packet_json = packet.dict()
        
  
        asyncio.create_task(broadcast_packet(packet_json))


packet_capture_service.register_callback(ws_packet_callback)

async def broadcast_packet(packet_data: Dict[str, Any]):
    """Broadcast packet data to all connected WebSocket clients."""
    for websocket in connected_websockets:
        try:
            await websocket.send_json(packet_data)
        except Exception:
       
            pass

@router.get("/interfaces", response_model=List[NetworkInterface])
async def get_network_interfaces():
    """Get a list of available network interfaces."""
    interfaces = []
    for iface, addrs in psutil.net_if_addrs().items():
        ip_address = None
        for addr in addrs:
            if addr.family == socket.AF_INET:
                ip_address = addr.address
                break
        
        is_up = psutil.net_if_stats().get(iface, None)
        is_up = is_up.isup if is_up else False
        
        interfaces.append(NetworkInterface(
            name=iface,
            description=iface,  
            ip_address=ip_address,
            is_up=is_up
        ))
    
    return interfaces

@router.get("/stats/{interface}", response_model=NetworkStats)
async def get_interface_stats(interface: str):
    """Get traffic statistics for a specific network interface."""
    try:
        stats = psutil.net_io_counters(pernic=True).get(interface)
        if not stats:
            raise HTTPException(status_code=404, detail=f"Interface {interface} not found")
        
        return NetworkStats(
            bytes_sent=stats.bytes_sent,
            bytes_received=stats.bytes_recv,
            packets_sent=stats.packets_sent,
            packets_received=stats.packets_recv,
            error_in=stats.errin,
            error_out=stats.errout,
            drop_in=stats.dropin,
            drop_out=stats.dropout
        )
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@router.post("/capture/start", response_model=CaptureStatus)
async def start_capture(interface: Optional[str] = None):
    """Start capturing packets on the specified interface."""
    return packet_capture_service.start_capture(interface)

@router.post("/capture/stop", response_model=CaptureStatus)
async def stop_capture():
    """Stop the active packet capture."""
    return packet_capture_service.stop_capture()

@router.get("/packets", response_model=List[PacketData])
async def get_packets(limit: int = 100, offset: int = 0):
    """Get a paginated list of captured packets."""
    return packet_capture_service.get_packets(limit, offset)

@router.websocket("/ws")
async def websocket_endpoint(websocket: WebSocket):
    """WebSocket endpoint for real-time packet updates."""
    await websocket.accept()
    connected_websockets.add(websocket)
    
    try:
        while True:
            
            await websocket.receive_text()
    except Exception:
 
        pass
    finally:
        connected_websockets.remove(websocket)