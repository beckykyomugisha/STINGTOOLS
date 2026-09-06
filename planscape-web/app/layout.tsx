import './globals.css';
import type { Metadata } from 'next';
import type { ReactNode } from 'react';
import { AuthProvider } from '@/lib/auth';
import { NotificationsProvider } from '@/lib/notifications';
import { ThemeProvider, themeInitScript } from '@/lib/theme';
import { ToastProvider } from '@/components/ui';

export const metadata: Metadata = {
  title: 'Planscape — Coordination',
  description: 'Planscape online BIM coordination',
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="en" suppressHydrationWarning>
      <head>
        {/* U2 — set the theme class BEFORE first paint. Without this a dark-mode
            user gets a white flash on every hard navigation, because React can
            only add the class after hydration. suppressHydrationWarning on <html>
            is required precisely because this script mutates it first. */}
        <script dangerouslySetInnerHTML={{ __html: themeInitScript }} />
      </head>
      {/* Colours now come from the tokens (U1) via globals.css, not from
          hard-coded slate utilities that dark mode could never override. */}
      <body className="min-h-screen antialiased">
        <ThemeProvider>
          <AuthProvider>
            {/* U3 — ToastProvider wraps everything because the grid contract
                makes a failed optimistic save's toast the ONLY signal the user
                gets that their edit was rolled back. */}
            <ToastProvider>
              <NotificationsProvider>{children}</NotificationsProvider>
            </ToastProvider>
          </AuthProvider>
        </ThemeProvider>
      </body>
    </html>
  );
}
