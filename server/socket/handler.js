const User = require('../models/User');

module.exports = (io, socket) => {
    console.log('User connected:', socket.id);

    // When a user logs in via socket, we can store their socket ID mapped to user ID if needed.
    // For now, we rely on the client sending their user ID with updates.

    // Handle location update
    socket.on('updateLocation', async (data) => {
        // data: { userId, lat, lng }
        const { userId, lat, lng } = data;

        if (!userId) return;

        try {
            // Update DB
            await User.update(
                { lat, lng, isOnline: true, lastActive: new Date() },
                { where: { id: userId } }
            );

            // Broadcast to everyone (including sender, or exclude sender with socket.broadcast)
            // We want to send the Updated User Info to everyone
            io.emit('locationUpdated', { userId, lat, lng });

        } catch (error) {
            console.error('Error updating location:', error);
        }
    });

    socket.on('disconnect', () => {
        console.log('User disconnected:', socket.id);
        // Ideally we'd mark them offline, but we don't track socket<->userId map in memory here.
        // Client can emit a 'logout' event explicitly.
    });
};
