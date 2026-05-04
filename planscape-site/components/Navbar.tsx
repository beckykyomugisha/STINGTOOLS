'use client';

import { useEffect, useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Menu, X } from 'lucide-react';

const links = [
  { label: 'Features', href: '#features' },
  { label: 'Standards', href: '#standards' },
  { label: 'Pricing', href: '#pricing' },
  { label: 'Africa', href: '#africa' },
];

function Logo({ size = 20 }: { size?: number }) {
  return (
    <a href="#hero" className="flex items-center gap-2">
      <span
        className="flex items-center justify-center rounded-lg bg-orange font-extrabold text-white"
        style={{ width: size + 8, height: size + 8, fontSize: size - 2 }}
      >
        P
      </span>
      <span className="font-bold text-white" style={{ fontSize: size }}>
        Planscape
      </span>
    </a>
  );
}

export default function Navbar() {
  const [scrolled, setScrolled] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 60);
    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });
    return () => window.removeEventListener('scroll', onScroll);
  }, []);

  return (
    <>
      <motion.header
        initial={false}
        animate={{
          backgroundColor: scrolled ? 'rgba(26, 31, 94, 1)' : 'rgba(26, 31, 94, 0)',
          backdropFilter: scrolled ? 'blur(8px)' : 'blur(0px)',
        }}
        transition={{ duration: 0.3, ease: 'easeOut' }}
        className="fixed inset-x-0 top-0 z-50 w-full"
      >
        <div className="mx-auto flex max-w-7xl items-center justify-between px-6 py-4">
          <Logo />

          {/* Center links */}
          <nav className="hidden items-center gap-8 md:flex">
            {links.map((l) => (
              <a
                key={l.href}
                href={l.href}
                className="relative text-sm font-medium text-white transition-colors hover:text-orange"
              >
                <span className="group">
                  {l.label}
                  <span className="absolute -bottom-1 left-0 h-0.5 w-0 bg-orange transition-all duration-200 group-hover:w-full" />
                </span>
              </a>
            ))}
          </nav>

          {/* Right buttons */}
          <div className="hidden items-center gap-3 md:flex">
            <a
              href="/"
              className="rounded-lg border border-white/40 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-white hover:text-navy"
            >
              Sign In
            </a>
            <a
              href="#"
              className="rounded-lg bg-orange px-5 py-2 text-sm font-semibold text-white shadow-md transition-colors hover:bg-orange-dark"
            >
              Request Demo
            </a>
          </div>

          {/* Mobile hamburger */}
          <button
            type="button"
            onClick={() => setMobileOpen(true)}
            aria-label="Open menu"
            className="md:hidden rounded-lg p-2 text-white"
          >
            <Menu size={24} />
          </button>
        </div>
      </motion.header>

      {/* Mobile overlay */}
      <AnimatePresence>
        {mobileOpen && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.2 }}
            className="fixed inset-0 z-[60] flex flex-col bg-navy md:hidden"
          >
            <div className="flex items-center justify-between px-6 py-4">
              <Logo />
              <button
                type="button"
                onClick={() => setMobileOpen(false)}
                aria-label="Close menu"
                className="rounded-lg p-2 text-white"
              >
                <X size={24} />
              </button>
            </div>
            <nav className="flex flex-col gap-2 px-6 pt-8">
              {links.map((l) => (
                <a
                  key={l.href}
                  href={l.href}
                  onClick={() => setMobileOpen(false)}
                  className="rounded-lg px-4 py-4 text-2xl font-semibold text-white hover:bg-white/5"
                >
                  {l.label}
                </a>
              ))}
              <a
                href="/"
                onClick={() => setMobileOpen(false)}
                className="mt-6 rounded-lg border border-white/40 px-4 py-3 text-center text-base font-medium text-white"
              >
                Sign In
              </a>
              <a
                href="#"
                onClick={() => setMobileOpen(false)}
                className="rounded-lg bg-orange px-5 py-3 text-center text-base font-semibold text-white"
              >
                Request Demo
              </a>
            </nav>
          </motion.div>
        )}
      </AnimatePresence>
    </>
  );
}
