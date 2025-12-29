import { useState } from 'react';
import axios from 'axios';

const Login = ({ onLogin }) => {
    const [isRegister, setIsRegister] = useState(false);
    const [username, setUsername] = useState('');
    const [password, setPassword] = useState('');
    const [error, setError] = useState('');

    // Hardcoded API URL for now, or use import.meta.env
    const API_URL = 'http://localhost:3000/api/auth';

    const handleSubmit = async (e) => {
        e.preventDefault();
        setError('');

        try {
            const endpoint = isRegister ? '/register' : '/login';
            const res = await axios.post(`${API_URL}${endpoint}`, { username, password });

            if (isRegister) {
                // Auto switch to login or just login immediately?
                // Let's just ask them to login after register for simplicity
                setIsRegister(false);
                alert('Registration successful! Please login.');
            } else {
                onLogin(res.data.token, res.data.user);
            }
        } catch (err) {
            console.error('Auth Error:', err);
            const msg = err.response?.data?.message || err.message || 'An error occurred';
            setError(msg);
        }
    };

    return (
        <div className="login-container">
            <div className="login-box">
                <h2>{isRegister ? 'Register' : 'Login'}</h2>
                <form onSubmit={handleSubmit}>
                    <input
                        type="text"
                        placeholder="Username"
                        value={username}
                        onChange={e => setUsername(e.target.value)}
                        required
                    />
                    <input
                        type="password"
                        placeholder="Password"
                        value={password}
                        onChange={e => setPassword(e.target.value)}
                        required
                    />
                    {error && <p className="error">{error}</p>}
                    <button type="submit">{isRegister ? 'Register' : 'Login'}</button>
                </form>
                <p onClick={() => setIsRegister(!isRegister)}>
                    {isRegister ? 'Already have an account? Login' : "Don't have an account? Register"}
                </p>
            </div>
        </div>
    );
};

export default Login;
