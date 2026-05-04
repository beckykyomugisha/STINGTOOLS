/** @type {import('next').NextConfig} */
//
// Phase 169 — the Next.js marketing site is built statically and copied
// into the .NET API's wwwroot/ root, served at:
//
//   http://<api-host>/      (marketing)
//   http://<api-host>/app/  (dashboard)
//
// The dashboard's HTML lives at wwwroot/app/index.html; its assets
// (css/, js/, viewer.html) stay at wwwroot root and don't conflict with
// Next.js's _next/* tree. basePath is empty so all marketing links work
// at the origin root.
//
const nextConfig = {
  output: 'export',
  basePath: process.env.NEXT_PUBLIC_BASE_PATH ?? '',
  images: { unoptimized: true },
  trailingSlash: true,
};

module.exports = nextConfig;
