from sqlalchemy import Column, Integer, String, Float, DateTime, ForeignKey, Index, Text, Boolean, BigInteger
from sqlalchemy.dialects.postgresql import JSONB
from sqlalchemy.orm import relationship
from app.db.session import Base

class Packet(Base):
    __tablename__ = "packets"

    id = Column(BigInteger, primary_key=True, index=True)
    capture_id = Column(Integer, ForeignKey("captures.id"), index=True)
    timestamp = Column(Float, index=True, nullable=False)
    source_ip = Column(String, index=True)
    destination_ip = Column(String, index=True)
    source_port = Column(Integer, index=True, nullable=True)
    destination_port = Column(Integer, index=True, nullable=True)
    protocol = Column(String, index=True)
    size = Column(Integer)
    ttl = Column(Integer, nullable=True)
    flags = Column(String, nullable=True)
    header_data = Column(JSONB, default={})
    payload_data = Column(JSONB, default={})
    analyzed = Column(Boolean, default=False)
    flow_id = Column(Integer, ForeignKey("flows.id"), nullable=True, index=True)
    session_id = Column(Integer, ForeignKey("sessions.id"), nullable=True, index=True)
    created_at = Column(DateTime(timezone=True), server_default=func.now())

    # Relationships
    capture = relationship("Capture", back_populates="packets")
    flow = relationship("Flow", back_populates="packets")
    session = relationship("Session", back_populates="packets")

    # Indexes for common queries
    __table_args__ = (
        Index('ix_packets_time_proto', 'timestamp', 'protocol'),
        Index('ix_packets_src_dst', 'source_ip', 'destination_ip'),
        Index('ix_packets_capture_time', 'capture_id', 'timestamp'),
        Index('ix_packets_flow_time', 'flow_id', 'timestamp'),
    )