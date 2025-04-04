from sqlalchemy import Column, Integer, String, Float, DateTime, Boolean, ForeignKey, Index
from sqlalchemy.dialects.postgresql import JSONB, ARRAY
from sqlalchemy.orm import relationship
from sqlalchemy.sql import func
from app.db.session import Base

class Host(Base):
    __tablename__ = "hosts"

    id = Column(Integer, primary_key=True, index=True)
    ip_address = Column(String, index=True, unique=True)
    mac_address = Column(String, nullable=True)
    hostname = Column(String, nullable=True)
    os_info = Column(String, nullable=True)
    first_seen = Column(Float, index=True)
    last_seen = Column(Float, index=True)
    is_local = Column(Boolean, default=False)
    
    # Geolocation data
    country = Column(String, nullable=True)
    city = Column(String, nullable=True)
    latitude = Column(Float, nullable=True)
    longitude = Column(Float, nullable=True)
    asn = Column(String, nullable=True)
    organization = Column(String, nullable=True)
    
    # Service information
    open_ports = Column(ARRAY(Integer), default=[])
    services = Column(JSONB, default={})
    
    # Traffic statistics
    total_sent_bytes = Column(BigInteger, default=0)
    total_received_bytes = Column(BigInteger, default=0)
    total_sent_packets = Column(BigInteger, default=0)
    total_received_packets = Column(BigInteger, default=0)
    
    # Additional metadata
    tags = Column(ARRAY(String), default=[])
    notes = Column(Text, nullable=True)
    metadata = Column(JSONB, default={})
    
    # Timestamps
    created_at = Column(DateTime(timezone=True), server_default=func.now())
    updated_at = Column(DateTime(timezone=True), onupdate=func.now())