import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // The deploy bundle (scripts/bundle-data.mjs) is read with fs at runtime, so tracing cannot see it.
  outputFileTracingIncludes: {
    "/": ["./data/**/*"],
    "/**/*": ["./data/**/*"],
  },
};

export default nextConfig;
