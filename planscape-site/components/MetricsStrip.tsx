'use client';

import { motion, useInView, useMotionValue, useTransform, animate } from 'framer-motion';
import { useEffect, useRef } from 'react';

function CountUp({
  to,
  suffix = '',
  prefix = '',
}: {
  to: number;
  suffix?: string;
  prefix?: string;
}) {
  const ref = useRef<HTMLSpanElement>(null);
  const inView = useInView(ref, { once: true, margin: '-50px' });
  const count = useMotionValue(0);
  const rounded = useTransform(count, (v) => `${prefix}${Math.round(v).toLocaleString()}${suffix}`);

  useEffect(() => {
    if (inView) {
      const controls = animate(count, to, { duration: 2, ease: 'easeOut' });
      return controls.stop;
    }
  }, [inView, count, to]);

  return <motion.span ref={ref}>{rounded}</motion.span>;
}

const metrics = [
  { value: 2555, suffix: '+', label: 'Pre-configured Parameters' },
  { value: 763, suffix: '', label: 'Plugin Commands' },
  { value: 40, suffix: '', label: 'Corporate Drawing Types' },
];

export default function MetricsStrip() {
  return (
    <section
      id="metrics"
      className="border-y border-slate-dark bg-white py-12"
    >
      <div className="mx-auto max-w-7xl px-6">
        <div className="flex flex-col items-center justify-center gap-12 md:flex-row md:gap-16">
          {metrics.map((m) => (
            <div key={m.label} className="text-center">
              <div className="text-5xl font-extrabold text-navy">
                <CountUp to={m.value} suffix={m.suffix} />
              </div>
              <div className="mt-2 text-sm font-medium uppercase tracking-wider text-muted">
                {m.label}
              </div>
            </div>
          ))}
        </div>

        <div className="mt-12 border-t border-slate-200 pt-8">
          <p className="text-center text-sm italic text-muted">
            Trusted by AEC teams across three continents
          </p>
          <div className="mt-6 flex flex-wrap items-center justify-center gap-6 sm:gap-8">
            {Array.from({ length: 6 }).map((_, i) => (
              <div
                key={i}
                className="flex h-10 w-28 items-center justify-center rounded-lg bg-slate-100 text-[10px] font-semibold tracking-widest text-slate-400"
              >
                CLIENT LOGO
              </div>
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}
