'use client';

import { Linkedin, Twitter, Github } from 'lucide-react';

function FooterLogo() {
  return (
    <div className="flex items-center gap-2">
      <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-orange text-lg font-extrabold text-white">
        P
      </span>
      <span className="text-[28px] font-bold text-white">Planscape</span>
    </div>
  );
}

const cols: { heading: string; links: string[] }[] = [
  {
    heading: 'Platform',
    links: ['Features', 'Pricing', 'Standards', 'Changelog', 'Revit Plugin', 'API Docs'],
  },
  {
    heading: 'Company',
    links: ['About', 'Africa', 'Careers', 'Blog', 'Press Kit', 'Contact'],
  },
  {
    heading: 'Legal',
    links: ['Privacy Policy', 'Terms of Service', 'Cookie Policy', 'GDPR', 'Security'],
  },
];

export default function Footer() {
  return (
    <footer className="bg-navy-dark px-6 pb-8 pt-16 text-white">
      <div className="mx-auto max-w-7xl">
        <div className="grid grid-cols-1 gap-10 sm:grid-cols-2 lg:grid-cols-4">
          {/* Brand */}
          <div>
            <FooterLogo />
            <p className="mt-3 max-w-xs text-sm text-white/50">
              ISO 19650 BIM coordination for AEC teams — from design through to
              FM handover.
            </p>
            <div className="mt-6 flex gap-3">
              {[
                { Icon: Linkedin, label: 'LinkedIn' },
                { Icon: Twitter, label: 'Twitter' },
                { Icon: Github, label: 'GitHub' },
              ].map(({ Icon, label }) => (
                <a
                  key={label}
                  href="#"
                  aria-label={label}
                  className="flex h-10 w-10 items-center justify-center rounded-lg bg-white/10 transition-colors hover:bg-white/20"
                >
                  <Icon size={18} className="text-white" />
                </a>
              ))}
            </div>
          </div>

          {/* Link columns */}
          {cols.map((c) => (
            <div key={c.heading}>
              <h4 className="text-xs font-semibold uppercase tracking-widest text-white/40">
                {c.heading}
              </h4>
              <ul className="mt-4 space-y-3">
                {c.links.map((link) => (
                  <li key={link}>
                    <a
                      href="#"
                      className="text-sm text-white/70 transition-colors hover:text-white"
                    >
                      {link}
                    </a>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>

        <div className="mt-12 flex flex-col items-start justify-between gap-3 border-t border-white/10 pt-8 text-xs text-white/30 sm:flex-row sm:items-center">
          <span>© 2026 Planscape. All rights reserved.</span>
          <span>Built in Uganda 🇺🇬 · Serving AEC teams worldwide</span>
        </div>
      </div>
    </footer>
  );
}
