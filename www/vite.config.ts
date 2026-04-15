import path from "node:path";
import react from "@vitejs/plugin-react-swc";
import { defineConfig } from "vite";

// https://vitejs.dev/config/
export default defineConfig(({ mode }) => ({
  base: "/",
  server: {
    host: "::",
    port: 8081,
    proxy: {
      // Only proxy API routes, not static assets
      "/app": {
        target: process.env.VITE_API_URL || "http://localhost:5002",
        changeOrigin: true,
      },
      "/audit": {
        target: process.env.VITE_API_URL || "http://localhost:5002",
        changeOrigin: true,
      },
      "/dev": {
        target: process.env.VITE_API_URL || "http://localhost:5002",
        changeOrigin: true,
      },
      "/dashboard": {
        target: process.env.VITE_API_URL || "http://localhost:5002",
        changeOrigin: true,
      },
      "/folder": {
        target: process.env.VITE_API_URL || "http://localhost:5002",
        changeOrigin: true,
      },
      "/resource": {
        target: process.env.VITE_API_URL || "http://localhost:5002",
        changeOrigin: true,
      },
      "/space": {
        target: process.env.VITE_API_URL || "http://localhost:5002",
        changeOrigin: true,
      },
      "/trash": {
        target: process.env.VITE_API_URL || "http://localhost:5002",
        changeOrigin: true,
      },
      "/user": {
        target: process.env.VITE_API_URL || "http://localhost:5002",
        changeOrigin: true,
      },
      "/admin": {
        target: process.env.VITE_API_URL || "http://localhost:5002",
        changeOrigin: true,
      },
    },
  },
  plugins: [react(), mode === "development"].filter(Boolean),
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
}));
