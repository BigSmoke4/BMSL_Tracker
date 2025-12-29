import { useState, useEffect, useRef } from 'react';
import { MapContainer, TileLayer, Marker, Popup } from 'react-leaflet';
import L from 'leaflet';
import io from 'socket.io-client';

// Fix for default marker icon in Leaflet with Vite/Webpack
delete L.Icon.Default.prototype._getIconUrl;
L.Icon.Default.mergeOptions({
    iconRetinaUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon-2x.png',
    iconUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon.png',
    shadowUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png',
});

const MapHandler = ({ user, onLogout }) => {
    const [position, setPosition] = useState([23.8103, 90.4125]); // Default Dhaka [lat, lng]
    const [users, setUsers] = useState({}); // Other users: { userId: { lat, lng, userId } }
    const [error, setError] = useState('');
    const socketRef = useRef();

    // 1. Setup Socket
    useEffect(() => {
        socketRef.current = io('http://localhost:3000');

        socketRef.current.on('locationUpdated', (data) => {
            // Update users state
            setUsers(prev => ({
                ...prev,
                [data.userId]: data
            }));
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
                console.error(err);
                setError('Unable to retrieve your location');
            },
            {
                enableHighAccuracy: true,
                timeout: 5000,
                maximumAge: 0
            }
        );

        return () => navigator.geolocation.clearWatch(watchId);
    }, [user]);

    const activeUsers = Object.values(users);

    return (
        <div className="map-wrapper">
            <div className="header">
                <span>Logged in as: <b>{user.username}</b></span>
                <button onClick={onLogout}>Logout</button>
            </div>

            {error && <div className="error-banner">{error}</div>}

            <div style={{ height: 'calc(100vh - 60px)', width: '100%' }}>
                <MapContainer center={position} zoom={13} scrollWheelZoom={true} style={{ height: '100%', width: '100%' }}>
                    <TileLayer
                        attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
                        url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
                    />

                    {/* My Marker */}
                    {position && (
                        <Marker position={position}>
                            <Popup>
                                You ({user.username})
                            </Popup>
                        </Marker>
                    )}

                    {/* Other Users */}
                    {activeUsers.map(u => (
                        u.userId !== user.id && (
                            <Marker key={u.userId} position={[u.lat, u.lng]}>
                                <Popup>
                                    User {u.userId}
                                </Popup>
                            </Marker>
                        )
                    ))}
                </MapContainer>
            </div>
        </div>
    );
};

export default MapHandler;
