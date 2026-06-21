# Planscape Web

Browser app for online BIM coordination, built on the existing Planscape Server API.
Lives alongside the marketing site (`planscape-site/`); this is the **authenticated
coordination app** (the rich client that was previously mobile-only).

## Status — slice 1: foundation + auth
- Next.js 14 (App Router) + TypeScript + Tailwind shell
- Sign in against `POST /api/auth/login`; bearer token in `localStorage`
- Protected `/dashboard`; auto-redirect based on session

Coming next: projects & issues → clashes & 3D viewer → real-time (SignalR).

## Run locally
```bash
cd planscape-web
npm install
cp .env.example .env.local   # set NEXT_PUBLIC_API_BASE
npm run dev                  # http://localhost:3000
```

## Config
`NEXT_PUBLIC_API_BASE` — Planscape API base URL (default `https://api.planscape.build`).

> CORS: this app uses bearer-token auth, so the API must allow this app's origin.
> `app.planscape.build` is already in the server's `Cors__Origins`. For localhost
> dev, add `http://localhost:3000` to the server's CORS allow-list or run the API locally.

## Checks
```bash
npm run typecheck   # tsc --noEmit
npm run build       # next build
```
