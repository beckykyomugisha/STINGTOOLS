'use client';

import { useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Check } from 'lucide-react';
import {
  BrowserChrome,
  DashboardMockup,
  IssuesMockup,
  PluginMockup,
} from '@/lib/mockScreenshots';

const tabs = ['Dashboard', 'Issue Tracker', 'Revit Plugin'] as const;
type Tab = (typeof tabs)[number];

export default function PlatformPreview() {
  const [active, setActive] = useState<Tab>('Dashboard');

  return (
    <section id="preview" className="bg-white px-6 py-24">
      <div className="mx-auto max-w-7xl">
        <motion.div
          initial={{ opacity: 0, y: 32 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true, margin: '-80px' }}
          transition={{ duration: 0.6, ease: 'easeOut' }}
          className="text-center"
        >
          <span className="text-sm font-semibold uppercase tracking-widest text-orange">
            Platform Preview
          </span>
          <h2 className="mt-3 text-4xl font-bold text-navy">
            See Planscape in action
          </h2>
          <p className="mt-2 text-lg text-muted">
            From the Revit plugin to cloud dashboard — everything connected.
          </p>
        </motion.div>

        {/* Tab switcher */}
        <div className="mt-12 flex justify-center">
          <div className="inline-flex rounded-full bg-slate-100 p-1">
            {tabs.map((t) => (
              <button
                key={t}
                onClick={() => setActive(t)}
                className={`rounded-full px-5 py-2 text-sm font-semibold transition-colors ${
                  active === t
                    ? 'bg-navy text-white'
                    : 'text-slate-600 hover:text-navy'
                }`}
              >
                {t}
              </button>
            ))}
          </div>
        </div>

        {/* Mock display */}
        <div className="mx-auto mt-8 max-w-5xl">
          <AnimatePresence mode="wait">
            <motion.div
              key={active}
              initial={{ opacity: 0, x: -20 }}
              animate={{ opacity: 1, x: 0 }}
              exit={{ opacity: 0, x: 20 }}
              transition={{ duration: 0.25, ease: 'easeOut' }}
            >
              {active === 'Dashboard' && (
                <BrowserChrome url="app.planscape.io/dashboard">
                  <DashboardMockup />
                </BrowserChrome>
              )}
              {active === 'Issue Tracker' && (
                <BrowserChrome url="app.planscape.io/issues">
                  <IssuesMockup />
                </BrowserChrome>
              )}
              {active === 'Revit Plugin' && (
                <div className="flex justify-center">
                  <div className="overflow-hidden rounded-xl border border-slate-200 shadow-2xl">
                    <PluginMockup />
                  </div>
                </div>
              )}
            </motion.div>
          </AnimatePresence>
        </div>

        <div className="mt-8 flex flex-wrap items-center justify-center gap-x-6 gap-y-2 text-sm text-muted">
          {[
            'Real project data',
            'No setup required',
            'Revit 2025/2026/2027',
          ].map((s) => (
            <span key={s} className="flex items-center gap-1.5">
              <Check size={14} className="text-success" />
              {s}
            </span>
          ))}
        </div>
      </div>
    </section>
  );
}
