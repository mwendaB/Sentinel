from sqlalchemy import Column, Integer, String, Float, DateTime, Boolean, BigInteger, ForeignKey, Index
from sqlalchemy.dialects.postgresql import JSONB
from sqlalchemy.orm import relationship
from sqlalchemy.sql import func
from app.db.session import Base

class Flow(Base):
    __tablename__ = "flows"

    id = Column(Integer, primary_key=True, index=True)
    capture_id = Column(Integer, ForeignKey("captures.id"), index=True)
    source_ip = Column(String, index=True)
    destination_ip = Column(String, index=True)
    source_port = Column(Integer, nullable=True)
    destination_port = Column(Integer, nullable=True)
    protocol = Column(String, index=True)
    start_time = Column(Float, index=True)
    end_time = Column(Float, index=True)
    packet_count = Column(Integer, default=0)
    bytes_sent = Column(BigInteger, default=0)
    bytes_received = Column(BigInteger, default=0)
    status = Column(String, default="active")  # active, closed, timeout
    metadata = Column(JSONB, default={})
    
    # For TCP flows
    initiated = Column(Boolean, default=False)
    completed = Column(Boolean, default=False)
    
    # Relationships
    capture = relationship("Capture", back_populates="flows")
    packets = relationship("Packet", back_populates="flow")
    
    # Timestamps
    created_at = Column(DateTime(timezone=True), server_default=func.now())
    updated_at = Column(DateTime(timezone=True), onupdate=func.now())
    
    # Indexes
    __table_args__ = (
        Index('ix_flows_src_dst_proto', 'source_ip', 'destination_ip', 'protocol'),
        Index('ix_flows_capture_start', 'capture_id', 'start_time'),
        Index('ix_flows_time_range', 'start_time', 'end_time'),
    )
    