import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
export default defineConfig({ plugins: [react()], server: { proxy: { '/api': process.env.PLAYBACK_VALIDATION_PORT === '5081' ? 'http://127.0.0.1:5081' : process.env.PLAYBACK_OFFLINE_TEST === 'yes' ? 'http://127.0.0.1:5079' : 'http://127.0.0.1:5078' } } })
