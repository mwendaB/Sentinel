import asyncio
import time
from typing import List, Dict, Any, Callable, Optional
from scapy.all import sniff, IP, TCP, UDP, ICMP
from ..models.packet import PacketData

class PacketCaptureService:
    def __init__(self):
        self.active_capture = False
        self.captured_packets: List[PacketData] = []
        self.packet_callbacks: List[Callable[[PacketData], None]] = []
    
    def start_capture(self, interface: Optional[str] = None) -> Dict[str, Any]:
        """Start packet capture on the specified interface."""
        if self.active_capture:
            return {"status": "Capture already running"}
        
        self.active_capture = True
        self.captured_packets = []
        

        asyncio.create_task(self._capture_packets(interface))
        
        return {
            "status": "Capture started", 
            "interface": interface or "default"
        }
    
    def stop_capture(self) -> Dict[str, Any]:
        """Stop the active packet capture."""
        if not self.active_capture:
            return {"status": "No capture running"}
        
        self.active_capture = False
        return {
            "status": "Capture stopped", 
            "packets_captured": len(self.captured_packets)
        }
    
    def get_packets(self, limit: int = 100, offset: int = 0) -> List[PacketData]:
        """Get a subset of captured packets."""
        end_idx = min(offset + limit, len(self.captured_packets))
        return self.captured_packets[offset:end_idx]
    
    def register_callback(self, callback: Callable[[PacketData], None]) -> None:
        """Register a callback for when a new packet is captured."""
        self.packet_callbacks.append(callback)
    
    def unregister_callback(self, callback: Callable[[PacketData], None]) -> None:
        """Unregister a packet callback."""
        if callback in self.packet_callbacks:
            self.packet_callbacks.remove(callback)
    
    async def _capture_packets(self, interface: Optional[str] = None) -> None:
        """Background task to capture packets."""
        def packet_callback(packet):
            if not self.active_capture:
                return
                
            try:
                packet_data = self._extract_packet_info(packet)
                self.captured_packets.append(packet_data)
                
                for callback in self.packet_callbacks:
                    callback(packet_data)
            except Exception as e:
                print(f"Error processing packet: {e}")
        
        while self.active_capture:
            sniff(iface=interface, prn=packet_callback, store=0, count=10)
            await asyncio.sleep(0.1)  
    
    def _extract_packet_info(self, packet) -> PacketData:
        """Extract relevant information from a captured packet."""
        packet_info = {
            "timestamp": time.time(),
            "source_ip": "",
            "destination_ip": "",
            "protocol": "Unknown",
            "size": len(packet),
            "info": {}
        }
        
        if IP in packet:
            packet_info["source_ip"] = packet[IP].src
            packet_info["destination_ip"] = packet[IP].dst
            
            if TCP in packet:
                packet_info["protocol"] = "TCP"
                packet_info["source_port"] = packet[TCP].sport
                packet_info["destination_port"] = packet[TCP].dport
                packet_info["info"]["flags"] = str(packet[TCP].flags)
                
            elif UDP in packet:
                packet_info["protocol"] = "UDP"
                packet_info["source_port"] = packet[UDP].sport
                packet_info["destination_port"] = packet[UDP].dport
                
            elif ICMP in packet:
                packet_info["protocol"] = "ICMP"
                packet_info["info"]["type"] = packet[ICMP].type
                packet_info["info"]["code"] = packet[ICMP].code
                
            else:
                packet_info["protocol"] = "Other"
        
        return PacketData(**packet_info)