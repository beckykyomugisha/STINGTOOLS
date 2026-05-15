'use client';

import { motion } from 'framer-motion';
import { Check, X } from 'lucide-react';

type Feature = { text: string; included: boolean };

type Plan = {
  name: string;
  price: string;
  priceUnit?: string;
  subtext: string;
  features: Feature[];
  cta: string;
  ctaSub?: string;
  highlighted?: boolean;
  ctaStyle: 'primary' | 'outline';
};

const plans: Plan[] = [
  {
    name: 'Small',
    price: '$35',
    priceUnit: '/firm/month',
    subtext: 'For small practices just getting started with BIM coordination.',
    features: [
      { text: 'Up to 6 users (1 author + 5 coordinators)', included: true },
      { text: 'Up to 5 active projects', included: true },
      { text: 'Full Revit plugin suite', included: true },
      { text: 'Cloud sync & real-time dashboard', included: true },
      { text: 'Issue tracker (BCF 2.1)', included: true },
      { text: 'ISO 19650 compliance dashboard', included: true },
      { text: 'Mobile app (iOS & Android)', included: true },
      { text: 'SSO / SAML', included: false },
      { text: 'On-premise deployment', included: false },
    ],
    cta: 'Start 30-Day Trial',
    ctaSub: 'No credit card required',
    ctaStyle: 'outline',
  },
  {
    name: 'Medium',
    price: '$55',
    priceUnit: '/firm/month',
    subtext: 'For growing practices managing multiple live projects.',
    features: [
      { text: 'Up to 12 users (1 author + 11 coordinators)', included: true },
      { text: 'Up to 10 active projects', included: true },
      { text: 'Full Revit plugin suite', included: true },
      { text: 'Cloud sync & real-time dashboard', included: true },
      { text: 'Issue tracker (BCF 2.1)', included: true },
      { text: 'Document control (CDE)', included: true },
      { text: 'Email & in-app notifications', included: true },
      { text: 'SSO / SAML', included: false },
      { text: 'On-premise deployment', included: false },
    ],
    cta: 'Start 30-Day Trial',
    ctaSub: 'No credit card required',
    highlighted: true,
    ctaStyle: 'primary',
  },
  {
    name: 'Large',
    price: '$90',
    priceUnit: '/firm/month',
    subtext: 'For established firms coordinating across multiple disciplines.',
    features: [
      { text: 'Up to 20 users (1 author + 19 coordinators)', included: true },
      { text: 'Unlimited active projects', included: true },
      { text: 'Full Revit plugin suite', included: true },
      { text: 'Cloud sync & real-time dashboard', included: true },
      { text: 'Issue tracker + Document control (CDE)', included: true },
      { text: 'Email & in-app notifications', included: true },
      { text: 'Priority support', included: true },
      { text: 'SSO / SAML', included: false },
      { text: 'On-premise deployment', included: false },
    ],
    cta: 'Start 30-Day Trial',
    ctaSub: 'No credit card required',
    ctaStyle: 'outline',
  },
  {
    name: 'Enterprise',
    price: 'Custom',
    subtext: 'For large contractors, government bodies, and developer clients.',
    features: [
      { text: 'Unlimited users & projects', included: true },
      { text: 'SSO / SAML authentication', included: true },
      { text: 'On-premise or private cloud', included: true },
      { text: 'Dedicated implementation support', included: true },
      { text: 'SLA guarantees', included: true },
      { text: 'Custom integrations (ACC, Procore, Aconex)', included: true },
      { text: 'NGO / Government 15% discount', included: true },
      { text: 'World Bank / AfDB BIM compliance package', included: true },
    ],
    cta: 'Talk to Sales',
    ctaStyle: 'outline',
  },
];

export default function PricingCards() {
  return (
    <section id="pricing" className="bg-white px-6 py-24">
      <div className="mx-auto max-w-7xl">
        <motion.div
          initial={{ opacity: 0, y: 32 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true, margin: '-80px' }}
          transition={{ duration: 0.6, ease: 'easeOut' }}
          className="text-center"
        >
          <span className="text-sm font-semibold uppercase tracking-widest text-orange">
            Pricing
          </span>
          <h2 className="mt-3 text-4xl font-bold text-navy">
            Simple, transparent pricing
          </h2>
          <p className="mt-2 text-lg text-muted">
            Start free. Scale as your team grows.
          </p>
        </motion.div>

        <div className="mt-16 grid grid-cols-1 gap-6 lg:grid-cols-4 lg:gap-8">
          {plans.map((p, i) => {
            const isPro = p.highlighted;
            return (
              <motion.div
                key={p.name}
                initial={{ opacity: 0, y: 32 }}
                whileInView={{ opacity: 1, y: 0 }}
                viewport={{ once: true, margin: '-50px' }}
                transition={{ duration: 0.5, delay: i * 0.1, ease: 'easeOut' }}
                className={`relative flex flex-col rounded-2xl p-8 ${
                  isPro
                    ? 'border-2 border-orange bg-orange/[0.03] lg:scale-[1.02]'
                    : 'border border-slate-200 bg-white'
                }`}
              >
                {isPro && (
                  <span className="absolute -top-4 left-1/2 -translate-x-1/2 rounded-full bg-orange px-4 py-1 text-xs font-bold uppercase tracking-wider text-white">
                    Most Popular
                  </span>
                )}

                <h3 className="text-xl font-bold text-navy">{p.name}</h3>

                <div className="mt-4 flex items-baseline gap-1">
                  <span
                    className={`font-extrabold text-navy ${
                      p.price === 'Custom' ? 'text-4xl' : 'text-5xl'
                    }`}
                  >
                    {p.price}
                  </span>
                  {p.priceUnit && (
                    <span className="text-base text-muted">{p.priceUnit}</span>
                  )}
                </div>

                <p className="mt-3 text-sm text-slate-600">{p.subtext}</p>

                <div className="my-6 border-t border-slate-200" />

                <ul className="space-y-3">
                  {p.features.map((f) => (
                    <li
                      key={f.text}
                      className={`flex items-start gap-2 text-sm ${
                        f.included
                          ? 'text-slate-700'
                          : 'text-slate-400 line-through'
                      }`}
                    >
                      {f.included ? (
                        <Check size={16} className="mt-0.5 shrink-0 text-success" />
                      ) : (
                        <X size={16} className="mt-0.5 shrink-0 text-slate-300" />
                      )}
                      <span>{f.text}</span>
                    </li>
                  ))}
                </ul>

                <div className="mt-8 flex-1" />

                <a
                  href="#"
                  className={`block w-full rounded-lg px-5 py-3 text-center text-sm font-semibold transition-colors ${
                    p.ctaStyle === 'primary'
                      ? 'bg-orange text-white hover:bg-orange-dark'
                      : 'border border-navy text-navy hover:bg-navy hover:text-white'
                  }`}
                >
                  {p.cta}
                </a>

                {p.ctaSub && (
                  <p className="mt-2 text-center text-xs text-muted">
                    {p.ctaSub}
                  </p>
                )}
              </motion.div>
            );
          })}
        </div>

        <p className="mx-auto mt-8 max-w-2xl text-center text-sm text-muted">
          All plans include the full Revit 2025/2026/2027 plugin and offline-first mobile app.
          Pay annually — get 1 month free. Invoiced in USD, UGX, KES, TZS, NGN, RWF, or ZAR.
          NGO &amp; government: 15% discount.
        </p>
      </div>
    </section>
  );
}
