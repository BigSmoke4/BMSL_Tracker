const User = require('../models/User');
const { Op } = require('sequelize');

// Keep track of socketId -> userId mapping for cleanup on disconnect
const socketToUser = new Map();

module.exports = (io, socket) => {
    console.log('User connected:', socket.id);

    // 1. Send initial locations of all users who have a recorded location
    const sendInitialLocations = async () => {
        try {
            const usersWithLocation = await User.findAll({
                where: {
                    lat: { [Op.ne]: null },
                    lng: { [Op.ne]: null }
                },
                attributes: ['id', 'username', 'lat', 'lng', 'isOnline', 'lastActive']
            });

            socket.emit('initialLocations', usersWithLocation.map(u => ({
                userId: u.id,
                username: u.username,
                lat: u.lat,
                lng: u.lng,
                isOnline: u.isOnline,
                lastActive: u.lastActive
            })));
        } catch (error) {
            console.error('Error fetching initial locations:', error);
        }
    };

    sendInitialLocations();

    // 2. Handle location update
    socket.on('updateLocation', async (data) => {
        const { userId, lat, lng } = data;
        if (!userId) return;

        // Map socket to user for disconnect handling
        socketToUser.set(socket.id, userId);

        try {
            await User.update(
                { lat, lng, isOnline: true, lastActive: new Date() },
                { where: { id: userId } }
            );

            const user = await User.findByPk(userId);
            if (!user) return;

            io.emit('locationUpdated', {
                userId,
                username: user.username,
                lat,
                lng,
                isOnline: true
            });
        } catch (error) {
            console.error('Error updating location:', error);
        }
    });

    socket.on('disconnect', async () => {
        const userId = socketToUser.get(socket.id);
        if (userId) {
            console.log(`User ${userId} disconnected`);
            try {
                await User.update({ isOnline: false }, { where: { id: userId } });
                io.emit('userOffline', userId);
            } catch (error) {
                console.error('Error marking user offline:', error);
            }
            socketToUser.delete(socket.id);
        } else {
            console.log('User disconnected:', socket.id);
        }
    });
};
