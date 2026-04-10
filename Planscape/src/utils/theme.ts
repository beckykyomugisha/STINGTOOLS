/** Corporate theme matching StingTools BIM Coordination Center colors */
export const theme = {
  colors: {
    primary: '#1A237E',       // Corporate navy (headers, nav)
    accent: '#E8912D',        // STING orange (actions, highlights)
    success: '#4CAF50',       // RAG green
    warning: '#FF9800',       // RAG amber
    danger: '#F44336',        // RAG red
    background: '#F5F5F5',    // Light grey background
    surface: '#FFFFFF',       // Card/surface white
    text: '#212121',          // Primary text
    textSecondary: '#757575', // Secondary text
    border: '#E0E0E0',       // Borders and dividers
    disabled: '#BDBDBD',     // Disabled state
    // CDE status colors
    cdeWIP: '#2196F3',       // Blue for WIP
    cdeSHARED: '#FF9800',    // Amber for SHARED
    cdePUBLISHED: '#4CAF50', // Green for PUBLISHED
    cdeARCHIVE: '#9E9E9E',   // Grey for ARCHIVE
    // Priority colors
    priorityCritical: '#D32F2F',
    priorityHigh: '#F44336',
    priorityMedium: '#FF9800',
    priorityLow: '#4CAF50',
  },
  spacing: {
    xs: 4,
    sm: 8,
    md: 16,
    lg: 24,
    xl: 32,
  },
  borderRadius: {
    sm: 4,
    md: 8,
    lg: 12,
    xl: 16,
  },
  fontSize: {
    xs: 10,
    sm: 12,
    md: 14,
    lg: 16,
    xl: 20,
    xxl: 24,
    hero: 32,
  },
} as const;

export function getCDEColor(status: string): string {
  switch (status) {
    case 'WIP': return theme.colors.cdeWIP;
    case 'SHARED': return theme.colors.cdeSHARED;
    case 'PUBLISHED': return theme.colors.cdePUBLISHED;
    case 'ARCHIVE': return theme.colors.cdeARCHIVE;
    default: return theme.colors.disabled;
  }
}

export function getPriorityColor(priority: string): string {
  switch (priority) {
    case 'CRITICAL': return theme.colors.priorityCritical;
    case 'HIGH': return theme.colors.priorityHigh;
    case 'MEDIUM': return theme.colors.priorityMedium;
    case 'LOW': return theme.colors.priorityLow;
    default: return theme.colors.disabled;
  }
}

export function getRAGColor(percent: number): string {
  if (percent >= 80) return theme.colors.success;
  if (percent >= 50) return theme.colors.warning;
  return theme.colors.danger;
}
