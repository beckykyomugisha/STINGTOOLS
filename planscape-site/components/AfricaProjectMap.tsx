'use client';

import { useEffect, useRef, useState } from 'react';

/**
 * Live project map for the Africa section.
 *
 * Renders the same 6 demo projects as the legacy marketing-site/index.html
 * Mapbox embed (Kampala, Entebbe, Lagos, Nairobi, Accra, Dar es Salaam)
 * but inside the unified Next.js app instead of the stranded static page.
 *
 * Mapbox GL JS is loaded from the CDN at runtime so the dependency is
 * zero-install — the static export ships with no extra bytes when the
 * token isn't set, and renders a graceful fallback message.
 *
 * Configure with `NEXT_PUBLIC_MAPBOX_TOKEN` in `.env.local` (or
 * `.env.production` for the deployed build). The token is public (it
 * goes in the browser bundle) so use the public scope from
 * mapbox.com → Account → Tokens.
 */

type Project = {
  name: string;
  city: string;
  country: string;
  lat: number;
  lng: number;
  status: 'Active' | 'Completed';
  compliance: number;
};

const PROJECTS: Project[] = [
  { name: 'New Hospital Wing',           city: 'Kampala',       country: 'Uganda',   lat:  0.3136, lng: 32.5811, status: 'Active',    compliance: 84 },
  { name: 'Airport Terminal Expansion',  city: 'Entebbe',       country: 'Uganda',   lat:  0.0424, lng: 32.4430, status: 'Active',    compliance: 91 },
  { name: 'Port Infrastructure Project', city: 'Lagos',         country: 'Nigeria',  lat:  6.4541, lng:  3.3947, status: 'Active',    compliance: 42 },
  { name: 'Mixed-Use Development',       city: 'Nairobi',       country: 'Kenya',    lat: -1.2921, lng: 36.8219, status: 'Completed', compliance: 98 },
  { name: 'Commercial Centre',           city: 'Accra',         country: 'Ghana',    lat:  5.6037, lng: -0.1870, status: 'Completed', compliance: 96 },
  { name: 'Water Treatment Plant',       city: 'Dar es Salaam', country: 'Tanzania', lat: -6.7924, lng: 39.2083, status: 'Active',    compliance: 67 },
];

const MAPBOX_VERSION = '3.3.0';

function loadMapbox(): Promise<any> {
  if (typeof window === 'undefined') return Promise.reject('SSR');
  if ((window as any).mapboxgl) return Promise.resolve((window as any).mapboxgl);

  const cssId = 'planscape-mapbox-css';
  if (!document.getElementById(cssId)) {
    const css = document.createElement('link');
    css.id = cssId;
    css.rel = 'stylesheet';
    css.href = `https://api.mapbox.com/mapbox-gl-js/v${MAPBOX_VERSION}/mapbox-gl.css`;
    document.head.appendChild(css);
  }

  return new Promise((resolve, reject) => {
    const scriptId = 'planscape-mapbox-js';
    const existing = document.getElementById(scriptId) as HTMLScriptElement | null;
    if (existing) {
      existing.addEventListener('load',  () => resolve((window as any).mapboxgl));
      existing.addEventListener('error', () => reject('mapbox-gl load failed'));
      if ((window as any).mapboxgl) resolve((window as any).mapboxgl);
      return;
    }
    const s = document.createElement('script');
    s.id = scriptId;
    s.src = `https://api.mapbox.com/mapbox-gl-js/v${MAPBOX_VERSION}/mapbox-gl.js`;
    s.onload = () => resolve((window as any).mapboxgl);
    s.onerror = () => reject('mapbox-gl load failed');
    document.head.appendChild(s);
  });
}

export default function AfricaProjectMap() {
  const containerRef = useRef<HTMLDivElement>(null);
  const [error, setError] = useState<string | null>(null);
  const token = process.env.NEXT_PUBLIC_MAPBOX_TOKEN || '';

  useEffect(() => {
    if (!token) return;
    if (!containerRef.current) return;
    let cancelled = false;
    let map: any;

    loadMapbox()
      .then((mapboxgl) => {
        if (cancelled) return;
        mapboxgl.accessToken = token;
        map = new mapboxgl.Map({
          container: containerRef.current,
          style: 'mapbox://styles/mapbox/light-v11',
          center: [22, 2],
          zoom: 2.6,
          attributionControl: false,
        });
        // Don't hijack page scroll until user clicks in.
        map.scrollZoom.disable();
        map.on('click', () => map.scrollZoom.enable());

        PROJECTS.forEach((p) => {
          const colour = p.status === 'Active' ? '#FF6B35' : '#1A1F5E';
          const el = document.createElement('div');
          el.style.cssText = `
            width: 18px; height: 18px; border-radius: 50%;
            background: ${colour}; border: 3px solid white;
            box-shadow: 0 0 0 4px ${colour}33; cursor: pointer;
          `;

          const dot = p.status === 'Active' ? '●' : '✓';
          const html = `
            <div style="font-family:system-ui,sans-serif;color:#1A1F5E;min-width:220px">
              <h4 style="margin:0 0 6px;font-size:14px;font-weight:700">${escapeHtml(p.name)}</h4>
              <div style="font-size:12px;opacity:0.7;margin-bottom:8px">📍 ${escapeHtml(p.city)}, ${escapeHtml(p.country)}</div>
              <div style="display:flex;justify-content:space-between;font-size:12px;margin:4px 0">
                <span>Status</span><span style="color:${colour};font-weight:600">${dot} ${escapeHtml(p.status)}</span>
              </div>
              <div style="display:flex;justify-content:space-between;font-size:12px;margin:4px 0">
                <span>Compliance</span><span style="font-weight:600">${p.compliance}%</span>
              </div>
              <a href="#pricing" style="display:inline-block;margin-top:8px;color:#FF6B35;font-weight:600;font-size:12px;text-decoration:none">
                Request a demo →
              </a>
            </div>`;

          new mapboxgl.Marker({ element: el })
            .setLngLat([p.lng, p.lat])
            .setPopup(new mapboxgl.Popup({ offset: 18, closeButton: true }).setHTML(html))
            .addTo(map);
        });
      })
      .catch((e) => {
        if (!cancelled) setError(typeof e === 'string' ? e : 'Mapbox failed to load');
      });

    return () => {
      cancelled = true;
      try { map?.remove(); } catch (_) {}
    };
  }, [token]);

  if (!token) {
    return (
      <div className="flex h-72 w-full max-w-md flex-col items-center justify-center rounded-xl border border-orange/20 bg-[#FFF3E8] px-6 py-8 text-center">
        <span className="text-3xl">🗺</span>
        <strong className="mt-2 text-navy">Live project map</strong>
        <p className="mt-1 text-sm text-slate-600">
          Set <code className="rounded bg-white/60 px-1 py-0.5 text-xs">NEXT_PUBLIC_MAPBOX_TOKEN</code>{' '}
          in <code className="rounded bg-white/60 px-1 py-0.5 text-xs">.env.local</code> to render
          the interactive map of Planscape projects across East &amp; West Africa.
        </p>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex h-72 w-full max-w-md flex-col items-center justify-center rounded-xl border border-red-200 bg-red-50 px-6 py-8 text-center text-sm text-red-700">
        <span>Map failed to load: {error}</span>
      </div>
    );
  }

  return (
    <div className="w-full max-w-md">
      <div
        ref={containerRef}
        className="h-72 w-full overflow-hidden rounded-xl border border-orange/20 shadow-md md:h-96"
        aria-label="Live map of Planscape projects across Africa"
      />
      <p className="mt-4 text-center text-xs italic text-slate-400">
        Click the map to enable zoom · Click a marker for project details
      </p>
    </div>
  );
}

function escapeHtml(s: string): string {
  return s.replace(/[&<>"']/g, (c) =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c] as string)
  );
}
