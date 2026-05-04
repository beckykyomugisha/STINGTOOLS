'use client';

import { motion } from 'framer-motion';
import {
  Shield,
  BookOpen,
  Zap,
  Layers,
  Building2,
  Globe,
  GitBranch,
  Ruler,
  type LucideIcon,
} from 'lucide-react';

type Badge = { Icon: LucideIcon; title: string; subtitle: string };

const badges: Badge[] = [
  { Icon: Shield, title: 'ISO 19650', subtitle: 'Information management for BIM' },
  { Icon: BookOpen, title: 'Uniclass 2015', subtitle: 'Classification framework for AEC' },
  { Icon: Zap, title: 'BS 7671', subtitle: 'Electrical installation standards' },
  { Icon: Layers, title: 'CIBSE Guidelines', subtitle: 'Building services engineering' },
  { Icon: Building2, title: 'BS 8300', subtitle: 'Accessibility and inclusive design' },
  { Icon: Globe, title: 'BCF 2.1', subtitle: 'BIM collaboration format' },
  { Icon: GitBranch, title: 'IFC 4', subtitle: 'Open BIM data exchange' },
  { Icon: Ruler, title: 'BS EN 1992', subtitle: 'Eurocode 2 — structural concrete' },
];

export default function StandardsBadges() {
  return (
    <section id="standards" className="bg-navy px-6 py-20 text-white">
      <div className="mx-auto max-w-7xl">
        <motion.div
          initial={{ opacity: 0, y: 32 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true, margin: '-80px' }}
          transition={{ duration: 0.6, ease: 'easeOut' }}
          className="text-center"
        >
          <span className="text-sm font-semibold uppercase tracking-widest text-orange">
            Built on industry standards
          </span>
          <h2 className="mt-3 text-4xl font-bold text-white">
            Not adapted. Purpose-built.
          </h2>
          <p className="mx-auto mt-4 max-w-2xl text-lg text-white/60">
            Planscape is architected around the standards your projects already
            require — not bolted on as an afterthought.
          </p>
        </motion.div>

        <div className="mt-16 grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-4">
          {badges.map(({ Icon, title, subtitle }, i) => (
            <motion.div
              key={title}
              initial={{ opacity: 0, y: 24 }}
              whileInView={{ opacity: 1, y: 0 }}
              viewport={{ once: true, margin: '-40px' }}
              transition={{ duration: 0.5, delay: i * 0.06, ease: 'easeOut' }}
              className="group rounded-2xl border border-white/10 bg-white/5 p-6 transition-all duration-150 hover:border-orange/40 hover:bg-white/10"
            >
              <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-orange/15">
                <Icon size={22} className="text-orange" />
              </div>
              <h3 className="mt-4 text-base font-semibold text-white">{title}</h3>
              <p className="mt-1 text-sm text-white/50">{subtitle}</p>
            </motion.div>
          ))}
        </div>
      </div>
    </section>
  );
}
