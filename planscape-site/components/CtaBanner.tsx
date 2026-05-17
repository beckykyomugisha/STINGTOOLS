'use client';

import { motion } from 'framer-motion';

export default function CtaBanner() {
  return (
    <section
      className="relative overflow-hidden px-6 py-24"
      style={{
        background:
          'linear-gradient(135deg, #1A1F5E 0%, #2D3480 50%, #E5522A 100%)',
      }}
    >
      {/* Decorative circle */}
      <div
        aria-hidden
        className="pointer-events-none absolute right-[-200px] top-1/2 h-[600px] w-[600px] -translate-y-1/2 rounded-full border border-white/5"
      />
      <div
        aria-hidden
        className="pointer-events-none absolute right-[-100px] top-1/2 h-[400px] w-[400px] -translate-y-1/2 rounded-full border border-white/5"
      />

      <motion.div
        initial={{ opacity: 0, y: 32 }}
        whileInView={{ opacity: 1, y: 0 }}
        viewport={{ once: true, margin: '-80px' }}
        transition={{ duration: 0.6, ease: 'easeOut' }}
        className="relative mx-auto max-w-3xl text-center"
      >
        <h2 className="text-4xl font-extrabold leading-tight text-white">
          Ready to bring ISO 19650 to your projects?
        </h2>
        <p className="mx-auto mt-4 max-w-2xl text-lg text-white/70">
          Join AEC teams across three continents using Planscape to deliver
          better-coordinated, fully-compliant BIM projects.
        </p>

        <div className="mt-10 flex flex-wrap justify-center gap-4">
          <a
            href="#"
            className="rounded-xl bg-white px-8 py-4 text-base font-bold text-navy shadow-lg transition-colors hover:bg-slate-100"
          >
            Start Free Trial
          </a>
          <a
            href="#"
            className="rounded-xl border border-white/40 bg-white/10 px-8 py-4 text-base font-semibold text-white transition-colors hover:bg-white/20"
          >
            Request a Demo
          </a>
        </div>
      </motion.div>
    </section>
  );
}
