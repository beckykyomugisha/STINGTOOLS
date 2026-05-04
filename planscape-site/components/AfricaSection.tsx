'use client';

import { motion } from 'framer-motion';
import { CheckCircle2, ArrowRight } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import mapboxgl from 'mapbox-gl';
import 'mapbox-gl/dist/mapbox-gl.css';

type DemoProject = {
  name: string;
  city: string;
  country: string;
  lat: number;
  lng: number;
  status: 'Active' | 'Completed';
  compliance: number;
};

const demoProjects: DemoProject[] = [
  { name: 'New Hospital Wing',          city: 'Kampala',       country: 'Uganda',   lat:  0.3136, lng: 32.5811, status: 'Active',    compliance: 84 },
  { name: 'Airport Terminal Expansion', city: 'Entebbe',       country: 'Uganda',   lat:  0.0424, lng: 32.4430, status: 'Active',    compliance: 91 },
  { name: 'Port Infrastructure',        city: 'Lagos',         country: 'Nigeria',  lat:  6.4541, lng:  3.3947, status: 'Active',    compliance: 42 },
  { name: 'Mixed-Use Development',      city: 'Nairobi',       country: 'Kenya',    lat: -1.2921, lng: 36.8219, status: 'Completed', compliance: 98 },
  { name: 'Commercial Centre',          city: 'Accra',         country: 'Ghana',    lat:  5.6037, lng: -0.1870, status: 'Completed', compliance: 96 },
  { name: 'Water Treatment Plant',      city: 'Dar es Salaam', country: 'Tanzania', lat: -6.7924, lng: 39.2083, status: 'Active',    compliance: 67 },
];

// Resolution order: build-time env var → runtime placeholder. The placeholder
// keeps the static export working: when the user hasn't set a real token the
// section renders a friendly fallback panel instead of crashing.
const MAPBOX_TOKEN =
  process.env.NEXT_PUBLIC_MAPBOX_TOKEN ?? 'PLANSCAPE_MAPBOX_TOKEN';

function escapeHtml(s: string): string {
  return s.replace(/[&<>"']/g, (c) =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c] || c),
  );
}

function popupHtml(p: DemoProject): string {
  const dotClass = p.status === 'Completed' ? 'ind-completed' : 'ind-active';
  const dot      = p.status === 'Completed' ? '✓' : '●';
  return `
    <div class="popup-card">
      <h4>${escapeHtml(p.name)}</h4>
      <div class="loc">📍 ${escapeHtml(p.city)}, ${escapeHtml(p.country)}</div>
      <div class="row"><span>Status</span><span class="${dotClass}">${dot} ${p.status}</span></div>
      <div class="row"><span>Compliance</span><span>${p.compliance}%</span></div>
      <hr>
      <a href="#pricing" class="demo-link">Request a Demo →</a>
    </div>
  `;
}

export default function AfricaSection() {
  const mapContainer = useRef<HTMLDivElement | null>(null);
  const mapRef       = useRef<mapboxgl.Map | null>(null);
  const [tokenMissing, setTokenMissing] = useState(false);

  useEffect(() => {
    if (!mapContainer.current) return;
    if (!MAPBOX_TOKEN || MAPBOX_TOKEN === 'PLANSCAPE_MAPBOX_TOKEN') {
      setTokenMissing(true);
      return;
    }

    mapboxgl.accessToken = MAPBOX_TOKEN;
    const map = new mapboxgl.Map({
      container: mapContainer.current,
      style: 'mapbox://styles/mapbox/dark-v11',
      center: [22, 2],
      zoom: 2.8,
      interactive: true,
      attributionControl: false,
    });
    mapRef.current = map;

    // Don't hijack the page scroll — user must click the map first.
    map.scrollZoom.disable();
    const enableZoom = () => map.scrollZoom.enable();
    map.on('click', enableZoom);

    const markers: mapboxgl.Marker[] = [];

    demoProjects.forEach((p) => {
      const key = p.status === 'Completed' ? 'completed' : 'active';

      const el = document.createElement('div');
      el.className = `map-marker-mkt ${key}`;

      const popup = new mapboxgl.Popup({ offset: 18, closeButton: true }).setHTML(popupHtml(p));

      const marker = new mapboxgl.Marker({ element: el })
        .setLngLat([p.lng, p.lat])
        .setPopup(popup)
        .addTo(map);

      markers.push(marker);
    });

    return () => {
      markers.forEach((m) => m.remove());
      map.off('click', enableZoom);
      map.remove();
      mapRef.current = null;
    };
  }, []);

  return (
    <section id="africa" className="bg-[#FEFAF6] px-6 py-24">
      <style jsx global>{`
        .map-marker-mkt {
          position: relative;
          width: 14px;
          height: 14px;
          border-radius: 9999px;
          border: 2px solid #fff;
          box-shadow: 0 1px 4px rgba(0, 0, 0, 0.4);
          cursor: pointer;
        }
        .map-marker-mkt.active    { background: #FF6B35; }
        .map-marker-mkt.completed { background: #22C55E; }
        .map-marker-mkt::after {
          content: '';
          position: absolute;
          inset: -2px;
          border-radius: 9999px;
          background: inherit;
          opacity: 0.35;
          z-index: -1;
          animation: mkt-pulse 2s ease-out infinite;
        }
        @keyframes mkt-pulse {
          0%   { transform: scale(1);   opacity: 0.5; }
          80%  { transform: scale(2.6); opacity: 0;   }
          100% { transform: scale(2.6); opacity: 0;   }
        }
        .mapboxgl-popup-content {
          background: #1a1f5e !important;
          color: #fff !important;
          border-radius: 12px !important;
          padding: 16px !important;
          min-width: 240px !important;
          border: 1px solid rgba(255, 255, 255, 0.1) !important;
          box-shadow: 0 8px 32px rgba(0, 0, 0, 0.4) !important;
        }
        .mapboxgl-popup-tip { border-top-color: #1a1f5e !important; }
        .mapboxgl-popup-close-button { color: rgba(255, 255, 255, 0.6) !important; padding: 4px 8px !important; }
        .popup-card h4 { margin: 0 0 4px; font-size: 15px; color: #fff; font-weight: 700; }
        .popup-card .loc { font-size: 12px; color: rgba(255, 255, 255, 0.65); margin-bottom: 8px; }
        .popup-card .row { display: flex; justify-content: space-between; font-size: 12px; margin: 4px 0; color: rgba(255, 255, 255, 0.85); }
        .popup-card .row .ind-active    { color: #ff6b35; font-weight: 600; }
        .popup-card .row .ind-completed { color: #22c55e; font-weight: 600; }
        .popup-card hr { border: 0; border-top: 1px solid rgba(255, 255, 255, 0.12); margin: 10px 0; }
        .popup-card .demo-link { display: inline-block; color: #ff6b35; font-weight: 600; font-size: 13px; text-decoration: none; }
        .popup-card .demo-link:hover { text-decoration: underline; }
      `}</style>

      <div className="mx-auto grid max-w-7xl grid-cols-1 items-center gap-16 lg:grid-cols-2">
        {/* LEFT */}
        <motion.div
          initial={{ opacity: 0, y: 32 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true, margin: '-80px' }}
          transition={{ duration: 0.6, ease: 'easeOut' }}
        >
          <span className="text-sm font-semibold uppercase tracking-widest text-orange">
            Made for Africa's AEC sector
          </span>
          <h2 className="mt-3 text-4xl font-bold leading-tight text-navy">
            Global standards.
            <br />
            Built for East &amp; West Africa.
          </h2>

          <p className="mt-6 max-w-lg text-lg text-slate-600">
            Africa's construction pipeline is growing faster than any other
            region. Planscape is built by AEC practitioners who understand the
            realities of infrastructure delivery across East and West Africa —
            mixed teams, variable connectivity, and projects that must comply
            with both local standards and international donor requirements.
          </p>

          <p className="mt-4 max-w-lg text-lg text-slate-600">
            From hospital wings in Kampala to port infrastructure in Lagos,
            Planscape's offline-capable Revit plugin and cloud dashboard give
            your team the same ISO 19650 tooling used by UK Tier 1 contractors
            — at a price structure that works for African practices.
          </p>

          <ul className="mt-8 space-y-3">
            {[
              'Works offline — syncs when connectivity returns',
              'Supports mixed Revit 2025 / 2026 / 2027 teams',
              'Pricing in USD with Africa regional discount available',
              'Local support across GMT+1 to GMT+3 time zones',
              'Built to satisfy World Bank & AfDB BIM requirements',
            ].map((item) => (
              <li key={item} className="flex items-start gap-3 text-base text-slate-700">
                <CheckCircle2 size={20} className="mt-0.5 shrink-0 text-orange" />
                <span>{item}</span>
              </li>
            ))}
          </ul>

          <a
            href="#"
            className="mt-10 inline-flex items-center gap-1.5 text-base font-semibold text-orange hover:underline"
          >
            Talk to our Africa team
            <ArrowRight size={18} />
          </a>
        </motion.div>

        {/* RIGHT — Mapbox map */}
        <motion.div
          initial={{ opacity: 0, scale: 0.95 }}
          whileInView={{ opacity: 1, scale: 1 }}
          viewport={{ once: true, margin: '-80px' }}
          transition={{ duration: 0.7, ease: 'easeOut' }}
          className="flex flex-col"
        >
          <div
            className="relative h-[420px] w-full overflow-hidden rounded-2xl border border-orange/30 shadow-2xl"
            style={{ boxShadow: '0 20px 60px rgba(0,0,0,0.15)' }}
          >
            {tokenMissing ? (
              <div className="flex h-full w-full flex-col items-center justify-center bg-[#FFF3E8] p-6 text-center text-sm text-slate-600">
                <div className="mb-2 text-4xl">🗺</div>
                <strong className="mb-1 text-navy">Interactive map awaits a Mapbox token</strong>
                <span>
                  Set <code className="rounded bg-white/70 px-1 py-0.5 font-mono text-xs">NEXT_PUBLIC_MAPBOX_TOKEN</code>{' '}
                  before <code className="rounded bg-white/70 px-1 py-0.5 font-mono text-xs">npm run build</code>{' '}
                  with a free token from{' '}
                  <a className="text-orange underline" href="https://account.mapbox.com/access-tokens/" target="_blank" rel="noopener noreferrer">
                    mapbox.com
                  </a>
                  .
                </span>
              </div>
            ) : (
              <div ref={mapContainer} className="h-full w-full" />
            )}
          </div>

          <div className="mt-4 flex flex-wrap gap-2">
            <span className="inline-flex items-center gap-2 rounded-full bg-orange/10 px-3 py-1 text-sm font-semibold text-orange">
              🌍 6 active projects across 5 countries
            </span>
            <span className="inline-flex items-center gap-2 rounded-full bg-orange/10 px-3 py-1 text-sm font-semibold text-orange">
              🛰 3 continents served
            </span>
          </div>
        </motion.div>
      </div>
    </section>
  );
}
