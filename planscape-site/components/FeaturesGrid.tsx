'use client';

import { motion } from 'framer-motion';
import {
  Tag,
  FileText,
  Layout,
  AlertTriangle,
  BarChart2,
  Link as LinkIcon,
  type LucideIcon,
} from 'lucide-react';

type Feature = {
  Icon: LucideIcon;
  title: string;
  body: string;
  badge: string;
  iconBg: string;
  iconColor: string;
};

const features: Feature[] = [
  {
    Icon: Tag,
    title: 'Smart Asset Tagging',
    body:
      'ISO 19650 eight-segment tags auto-populated directly from Revit geometry, MEP systems, and spatial data. Zero manual entry.',
    badge: '763 commands',
    iconBg: 'bg-orange/10',
    iconColor: 'text-orange',
  },
  {
    Icon: FileText,
    title: 'Document Control',
    body:
      'Full CDE state machine — WIP to Shared to Published to Archived. Transmittals, RFIs, BOQ export, and FM handover in one place.',
    badge: 'ISO 19650-2',
    iconBg: 'bg-blue-500/10',
    iconColor: 'text-blue-500',
  },
  {
    Icon: Layout,
    title: 'Drawing Manager',
    body:
      '40 corporate drawing types with scope-box binding, auto-crop, viewport packing, and title-block parameter stamping.',
    badge: '40 drawing types',
    iconBg: 'bg-purple-500/10',
    iconColor: 'text-purple-500',
  },
  {
    Icon: AlertTriangle,
    title: 'Issue Tracking',
    body:
      'BCF-compatible RFIs and NCRs with SLA tracking, photo attachments, assignee workflows, and full audit trail.',
    badge: 'BCF 2.1',
    iconBg: 'bg-red-500/10',
    iconColor: 'text-red-500',
  },
  {
    Icon: BarChart2,
    title: 'Compliance Dashboard',
    body:
      'Real-time RAG scoring per discipline. Trend analysis, per-token compliance breakdown, and automated morning briefing on model open.',
    badge: 'Real-time',
    iconBg: 'bg-green-500/10',
    iconColor: 'text-green-500',
  },
  {
    Icon: LinkIcon,
    title: 'Platform Integration',
    body:
      'Connect to Autodesk Construction Cloud, Procore, SharePoint, and Trimble. BCF export, IFC property maps, and CDE sync.',
    badge: 'ACC · Procore · IFC',
    iconBg: 'bg-amber-500/10',
    iconColor: 'text-amber-500',
  },
];

export default function FeaturesGrid() {
  return (
    <section id="features" className="bg-slate-50 px-6 py-24">
      <div className="mx-auto max-w-7xl">
        <motion.div
          initial={{ opacity: 0, y: 32 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true, margin: '-80px' }}
          transition={{ duration: 0.6, ease: 'easeOut' }}
          className="text-center"
        >
          <span className="text-sm font-semibold uppercase tracking-widest text-orange">
            Platform Capabilities
          </span>
          <h2 className="mt-3 text-4xl font-bold text-navy">
            Everything your BIM team needs
          </h2>
          <p className="mt-2 text-lg text-muted">
            One platform replacing five disconnected tools.
          </p>
        </motion.div>

        <div className="mt-16 grid grid-cols-1 gap-6 md:grid-cols-2 lg:grid-cols-3">
          {features.map(({ Icon, title, body, badge, iconBg, iconColor }, i) => (
            <motion.div
              key={title}
              initial={{ opacity: 0, y: 32 }}
              whileInView={{ opacity: 1, y: 0 }}
              viewport={{ once: true, margin: '-50px' }}
              transition={{ duration: 0.5, delay: i * 0.08, ease: 'easeOut' }}
              className="group rounded-2xl border border-slate-200 bg-white p-8 transition-all duration-200 hover:border-orange/40 hover:shadow-lg"
            >
              <div
                className={`flex h-12 w-12 items-center justify-center rounded-xl ${iconBg}`}
              >
                <Icon className={iconColor} size={22} />
              </div>
              <h3 className="mt-5 text-xl font-bold text-navy">{title}</h3>
              <p className="mt-3 text-base leading-relaxed text-slate-600">
                {body}
              </p>
              <span className="mt-5 inline-block rounded-full bg-slate-100 px-3 py-1 text-xs font-semibold text-slate-700">
                {badge}
              </span>
            </motion.div>
          ))}
        </div>
      </div>
    </section>
  );
}
