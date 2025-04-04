# app/api/api_v1/endpoints/auth.py
from datetime import timedelta
from typing import Any
from fastapi import APIRouter, Depends, HTTPException, status
from fastapi.security import OAuth2PasswordRequestForm
from sqlalchemy.orm import Session

from app.core.security import create_access_token, authenticate_user, get_password_hash
from app.core.config import settings
from app.db.session import get_db
from app.models.user import User
from app.schemas.user import UserCreate, UserResponse, Token

router = APIRouter()

@router.post("/login", response_model=Token)
def login(
    db: Session = Depends(get_db), 
    form_data: OAuth2PasswordRequestForm = Depends()
) -> Any:
    """
    OAuth2 compatible token login, get an access token for future requests
    """
    user = authenticate_user(db, form_data.username, form_data.password)
    if not user:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Incorrect username or password",
            headers={"WWW-Authenticate": "Bearer"},
        )
    access_token_expires = timedelta(minutes=settings.ACCESS_TOKEN_EXPIRE_MINUTES)
    access_token = create_access_token(
        subject=user.id, expires_delta=access_token_expires
    )
    return {"access_token": access_token, "token_type": "bearer"}

@router.post("/register", response_model=UserResponse)
def create_user(
    user_in: UserCreate, 
    db: Session = Depends(get_db)
) -> Any:
    """
    Create new user
    """
    # Check if user exists
    db_user_email = db.query(User).filter(User.email == user_in.email).first()
    if db_user_email:
        raise HTTPException(
            status_code=400,
            detail="Email already registered"
        )
    
    db_user_username = db.query(User).filter(User.username == user_in.username).first()
    if db_user_username:
        raise HTTPException(
            status_code=400,
            detail="Username already registered"
        )
    
    # Create user
    user = User(
        email=user_in.email,
        username=user_in.username,
        hashed_password=get_password_hash(user_in.password),
        is_active=True,
        is_superuser=False
    )
    db.add(user)
    db.commit()
    db.refresh(user)
    
    return UserResponse(
        id=user.id,
        email=user.email,
        username=user.username,
        is_active=user.is_active,
        is_superuser=user.is_superuser
    )

# app/api/api_v1/endpoints/captures.py
from typing import Any, List, Optional
from fastapi import APIRouter, Depends, HTTPException, BackgroundTasks, Query
from sqlalchemy.orm import Session

from app.db.session import get_db
from app.core.security import get_current_user
from app.models.user import User
from app.services.packet_capture import PacketCaptureService
from app.schemas.capture import (
    CaptureCreate, 
    CaptureResponse, 
    CaptureStatus,
    CaptureDetails,
    CaptureExport
)

router = APIRouter()

@router.post("/", response_model=CaptureResponse)
def start_capture(
    capture_in: CaptureCreate,
    background_tasks: BackgroundTasks,
    db: Session = Depends(get_db),
    current_user: User = Depends(get_current_user)
) -> Any:
    """
    Start a new packet capture session
    """
    # Initialize capture service
    capture_service = PacketCaptureService(db)
    
    # Start capture
    result = capture_service.start_capture(
        interface=capture_in.interface,
        filter_str=capture_in.filter,
        name=capture_in.name,
        description=capture_in.description,
        user_id=current_user.id
    )
    
    return {
        "id": result["capture_id"],
        "status": result["status"],
        "message": f"Capture started on {result['interface']}"
    }

@router.get("/", response_model=List[CaptureStatus])
def list_captures(
    db: Session = Depends(get_db),
    current_user: User = Depends(get_current_user),
    skip: int = 0, 
    limit: int = 100,
    active_only: bool = False
) -> Any:
    """
    List all capture sessions
    """
    capture_service = PacketCaptureService(db)
    
    # Get active captures first
    active_captures = capture_service.get_active_captures()
    
    # Convert to response format
    result = [
        CaptureStatus(
            id=capture["id"],
            name=capture["name"],
            interface=capture["interface"],
            status="active",
            start_time=capture["start_time"],
            packet_count=capture["packet_count"],
            bytes_captured=capture["bytes_captured"]
        )
        for capture in active_captures
    ]
    
    # If only active captures requested, return now
    if active_only:
        return result
    
    # Add completed captures from database
    query = db.query(Capture).filter(Capture.status != "active")
    if not current_user.is_superuser:
        # Filter to only show user's own captures if not admin
        query = query.filter(Capture.user_id == current_user.id)
    
    completed_captures = query.order_by(Capture.start_time.desc()).offset(skip).limit(limit).all()
    
    for capture in completed_captures:
        result.append(
            CaptureStatus(
                id=capture.id,
                name=capture.name,
                interface=capture.interface,
                status=capture.status,
                start_time=capture.start_time,
                end_time=capture.end_time,
                packet_count=capture.packet_count,
                bytes_captured=capture.bytes_captured
            )
        )
    
    return result

@router.get("/{capture_id}", response_model=CaptureDetails)
def get_capture(
    capture_id: int,
    db: Session = Depends(get_db),
    current_user: User = Depends(get_current_user)
) -> Any:
    """
    Get detailed information about a specific capture
    """
    # Get capture from database
    capture = db.query(Capture).filter(Capture.id == capture_id).first()
    if not capture:
        raise HTTPException(status_code=404, detail="Capture not found")
    
    # Check permissions
    if not current_user.is_superuser and capture.user_id != current_user.id:
        raise HTTPException(status_code=403, detail="Not enough permissions")
    
    # Get capture details
    capture_service = PacketCaptureService(db)
    details = capture_service.get_capture_details(capture_id)
    
    return details

@router.post("/{capture_id}/stop", response_model=CaptureResponse)
def stop_capture(
    capture_id: int,
    db: Session = Depends(get_db),
    current_user: User = Depends(get_current_user)
) -> Any:
    """
    Stop an active capture session
    """
    # Get capture from database
    capture = db.query(Capture).filter(Capture.id == capture_id).first()
    if not capture:
        raise HTTPException(status_code=404, detail="Capture not found")
    
    # Check permissions
    if not current_user.is_superuser and capture.user_id != current_user.id:
        raise HTTPException(status_code=403, detail="Not enough permissions")
    
    # Stop capture
    capture_service = PacketCaptureService(db)
    result = capture_service.stop_capture(capture_id)
    
    return {
        "id": capture_id,
        "status": result["status"],
        "message": "Capture stopped successfully"
    }

@router.post("/{capture_id}/export", response_model=CaptureExport)
def export_capture(
    capture_id: int,
    format: str = Query("pcap", description="Export format (pcap)"),
    db: Session = Depends(get_db),
    current_user: User = Depends(get_current_user)
) -> Any:
    """
    Export capture data to a file
    """
    # Get capture from database
    capture = db.query(Capture).filter(Capture.id == capture_id).first()
    if not capture:
        raise HTTPException(status_code=404, detail="Capture not found")
    
    # Check permissions
    if not current_user.is_superuser and capture.user_id != current_user.id:
        raise HTTPException(status_code=403, detail="Not enough permissions")
    
    # Export capture
    capture_service = PacketCaptureService(db)
    try:
        path = capture_service.export_capture(capture_id, format)
        
        return {
            "id": capture_id,
            "file_path": path,
            "format": format,
            "message": f"Capture exported to {path}"
        }
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Export failed: {str(e)}")

# app/api/api_v1/endpoints/packets.py
from typing import Any, List, Optional
from fastapi import APIRouter, Depends, HTTPException, Query
from sqlalchemy.orm import Session

from app.db.session import get_db
from app.core.security import get_current_user
from app.models.user import User
from app.models.packet import Packet
from app.models.capture import Capture
from app.schemas.packet import PacketResponse, PacketFilter

router = APIRouter()

@router.get("/{capture_id}", response_model=List[PacketResponse])
def get_packets(
    capture_id: int,
    db: Session = Depends(get_db),
    current_user: User = Depends(get_current_user),
    filter: PacketFilter = Depends(),
    skip: int = 0,
    limit: int = 100
) -> Any:
    """
    Get packets for a specific capture with filtering options
    """
    # Check if capture exists
    capture = db.query(Capture).filter(Capture.id == capture_id).first()
    if not capture:
        raise HTTPException(status_code=404, detail="Capture not found")
    
    # Check permissions
    if not current_user.is_superuser and capture.user_id != current_user.id:
        raise HTTPException(status_code=403, detail="Not enough permissions")
    
    # Build query with filters
    query = db.query(Packet).filter(Packet.capture_id == capture_id)
    
    # Apply filters
    if filter.protocol:
        query = query.filter(Packet.protocol == filter.protocol)
    
    if filter.source_ip:
        query = query.filter(Packet.source_ip.like(f"%{filter.source_ip}%"))
    
    if filter.destination_ip:
        query = query.filter(Packet.destination_ip.like(f"%{filter.destination_ip}%"))
    
    if filter.source_port is not None:
        query = query.filter(Packet.source_port == filter.source_port)
    
    if filter.destination_port is not None:
        query = query.filter(Packet.destination_port == filter.destination_port)
    
    if filter.min_size is not None:
        query = query.filter(Packet.size >= filter.min_size)
    
    if filter.max_size is not None:
        query = query.filter(Packet.size <= filter.max_size)
    
    if filter.start_time is not None:
        query = query.filter(Packet.timestamp >= filter.start_time)
    
    if filter.end_time is not None:
        query = query.filter(Packet.timestamp <= filter.end_time)
    
    # Order by timestamp
    query = query.order_by(Packet.timestamp)
    
    # Apply pagination
    total = query.count()
    packets = query.offset(skip).limit(limit).all()
    
    # Convert to response format
    result = []
    for packet in packets:
        result.append(
            PacketResponse(
                id=packet.id,
                capture_id=packet.capture_id,
                timestamp=packet.timestamp,
                source_ip=packet.source_ip,
                destination_ip=packet.destination_ip,
                source_port=packet.source_port,
                destination_port=packet.destination_port,
                protocol=packet.protocol,
                size=packet.size,
                ttl=packet.ttl,
                flags=packet.flags,
                header_data=packet.header_data,
                payload_data=packet.payload_data,
                flow_id=packet.flow_id,
                session_id=packet.session_id
            )
        )
    
    return result

# app/api/api_v1/endpoints/analysis.py
from typing import Any, List, Dict, Optional
from fastapi import APIRouter, Depends, HTTPException, Query
from sqlalchemy import func, desc
from sqlalchemy.orm import Session

from app.db.session import get_db
from app.core.security import get_current_user
from app.models.user import User
from app.models.packet import Packet
from app.models.flow import Flow
from app.models.capture import Capture
from app.schemas.analysis import (
    ProtocolDistribution,
    TrafficVolume,
    TopHosts,
    TopFlows,
    TimeSeriesPoint,
    GeoLocation
)

router = APIRouter()

@router.get("/{capture_id}/protocol-distribution", response_model=ProtocolDistribution)
def get_protocol_distribution(
    capture_id: int,
    db: Session = Depends(get_db),
    current_user: User = Depends(get_current_user)
) -> Any:
    """
    Get protocol distribution for a capture
    """
    # Check if capture exists
    capture = db.query(Capture).filter(Capture.id == capture_id).first()
    if not capture:
        raise HTTPException(status_code=404, detail="Capture not found")
    
    # Check permissions
    if not current_user.is_superuser and capture.user_id != current_user.id:
        raise HTTPException(status_code=403, detail="Not enough permissions")
    
    # Get protocol distribution
    protocols = db.query(
        Packet.protocol,
        func.count(Packet.id).label("count"),
        func.sum(Packet.size).label("bytes")
    ).filter(
        Packet.capture_id == capture_id
    ).group_by(