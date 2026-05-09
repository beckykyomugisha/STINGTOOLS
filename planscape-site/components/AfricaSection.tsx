'use client';

import { motion } from 'framer-motion';
import { CheckCircle2, ArrowRight } from 'lucide-react';
import { useState } from 'react';
import AfricaProjectMap from './AfricaProjectMap';

const cities = [
  { name: 'Kampala, Uganda', x: 218, y: 220 },
  { name: 'Nairobi, Kenya', x: 240, y: 232 },
  { name: 'Lagos, Nigeria', x: 95, y: 190 },
  { name: 'Accra, Ghana', x: 70, y: 198 },
  { name: 'Dar es Salaam, Tanzania', x: 248, y: 256 },
];

/**
 * Stylised, simplified outline of the African continent.
 * Hand-drawn path approximating the coastline within a 300x380 viewBox.
 */
const AFRICA_PATH =
  'M 150 30 C 175 28 198 32 218 38 L 240 50 C 255 62 268 78 275 96 L 280 118 C 282 138 278 156 270 172 L 268 190 C 272 206 278 222 280 240 L 278 260 C 272 282 260 304 244 322 L 224 340 C 205 354 184 360 165 358 L 145 354 C 128 350 116 342 108 330 L 96 312 C 88 296 84 278 80 260 L 74 240 C 68 224 60 210 52 196 L 44 178 C 38 162 36 146 38 130 L 42 110 C 50 92 64 78 82 68 L 102 58 C 118 48 135 38 150 30 Z';

export default function AfricaSection() {
  const [hovered, setHovered] = useState<string | null>(null);

  return (
    <section id="africa" className="bg-[#FEFAF6] px-6 py-24">
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

        {/* RIGHT — Africa map */}
        <motion.div
          initial={{ opacity: 0, scale: 0.95 }}
          whileInView={{ opacity: 1, scale: 1 }}
          viewport={{ once: true, margin: '-80px' }}
          transition={{ duration: 0.7, ease: 'easeOut' }}
          className="flex flex-col items-center"
        >
          {/* Live Mapbox map of Planscape projects (East + West Africa) when
              NEXT_PUBLIC_MAPBOX_TOKEN is configured. The component renders
              its own no-token fallback message; the SVG illustration below
              is kept for visual continuity on builds without a token. */}
          <AfricaProjectMap />

          {/* Decorative SVG outline kept as a secondary visual; the live map
              above is the source of truth when a token is configured. */}
          <div className="relative mt-8 hidden md:block" aria-hidden>
            <svg
              viewBox="0 0 300 380"
              className="h-auto w-full max-w-xs opacity-70"
              role="img"
              aria-label="Stylised outline of the African continent"
            >
              <path
                d={AFRICA_PATH}
                fill="#FFF3E8"
                stroke="#FF6B35"
                strokeWidth={2}
                strokeLinejoin="round"
              />

              {cities.map((c, i) => (
                <g
                  key={c.name}
                  onMouseEnter={() => setHovered(c.name)}
                  onMouseLeave={() => setHovered(null)}
                  className="cursor-pointer"
                >
                  {/* pulsing outer ring */}
                  <motion.circle
                    cx={c.x}
                    cy={c.y}
                    r={8}
                    fill="rgba(255, 107, 53, 0.3)"
                    animate={{ r: [8, 18, 8], opacity: [0.6, 0, 0.6] }}
                    transition={{
                      duration: 2,
                      repeat: Infinity,
                      delay: i * 0.4,
                      ease: 'easeInOut',
                    }}
                  />
                  {/* solid inner dot */}
                  <circle cx={c.x} cy={c.y} r={4} fill="#FF6B35" />

                  {/* tooltip */}
                  {hovered === c.name && (
                    <g>
                      <rect
                        x={c.x + 10}
                        y={c.y - 18}
                        width={c.name.length * 5.5 + 14}
                        height={20}
                        rx={4}
                        fill="#1A1F5E"
                      />
                      <text
                        x={c.x + 17}
                        y={c.y - 4}
                        fontSize={10}
                        fill="white"
                        fontWeight={500}
                      >
                        {c.name}
                      </text>
                    </g>
                  )}
                </g>
              ))}
            </svg>
          </div>
          <p className="mt-4 text-center text-xs italic text-slate-400">
            Actively supporting projects across East &amp; West Africa
          </p>
        </motion.div>
      </div>
    </section>
  );
}
