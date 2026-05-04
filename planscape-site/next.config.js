/** @type {import('next').NextConfig} */
//
// Phase 169 — the Next.js marketing site is built statically (`output: 'export'`)
// and the resulting `out/` folder is copied into the .NET API's
// `wwwroot/welcome/` so it's served at:
//
//   http://<api-host>/welcome/
//
// `basePath` rewrites every internal link + `_next/*` asset URL to live
// under that prefix. `assetPrefix` keeps fonts/images on the same prefix.
// `BASE_PATH` env var lets local devs run `npm run dev` at the root without
// the prefix (otherwise next/dev would be unreachable on localhost:3000).
//
const basePath = process.env.NEXT_PUBLIC_BASE_PATH ?? '/welcome';

const nextConfig = {
  output: 'export',
  basePath,
  assetPrefix: basePath,
  images: { unoptimized: true },
  trailingSlash: true,
  env: {
    NEXT_PUBLIC_BASE_PATH: basePath,
  },
};

module.exports = nextConfig;
