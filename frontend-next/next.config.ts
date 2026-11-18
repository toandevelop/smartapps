import type { NextConfig } from "next";

const BACKEND_URL = process.env.NEXT_PUBLIC_API_ORIGIN || "http://localhost:5200";

const nextConfig: NextConfig = {
  // E2E builds run beside the developer's .next output without replacing it.
  distDir: process.env.SMARTAPPS_E2E === "1" ? ".next-e2e" : ".next",
  async rewrites() {
    return [
      {
        source: "/api/:path*",
        destination: `${BACKEND_URL}/api/:path*`,
      },
    ];
  },
};

export default nextConfig;
