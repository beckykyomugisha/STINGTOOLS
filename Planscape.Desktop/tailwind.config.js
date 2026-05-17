/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ['./src/renderer/**/*.{html,ts,tsx}'],
  theme: {
    extend: {
      colors: {
        // Planscape brand palette
        'ps-bg':        '#1a1a2e',
        'ps-surface':   '#16213e',
        'ps-elevated':  '#0f3460',
        'ps-accent':    '#E8912D',
        'ps-accent-dim':'#c4771f',
        'ps-text':      '#e2e8f0',
        'ps-muted':     '#94a3b8',
        'ps-border':    '#2d3748',
        // Status
        'ps-green':     '#22c55e',
        'ps-red':       '#ef4444',
        'ps-amber':     '#f59e0b',
        'ps-blue':      '#3b82f6',
        // CDE states
        'cde-wip':      '#6366f1',
        'cde-shared':   '#f59e0b',
        'cde-published':'#22c55e',
        'cde-archived': '#6b7280'
      },
      fontFamily: {
        sans: ['Inter', 'system-ui', '-apple-system', 'sans-serif'],
        mono: ['JetBrains Mono', 'Fira Code', 'monospace']
      },
      animation: {
        'spin-slow': 'spin 2s linear infinite',
        'pulse-soft': 'pulse 3s cubic-bezier(0.4, 0, 0.6, 1) infinite'
      }
    }
  },
  plugins: []
}
