'use client';

import { motion } from 'framer-motion';
import { PlayCircle, Check, ChevronDown } from 'lucide-react';
import { BrowserChrome, DashboardMockup } from '@/lib/mockScreenshots';

export default function HeroSection() {
  return (
    <section
      id="hero"
      className="hero-grid relative flex min-h-screen items-center overflow-hidden"
      style={{
        background:
          'linear-gradient(135deg, #0F1340 0%, #1A1F5E 60%, #2D3480 100%)',
      }}
    >
      <div className="mx-auto grid w-full max-w-7xl grid-cols-1 items-center gap-12 px-6 pb-20 pt-32 lg:grid-cols-2 lg:gap-8 lg:pt-28">
        {/* LEFT */}
        <motion.div
          initial={{ opacity: 0, y: 32 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.7, ease: 'easeOut' }}
          className="lg:pr-12"
        >
          <span className="inline-block rounded-full bg-orange/15 px-3 py-1 text-sm font-medium text-orange">
            ISO 19650 · BIM Coordination · AEC Platform
          </span>

          <h1 className="mt-4 text-4xl font-bold leading-tight text-white lg:text-5xl xl:text-6xl">
            BIM Coordination,
            <br />
            Built for{' '}
            <span className="gradient-text-orange">ISO&nbsp;19650</span>.
          </h1>

          <p className="mt-6 max-w-[520px] text-lg text-muted lg:text-xl">
            Planscape unifies asset tagging, document control, and site
            coordination for AEC teams — from design through to FM handover.
          </p>

          <div className="mt-10 flex flex-wrap gap-4">
            <motion.a
              href="#"
              whileHover={{ scale: 1.02 }}
              whileTap={{ scale: 0.98 }}
              className="rounded-xl bg-orange px-8 py-4 text-base font-semibold text-white shadow-orange-glow transition-colors hover:bg-orange-dark"
            >
              Start Free Trial
            </motion.a>
            <motion.a
              href="#"
              whileHover={{ scale: 1.02 }}
              whileTap={{ scale: 0.98 }}
              className="flex items-center gap-2 rounded-xl border border-white/30 bg-white/5 px-8 py-4 text-base font-semibold text-white transition-colors hover:border-white/50 hover:bg-white/10"
            >
              <PlayCircle size={20} className="fill-white text-navy" />
              Watch 90-sec Demo
            </motion.a>
          </div>

          <div className="mt-8 flex flex-wrap items-center gap-x-6 gap-y-2 text-sm text-muted">
            <span className="flex items-center gap-1.5">
              <Check size={14} className="text-orange" />
              No credit card required
            </span>
            <span className="flex items-center gap-1.5">
              <Check size={14} className="text-orange" />
              Set up in under 10 minutes
            </span>
          </div>
        </motion.div>

        {/* RIGHT — floating mockup */}
        <motion.div
          initial={{ opacity: 0, y: 32 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.8, delay: 0.2, ease: 'easeOut' }}
          className="relative"
        >
          <motion.div
            animate={{ y: [0, -12, 0] }}
            transition={{
              duration: 4,
              ease: 'easeInOut',
              repeat: Infinity,
              repeatType: 'mirror',
            }}
            style={{
              filter: 'drop-shadow(0 40px 80px rgba(255,107,53,0.15))',
            }}
            className="rotate-1"
          >
            <BrowserChrome>
              <DashboardMockup />
            </BrowserChrome>
          </motion.div>
        </motion.div>
      </div>

      {/* Scroll indicator */}
      <motion.a
        href="#metrics"
        animate={{ y: [0, 8, 0] }}
        transition={{ duration: 1.5, repeat: Infinity, ease: 'easeInOut' }}
        className="absolute bottom-8 left-1/2 z-10 hidden -translate-x-1/2 flex-col items-center gap-1 text-white/40 hover:text-white/70 md:flex"
      >
        <span className="text-xs">Scroll to explore</span>
        <ChevronDown size={20} />
      </motion.a>
    </section>
  );
}
