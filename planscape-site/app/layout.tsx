import type { Metadata } from 'next';
import { Inter } from 'next/font/google';
import './globals.css';

const inter = Inter({
  subsets: ['latin'],
  variable: '--font-inter',
  display: 'swap',
});

export const metadata: Metadata = {
  title: 'Planscape — ISO 19650 BIM Coordination Platform',
  description:
    'Planscape unifies asset tagging, document control, and site coordination for AEC teams. Built for ISO 19650. Serving East & West Africa and international markets.',
  openGraph: {
    title: 'Planscape — ISO 19650 BIM Coordination Platform',
    description:
      'Planscape unifies asset tagging, document control, and site coordination for AEC teams. Built for ISO 19650.',
    type: 'website',
  },
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en" className={inter.variable}>
      <body className="font-sans">{children}</body>
    </html>
  );
}
