from pydantic import BaseModel
from typing import Dict, Any, Optional
import time

class PacketData(BaseModel):
    timestamp: float = time.time()
    source_ip: str
    destination_ip: str
    protocol: str
    source_port: Optional[int] = None
    destination_port: Optional[int] = None
    size: int
    info: Dict[str, Any] = {}

class NetworkInterface(BaseModel):
    name: str
    description: str
    ip_address: Optional[str] = None
    is_up: bool

class NetworkStats(BaseModel):
    bytes_sent: int
    bytes_received: int
    packets_sent: int
    packets_received: int
    error_in: int
    error_out: int
    drop_in: int
    drop_out: int

class CaptureStatus(BaseModel):
    status: str
    interface: Optional[str] = None
    packets_captured: Optional[int] = None