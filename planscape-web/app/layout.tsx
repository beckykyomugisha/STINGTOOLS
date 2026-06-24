import './globals.css';
import type { Metadata } from 'next';
import type { ReactNode } from 'react';
import { AuthProvider } from '@/lib/auth';
import { NotificationsProvider } from '@/lib/notifications';

export const metadata: Metadata = {
  title: 'Planscape — Coordination',
  description: 'Planscape online BIM coordination',
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="en">
      <body className="min-h-screen bg-slate-50 text-slate-900 antialiased">
        <AuthProvider>
          <NotificationsProvider>{children}</NotificationsProvider>
        </AuthProvider>
      </body>
    </html>
  );
}
