import { useState, useEffect, useRef } from 'react';
import { MapContainer, TileLayer, Marker, Popup } from 'react-leaflet';
import L from 'leaflet';
import io from 'socket.io-client';

const MapHandler = ({ user, onLogout }) => {
    const [position, setPosition] = useState([23.8103, 90.4125]); // Default Dhaka [lat, lng]
    const [users, setUsers] = useState({}); // All users: { userId: { lat, lng, username, isOnline, lastActive } }
    const [error, setError] = useState('');
    const [retryCount, setRetryCount] = useState(0);
    const [hasInitialPosition, setHasInitialPosition] = useState(false);
    const socketRef = useRef();
    const mapRef = useRef();

    // 1. Setup Socket
    useEffect(() => {
        socketRef.current = io('http://localhost:3000');

        socketRef.current.on('initialLocations', (locations) => {
            const usersMap = {};
            locations.forEach(loc => {
                usersMap[loc.userId] = loc;
            });
            setUsers(usersMap);
        });

        socketRef.current.on('locationUpdated', (data) => {
            setUsers(prev => ({
                ...prev,
                [data.userId]: data
            }));
        });

        socketRef.current.on('userOffline', (userId) => {
            setUsers(prev => {
                if (!prev[userId]) return prev;
                return {
                    ...prev,
                    [userId]: { ...prev[userId], isOnline: false }
                };
            });
        });

        return () => {
            socketRef.current.disconnect();
        };
    }, []);

    // 2. Get Location & Emit
    useEffect(() => {
        if (!navigator.geolocation) {
            setError('Geolocation is not supported by your browser');
            return;
        }

        const watchId = navigator.geolocation.watchPosition(
            (pos) => {
                const { latitude, longitude } = pos.coords;
                setPosition([latitude, longitude]);
                setError('');

                if (!hasInitialPosition) {
                    setHasInitialPosition(true);
                    if (mapRef.current) {
                        mapRef.current.setView([latitude, longitude], 15);
                    }
                }

                // Emit to server
                if (socketRef.current && user) {
                    socketRef.current.emit('updateLocation', {
                        userId: user.id,
                        lat: latitude,
                        lng: longitude
                    });
                }
            },
            (err) => {
                console.error('Geolocation error:', err);
                if (err.code === err.TIMEOUT) {
                    if (retryCount === 0) {
                        setError('Optimizing location accuracy... (Snapchat style!)');
                        setRetryCount(1);
                    } else {
                        setError('Connection slow. Still trying to find you...');
                    }
                } else if (err.code === err.PERMISSION_DENIED) {
                    setError('Location permission denied. Please allow it in settings.');
                }
            },
            {
                enableHighAccuracy: retryCount === 0,
                timeout: retryCount === 0 ? 5000 : 15000,
                maximumAge: 30000
            }
        );

        return () => navigator.geolocation.clearWatch(watchId);
    }, [user, retryCount, hasInitialPosition]);

    const handleRecenter = () => {
        if (mapRef.current && position) {
            mapRef.current.flyTo(position, 16, { animate: true, duration: 1.5 });
        }
    };

    // Custom Marker Creator
    const getShortName = (name) => {
        if (!name) return '??';
        const parts = name.split(/[\s_-]+/).filter(Boolean);
        if (parts.length >= 2) {
            return (parts[0][0] + parts[1][0]).toUpperCase();
        }
        return name.substring(0, 2).toUpperCase();
    };

    const createCustomIcon = (username, isOnline) => {
        return L.divIcon({
            className: 'custom-div-icon',
            html: `
                <div class="marker-wrapper ${isOnline ? 'online' : 'offline'}">
                    <div class="marker-pin"></div>
                    <div class="marker-label">${getShortName(username)}</div>
                    <div class="marker-name">${username}</div>
                </div>
            `,
            iconSize: [40, 40],
            iconAnchor: [20, 20]
        });
    };

    const activeUsers = Object.values(users);

    return (
        <div className="map-wrapper">
            <div className="header">
                <span>Logged in as: <b>{user.username}</b></span>
                <div className="header-btns">
                    <button className="recenter-btn" onClick={handleRecenter}>My Location</button>
                    <button className="logout-btn" onClick={onLogout}>Logout</button>
                </div>
            </div>

            {error && (
                <div className="error-banner">
                    {error}
                    <button className="retry-btn" onClick={() => { setError(''); setRetryCount(0); }}>
                        Retry
                    </button>
                </div>
            )}

            <div style={{ height: '100vh', width: '100%', position: 'relative' }}>
                <MapContainer
                    center={position}
                    zoom={13}
                    scrollWheelZoom={true}
                    style={{ height: '100%', width: '100%', background: '#f5f5f7' }}
                    zoomControl={false}
                    ref={mapRef}
                >
                    <TileLayer
                        attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
                        url="https://{s}.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}{r}.png"
                    />

                    {/* My Marker */}
                    {position && (
                        <Marker position={position} icon={createCustomIcon(user.username, true)}>
                            <Popup>
                                <div className="popup-card">
                                    <div className="popup-header">
                                        <span className="online-dot">●</span>
                                        <b>You ({user.username})</b>
                                    </div>
                                    <p className="popup-status">You're live on the map!</p>
                                </div>
                            </Popup>
                        </Marker>
                    )}

                    {/* Other Users */}
                    {activeUsers.map(u => (
                        u.userId !== user.id && (
                            <Marker
                                key={u.userId}
                                position={[u.lat, u.lng]}
                                icon={createCustomIcon(u.username, u.isOnline)}
                            >
                                <Popup>
                                    <div className="popup-card">
                                        <div className="popup-header">
                                            <span className={u.isOnline ? 'online-dot' : 'offline-dot'}>●</span>
                                            <b>{u.username}</b>
                                        </div>
                                        {u.isOnline ? (
                                            <p className="status-live">Currently live</p>
                                        ) : (
                                            <p className="status-offline">
                                                Active {new Date(u.lastActive).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                                            </p>
                                        )}
                                    </div>
                                </Popup>
                            </Marker>
                        )
                    ))}
                </MapContainer>
            </div>

            <style>{`
                .marker-wrapper {
                    position: relative;
                    width: 40px;
                    height: 40px;
                    display: flex;
                    justify-content: center;
                    align-items: center;
                    transition: all 0.3s cubic-bezier(0.175, 0.885, 0.32, 1.275);
                }
                .marker-wrapper:hover {
                    transform: scale(1.3);
                    z-index: 1000 !important;
                }
                .marker-pin {
                    width: 34px;
                    height: 34px;
                    border-radius: 50% 50% 50% 0;
                    background: var(--accent-secondary);
                    position: absolute;
                    transform: rotate(-45deg);
                    border: 2px solid white;
                    box-shadow: 0 4px 15px rgba(0,0,0,0.4);
                    transition: all 0.3s;
                }
                .marker-wrapper.online .marker-pin {
                    background: var(--accent-online);
                    animation: pulseMarker 2s infinite;
                }
                .marker-wrapper.offline .marker-pin {
                    background: #555;
                    filter: grayscale(1);
                    opacity: 0.7;
                }
                @keyframes pulseMarker {
                    0% { box-shadow: 0 0 0 0 rgba(0, 255, 136, 0.6); }
                    70% { box-shadow: 0 0 0 15px rgba(0, 255, 136, 0); }
                    100% { box-shadow: 0 0 0 0 rgba(0, 255, 136, 0); }
                }
                .marker-label {
                    position: relative;
                    color: white;
                    font-weight: 800;
                    font-size: 14px;
                    z-index: 1;
                    font-family: 'Outfit';
                    text-shadow: 0 1px 2px rgba(0,0,0,0.5);
                }
                .marker-name {
                    position: absolute;
                    top: -30px;
                    background: var(--glass-bg);
                    backdrop-filter: blur(8px);
                    color: white;
                    padding: 4px 10px;
                    border-radius: 10px;
                    font-size: 12px;
                    white-space: nowrap;
                    font-weight: 600;
                    opacity: 0;
                    transition: all 0.2s;
                    border: 1px solid var(--glass-border);
                    box-shadow: 0 4px 15px rgba(0,0,0,0.3);
                    pointer-events: none;
                }
                .marker-wrapper:hover .marker-name {
                    opacity: 1;
                    top: -40px;
                }
                .popup-card { min-width: 140px; font-family: 'Outfit'; }
                .popup-header { display: flex; align-items: center; gap: 8px; margin-bottom: 5px; }
                .online-dot { color: var(--accent-online); font-size: 18px; }
                .offline-dot { color: #888; font-size: 18px; }
                .popup-status { margin: 0; font-size: 13px; color: var(--text-sub); }
                .status-live { color: var(--accent-online); margin: 0; font-weight: 600; font-size: 12px; text-transform: uppercase; letter-spacing: 0.5px; }
                .status-offline { color: var(--text-sub); margin: 0; font-size: 12px; }
            `}</style>
        </div>
    );
};

export default MapHandler;
