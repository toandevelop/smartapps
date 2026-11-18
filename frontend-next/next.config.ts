import type { NextConfig } from "next";

const BACKEND_URL = process.env.NEXT_PUBLIC_API_ORIGIN || "http://localhost:5200";
const RUST_URL = process.env.NEXT_PUBLIC_RUST_ORIGIN || "http://localhost:5300";

const nextConfig: NextConfig = {
  // E2E builds run beside the developer's .next output without replacing it.
  distDir: process.env.SMARTAPPS_E2E === "1" ? ".next-e2e" : ".next",
  async rewrites() {
    return [
      {
        source: "/api/:path*",
        destination: `${BACKEND_URL}/api/:path*`,
      },
      {
        source: "/rust-api/:path*",
        destination: `${RUST_URL}/:path*`,
      },
    ];
  },
};

export default nextConfig;
